using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Bacon.Caching.Services;

internal sealed partial class CacheService<TItem>(IOptions<CacheServerOptions> cacheServerOptionsOptions, IOptions<CacheOptions<TItem>> cacheOptionsOptions, IMemoryCache memoryCache, ILogger<TItem> logger, IDistributedCacheService<TItem>? distributedCacheService = null)
    : ICacheService<TItem>
{
    private readonly CacheServerOptions _cacheServerOptions = cacheServerOptionsOptions.Value;
    private readonly CacheOptions<TItem> _cacheOptions = cacheOptionsOptions.Value;
    private readonly ConcurrentDictionary<string, InFlightLoad<TItem>> _inFlight = new();

    #region Public

    #region Get

    #region Get

    public async ValueTask<TItem?> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default) => await GetAsync(string.Empty, tenantId: tenantId, cancellationToken: cancellationToken);

    [OverloadResolutionPriority(1)]
    public async ValueTask<TItem?> GetAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(GetAsync);

        TItem? defaultValue = default;

        if (IsCacheDisabled(source))
        {
            return defaultValue;
        }

        #endregion Validations

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        return await GetSingleItemAsync(source, cacheKey, cancellationToken);
    }

    public async ValueTask<IReadOnlyDictionary<TSuffix, TItem?>> GetItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffixes, nameof(cacheKeySuffixes));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(GetItemsAsync);

        if (!cacheKeySuffixes.Any())
        {
            LogNoItemsToCacheDebug(source);
            return new Dictionary<TSuffix, TItem?>();
        }

        TSuffix[] typedSuffixes = [.. cacheKeySuffixes.Distinct()];

        ValidateCacheKeyNullSuffixes(typedSuffixes, nameof(cacheKeySuffixes));

        TItem? defaultValue = default;

        Dictionary<string, TItem?> items = [];
        Dictionary<string, TSuffix> relations = [];
        foreach (TSuffix cacheKeySuffix in typedSuffixes)
        {
            string key = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

            items.Add(key, defaultValue);
            relations.Add(key, cacheKeySuffix);
        }

        if (IsCacheDisabled(source))
        {
            return MapCachedItems(items, relations);
        }

        #endregion Validations

        items = await GetManyItemsAsync(source, items, cancellationToken);

        return MapCachedItems(items, relations);
    }

    #endregion Get

    #region GetOrCreate

    public async ValueTask<TItem?> GetOrCreateAsync(Func<CancellationToken, ValueTask<TItem?>> factory, string? tenantId = null, CancellationToken cancellationToken = default) => await GetOrCreateAsync(string.Empty, factory, tenantId: tenantId, cancellationToken: cancellationToken);

    [OverloadResolutionPriority(1)]
    public async ValueTask<TItem?> GetOrCreateAsync<TSuffix>(TSuffix cacheKeySuffix, Func<CancellationToken, ValueTask<TItem?>> factory, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(GetOrCreateAsync);

        if (IsCacheDisabled(source))
        {
            return await factory(cancellationToken);
        }

        #endregion Validations

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        #region Initial Cache Check

        TItem? item = await GetSingleItemAsync(source, cacheKey, cancellationToken);

        if (!IsMissing(item))
        {
            return item;
        }

        #endregion Initial Cache Check

        Dictionary<TSuffix, TItem?> missingItems = await LoadMissingItemsAsync(source, [(cacheKeySuffix, cacheKey)], async (_, ct) =>
        {
            TItem? freshItem = await factory(ct);

            // A null item means no data: leave the key out, like a plural factory does for a suffix it did not find
            Dictionary<TSuffix, TItem> found = [];
            if (freshItem is not null)
            {
                found.Add(cacheKeySuffix, freshItem);
            }

            return found;
        }, cancellationToken);

        return missingItems.TryGetValue(cacheKeySuffix, out TItem? loadedItem) ? loadedItem : default;
    }

    public async ValueTask<IReadOnlyDictionary<TSuffix, TItem?>> GetOrCreateItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, Func<IReadOnlyCollection<TSuffix>, CancellationToken, ValueTask<IReadOnlyDictionary<TSuffix, TItem>>> factory, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffixes, nameof(cacheKeySuffixes));
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(GetOrCreateItemsAsync);

        TSuffix[] typedSuffixes = [.. cacheKeySuffixes.Distinct()];

        if (typedSuffixes.Length == 0)
        {
            LogNoItemsToCacheDebug(source);
            return new Dictionary<TSuffix, TItem?>();
        }

        ValidateCacheKeyNullSuffixes(typedSuffixes, nameof(cacheKeySuffixes));

        if (IsCacheDisabled(source))
        {
            IReadOnlyDictionary<TSuffix, TItem> freshItems = await factory(typedSuffixes, cancellationToken);

            Dictionary<TSuffix, TItem?> noCacheResult = new(typedSuffixes.Length);
            foreach (TSuffix suffix in typedSuffixes)
            {
                noCacheResult[suffix] = freshItems.TryGetValue(suffix, out TItem? loadedItem) ? loadedItem : default;
            }

            return noCacheResult;
        }

        #endregion Validations

        // Build every key once: the read core and the load share them.
        Dictionary<string, TItem?> items = [];
        Dictionary<string, TSuffix> relations = [];
        foreach (TSuffix suffix in typedSuffixes)
        {
            string key = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, suffix);

            items.Add(key, default);
            relations.Add(key, suffix);
        }

        #region Initial Cache Check

        items = await GetManyItemsAsync(source, items, cancellationToken);

        List<(TSuffix Suffix, string Key)> missing = [.. items.Where(w => IsMissing(w.Value)).Select(s => (relations[s.Key], s.Key))];

        Dictionary<TSuffix, TItem?> result = MapCachedItems(items, relations);

        if (missing.Count == 0)
        {
            return result;
        }

        #endregion Initial Cache Check

        Dictionary<TSuffix, TItem?> missingItems = await LoadMissingItemsAsync(source, missing, factory, cancellationToken);

        foreach (KeyValuePair<TSuffix, TItem?> missingItem in missingItems)
        {
            result[missingItem.Key] = missingItem.Value;
        }

        return result;
    }

    #endregion GetOrCreate

    #endregion Get

    #region Set

    public async ValueTask SetAsync(TItem item, string? tenantId = null, CancellationToken cancellationToken = default) => await SetAsync(item, cacheKeySuffix: string.Empty, tenantId: tenantId, cancellationToken: cancellationToken);

    [OverloadResolutionPriority(1)]
    public async ValueTask SetAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));
        ArgumentNullException.ThrowIfNull(item, nameof(item));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(SetAsync);

        if (IsCacheDisabled(source))
        {
            return;
        }

        #endregion Validations

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        await SetSingleItemAsync(source, cacheKey, item, cancellationToken);
    }

    public async ValueTask SetItemsAsync<TSuffix>(IReadOnlyDictionary<TSuffix, TItem> items, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(items, nameof(items));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(SetItemsAsync);

        if (!items.Any())
        {
            LogNoItemsToCacheDebug(source);
            return;
        }

        if (IsCacheDisabled(source))
        {
            return;
        }

        TSuffix[] typedSuffixes = [.. items.Keys];

        ValidateCacheKeyNullSuffixes(typedSuffixes, nameof(items));
        ValidateNullValues(items);

        #endregion Validations

        Dictionary<string, TItem> keyedItems = new(items.Count);
        foreach (KeyValuePair<TSuffix, TItem> item in items)
        {
            keyedItems.Add(GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, item.Key), item.Value);
        }

        await SetManyItemsAsync(source, keyedItems, cancellationToken);
    }

    #endregion Set

    #region Remove

    public async ValueTask RemoveAsync(string? tenantId = null, CancellationToken cancellationToken = default) => await RemoveAsync(cacheKeySuffix: string.Empty, tenantId: tenantId, cancellationToken: cancellationToken);

    [OverloadResolutionPriority(1)]
    public async ValueTask RemoveAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(RemoveAsync);

        if (IsCacheDisabled(source))
        {
            return;
        }

        #endregion Validations

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        #region Distributed Cache

        if (IsDistributedCacheEnabled(source))
        {
            try
            {
                ValidateDistributedCache();

                await distributedCacheService!.RemoveAsync(cacheKey, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDistributedCacheError(source, ex);
            }
        }

        #endregion Distributed Cache

        #region Memory Cache

        if (IsMemoryCacheEnabled(source))
        {
            try
            {
                memoryCache.Remove(cacheKey);
            }
            catch (Exception ex)
            {
                LogMemoryCacheError(source, ex);
            }
        }

        #endregion Memory Cache
    }

    public async ValueTask RemoveItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(cacheKeySuffixes, nameof(cacheKeySuffixes));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(RemoveItemsAsync);

        if (!cacheKeySuffixes.Any())
        {
            LogNoItemsToCacheDebug(source);
            return;
        }

        if (IsCacheDisabled(source))
        {
            return;
        }

        TSuffix[] typedSuffixes = [.. cacheKeySuffixes.Distinct()];

        ValidateCacheKeyNullSuffixes(typedSuffixes, nameof(cacheKeySuffixes));

        #endregion Validations

        string[] cacheKeys = [.. typedSuffixes.Select(s => GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, s))];

        #region Distributed Cache

        if (IsDistributedCacheEnabled(source))
        {
            try
            {
                ValidateDistributedCache();

                await distributedCacheService!.RemoveItemsAsync(cacheKeys, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDistributedCacheError(source, ex);
            }
        }

        #endregion Distributed Cache

        #region Memory Cache

        if (IsMemoryCacheEnabled(source))
        {
            try
            {
                foreach (string cacheKey in cacheKeys)
                {
                    memoryCache.Remove(cacheKey);
                }
            }
            catch (Exception ex)
            {
                LogMemoryCacheError(source, ex);
            }
        }

        #endregion Memory Cache
    }

    #endregion Remove

    #endregion Public

    #region Privates

    #region Global

    private string GetCacheKey<TSuffix>(CacheScopes cacheScope, string? tenantId, string cacheKey, TSuffix? cacheKeySuffix)
    {
        string suffix = cacheKeySuffix?.ToString()?.ToLowerInvariant() ?? string.Empty;

        if (cacheScope == CacheScopes.Global)
        {
            if (tenantId is not null)
            {
                throw new ArgumentException("Caching - A global cache item cannot be accessed with a tenant ID", nameof(tenantId));
            }

            return $"|{cacheKey}{suffix}";
        }

        if (tenantId is not null)
        {
            TenantValidationService.ValidateTenantId(tenantId);
        }

        string _tenantId = (tenantId ?? _cacheServerOptions.TenantId) ?? throw new ArgumentNullException(nameof(tenantId), "The tenant ID cannot be null");

        ArgumentException.ThrowIfNullOrWhiteSpace(_tenantId, nameof(tenantId));

        return $"{_tenantId}_{cacheKey}{suffix}";
    }

    private static Dictionary<TSuffix, TItem?> MapCachedItems<TSuffix>(IDictionary<string, TItem?> items, Dictionary<string, TSuffix> relations) where TSuffix : notnull
    {
        return items.ToDictionary(key => relations[key.Key], value => value.Value);
    }

    #region Enable Check

    private bool IsCacheDisabled(string source)
    {
        if (_cacheOptions.MemoryCacheDuration == TimeSpan.Zero && _cacheOptions.DistributedCacheDuration == TimeSpan.Zero)
        {
            LogCacheDisabled(source);
            return true;
        }

        return false;
    }

    private bool IsMemoryCacheEnabled(string source, bool isWrite = false)
    {
        if (_cacheOptions.MemoryCacheDuration == TimeSpan.Zero)
        {
            if (isWrite)
            {
                LogMemoryCacheDisabledForWriting(source);
            }
            else
            {
                LogMemoryCacheDisabled(source);
            }
            
            return false;
        }

        return true;
    }

    private bool IsDistributedCacheEnabled(string source)
    {
        if (_cacheOptions.DistributedCacheDuration == TimeSpan.Zero)
        {
            LogDistributedCacheDisabled(source);
            return false;
        }

        return true;
    }

    #endregion Disable Check

    #region Validations

    private static bool IsMissing(TItem? item)
    {
        return item is null || item.Equals(default(TItem));
    }

    private static void ValidateCacheKeyNullSuffixes<TSuffix>(TSuffix[] cacheKeySuffixes, string source) where TSuffix : notnull
    {
        foreach (TSuffix cacheKeySuffix in cacheKeySuffixes)
        {
            if (string.IsNullOrWhiteSpace(cacheKeySuffix?.ToString()))
            {
                throw new ArgumentException($"The {source} cannot contain null values or values that convert to an empty string", source);
            }
        }
    }

    private void ValidateDistributedCache()
    {
        if (distributedCacheService == null)
        {
            throw new InvalidOperationException("No distributed cache provider is registered. Either register one or set DistributedCacheDuration to TimeSpan.Zero");
        }
    }

    private static void ValidateNullValues<TSuffix>(IReadOnlyDictionary<TSuffix, TItem> items) where TSuffix : notnull
    {
        IEnumerable<TSuffix> invalidItems = items.Where(w => w.Value == null).Select(s => s.Key);

        if (invalidItems.Any())
        {
            throw new ArgumentNullException(null, $"The following keys contain invalid null values: {string.Join(", ", invalidItems)}");
        }
    }

    #endregion Validations

    #endregion Global

    #region Get

    #region Core

    #region GetAsync

    private async ValueTask<TItem?> GetSingleItemAsync(string source, string cacheKey, CancellationToken cancellationToken)
    {
        TItem? item = default;

        #region Memory Cache

        if (IsMemoryCacheEnabled(source))
        {
            item = GetSingleItem(source, cacheKey);
        }

        if (!IsMissing(item))
        {
            return item;
        }

        #endregion Memory Cache

        LogNoMemoryItemCachedDebug(source);

        #region Distributed Cache + Memory Backfill

        if (IsDistributedCacheEnabled(source))
        {
            item = await GetDistributedSingleAndBackFillAsync(source, cacheKey, cancellationToken);
        }

        #endregion Distributed Cache + Memory Backfill

        return item;
    }

    private TItem? GetSingleItem(string source, string cacheKey)
    {
        TItem? item = default;

        try
        {
            item = memoryCache.Get<TItem>(cacheKey);
        }
        catch (Exception ex)
        {
            LogMemoryCacheError(source, ex);
        }

        return item;
    }

    private async ValueTask<TItem?> GetDistributedSingleAndBackFillAsync(string source, string cacheKey, CancellationToken cancellationToken)
    {
        TItem? item = default;

        try
        {
            ValidateDistributedCache();

            item = await distributedCacheService!.GetAsync(cacheKey, cancellationToken: cancellationToken);

            if (item == null)
            {
                LogNoDistributedItemCachedDebug(source);
                return item;
            }

            if (IsMemoryCacheEnabled(source, isWrite: true))
            {
                SetMemoryCache(cacheKey, item, _cacheOptions.MemoryCacheDuration);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDistributedCacheError(source, ex);
        }

        return item;
    }

    #endregion GetAsync

    #region GetItemsAsync

    private async ValueTask<Dictionary<string, TItem?>> GetManyItemsAsync(string source, IReadOnlyDictionary<string, TItem?> items, CancellationToken cancellationToken)
    {
        Dictionary<string, TItem?> loaded = IsMemoryCacheEnabled(source) ? GetManyItems(source, items) : new(items);

        string[] missedKeysFromMemory = [.. loaded.Where(w => IsMissing(w.Value)).Select(s => s.Key)];

        bool isDistributedCacheEnabled = IsDistributedCacheEnabled(source);

        #region Distributed Cache + Memory Backfill

        if (isDistributedCacheEnabled && missedKeysFromMemory.Length == 0)
        {
            LogNoMissedMemoryCacheItemsDebug(source);
        }
        else if (isDistributedCacheEnabled && missedKeysFromMemory.Length != 0)
        {
            loaded = await GetDistributedManyAndBackFillAsync(source, missedKeysFromMemory, loaded, cancellationToken);
        }

        #endregion Distributed Cache + Memory Backfill

        return loaded;
    }

    private Dictionary<string, TItem?> GetManyItems(string source, IReadOnlyDictionary<string, TItem?> items)
    {
        Dictionary<string, TItem?> loaded = new(items);

        try
        {
            foreach (KeyValuePair<string, TItem?> keyValuePair in items)
            {
                loaded[keyValuePair.Key] = memoryCache.Get<TItem>(keyValuePair.Key);
            }
        }
        catch (Exception ex)
        {
            LogMemoryCacheError(source, ex);
        }

        return loaded;
    }

    private async ValueTask<Dictionary<string, TItem?>> GetDistributedManyAndBackFillAsync(string source, string[] missedKeysFromMemory, IReadOnlyDictionary<string, TItem?> items, CancellationToken cancellationToken)
    {
        Dictionary<string, TItem?> loaded = new(items);

        try
        {
            ValidateDistributedCache();

            Dictionary<string, TItem?> distributedCacheValues = await distributedCacheService!.GetItemsAsync(missedKeysFromMemory.AsReadOnly(), cancellationToken: cancellationToken);

            if (distributedCacheValues.Count == 0)
            {
                LogNoDistributedItemCachedDebug(source);
            }
            else
            {
                bool isMemoryCacheEnabled = IsMemoryCacheEnabled(source, isWrite: true);

                MemoryCacheEntryOptions? memoryCacheEntryOptions = null;
                if (isMemoryCacheEnabled)
                {
                    LogSyncMemoryCacheFromDistributedCacheDebug(source);

                    memoryCacheEntryOptions ??= new()
                    {
                        SlidingExpiration = null,
                        AbsoluteExpirationRelativeToNow = _cacheOptions.MemoryCacheDuration
                    };
                }

                foreach (KeyValuePair<string, TItem?> distributedCacheValue in distributedCacheValues)
                {
                    if (isMemoryCacheEnabled && distributedCacheValue.Value is not null)
                    {
                        SetMemoryCache(distributedCacheValue.Key, distributedCacheValue.Value, _cacheOptions.MemoryCacheDuration, memoryCacheEntryOptions: memoryCacheEntryOptions);
                    }

                    loaded[distributedCacheValue.Key] = distributedCacheValue.Value;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDistributedCacheError(source, ex);
        }

        return loaded;
    }

    #endregion GetItemsAsync

    #endregion

    #region Load

    private async Task LoadItemsAsync<TSuffix>(string source, LoadScope scope, List<(TSuffix Suffix, string Key, InFlightLoad<TItem> Load)> claimed, Func<IReadOnlyCollection<TSuffix>, CancellationToken, ValueTask<IReadOnlyDictionary<TSuffix, TItem>>> factory) where TSuffix : notnull
    {
        Dictionary<TSuffix, TItem?> results = [];
        Exception? failure = null;

        try
        {
            // Re-check memory: another load may have finished between our miss and our claim.
            // Not IsMemoryCacheEnabled: the read core already logged "memory disabled" for this call.
            bool isMemoryCacheOn = _cacheOptions.MemoryCacheDuration != TimeSpan.Zero;

            List<(TSuffix Suffix, string Key)> toLoad = [];
            foreach ((TSuffix suffix, string key, _) in claimed)
            {
                TItem? memoryItem = isMemoryCacheOn ? GetSingleItem(source, key) : default;

                if (IsMissing(memoryItem))
                {
                    toLoad.Add((suffix, key));
                }
                else
                {
                    results[suffix] = memoryItem;
                }
            }

            if (toLoad.Count != 0)
            {
                TSuffix[] suffixesToLoad = [.. toLoad.Select(s => s.Suffix)];

                // The factory is canceled only when every caller waiting on this load has left.
                IReadOnlyDictionary<TSuffix, TItem> loaded = await factory(suffixesToLoad, scope.Token) ?? throw new InvalidOperationException("The factory returned null instead of a dictionary. Return the items found, or an empty dictionary when none exist");

                Dictionary<string, TItem> toStore = [];
                foreach ((TSuffix suffix, string key) in toLoad)
                {
                    if (loaded.TryGetValue(suffix, out TItem? loadedItem) && !IsMissing(loadedItem))
                    {
                        results[suffix] = loadedItem;
                        toStore.Add(key, loadedItem!);
                    }
                }

                // The work is already done: store it even if every caller has left in the meantime.
                if (toStore.Count == 1)
                {
                    KeyValuePair<string, TItem> single = toStore.First();

                    await SetSingleItemAsync(source, single.Key, single.Value, CancellationToken.None);
                }
                else if (toStore.Count > 1)
                {
                    await SetManyItemsAsync(source, toStore, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        scope.Dispose();

        // Remove the entries BEFORE completing them, so a new caller never joins a finished (or failed) load.
        foreach ((TSuffix suffix, string key, InFlightLoad<TItem> load) in claimed)
        {
            _inFlight.TryRemove(new KeyValuePair<string, InFlightLoad<TItem>>(key, load));
        }

        foreach ((TSuffix suffix, string key, InFlightLoad<TItem> load) in claimed)
        {
            if (failure != null)
            {
                load.Completion.TrySetException(failure);
                _ = load.Completion.Task.Exception;
            }
            else
            {
                load.Completion.TrySetResult(results.GetValueOrDefault(suffix));
            }
        }
    }

    #endregion Load

    #region Load Missing

    private async ValueTask<Dictionary<TSuffix, TItem?>> LoadMissingItemsAsync<TSuffix>(string source, List<(TSuffix Suffix, string Key)> missing, Func<IReadOnlyCollection<TSuffix>, CancellationToken, ValueTask<IReadOnlyDictionary<TSuffix, TItem>>> factory, CancellationToken cancellationToken) where TSuffix : notnull
    {
        List<(TSuffix Suffix, string Key)> unresolved = missing;

        List<(TSuffix Suffix, InFlightLoad<TItem> Load)> loads = []; // the load that produces each key's result
        List<LoadScope> scopes = []; // scopes this caller started or joined: each is left once, at the end

        try
        {
            while (unresolved.Count != 0)
            {
                LoadScope batchScope = new();
                List<(TSuffix Suffix, string Key, InFlightLoad<TItem> Load)> claimed = [];
                List<(TSuffix Suffix, string Key)> abandoned = [];
                Dictionary<LoadScope, bool> joinResults = [];

                foreach ((TSuffix Suffix, string Key) pending in unresolved)
                {
                    InFlightLoad<TItem> candidate = new(batchScope);
                    InFlightLoad<TItem> existing = _inFlight.GetOrAdd(pending.Key, candidate);

                    if (ReferenceEquals(existing, candidate))
                    {
                        claimed.Add((pending.Suffix, pending.Key, candidate));
                        loads.Add((pending.Suffix, candidate));
                        continue;
                    }

                    // Join each foreign scope only once, however many of its keys we need.
                    if (!joinResults.TryGetValue(existing.Scope, out bool joined))
                    {
                        joined = existing.Scope.TryJoin();
                        joinResults[existing.Scope] = joined;

                        if (joined)
                        {
                            scopes.Add(existing.Scope);
                        }
                    }

                    if (joined)
                    {
                        loads.Add((pending.Suffix, existing));
                    }
                    else
                    {
                        // Everyone who wanted that load has left: drop it and claim the key again on the next pass.
                        _inFlight.TryRemove(new KeyValuePair<string, InFlightLoad<TItem>>(pending.Key, existing));
                        abandoned.Add(pending);
                    }
                }

                if (claimed.Count != 0)
                {
                    scopes.Add(batchScope);
                    _ = LoadItemsAsync(source, batchScope, claimed, factory);
                }
                else
                {
                    // Nothing was claimed, so nobody else references this scope
                    batchScope.Dispose();
                }

                unresolved = abandoned;
            }

            // Our token only cancels this wait, never the load.
            await Task.WhenAll(loads.Select(s => (Task)s.Load.Completion.Task)).WaitAsync(cancellationToken);

            Dictionary<TSuffix, TItem?> result = new(loads.Count);
            foreach ((TSuffix suffix, InFlightLoad<TItem> load) in loads)
            {
                result[suffix] = await load.Completion.Task;
            }

            return result;
        }
        finally
        {
            foreach (LoadScope scope in scopes)
            {
                scope.Leave();
            }
        }
    }

    #endregion Load Missing

    #endregion Get

    #region Set

    private void SetMemoryCache(string key, TItem item, TimeSpan duration, MemoryCacheEntryOptions? memoryCacheEntryOptions = null)
    {
        memoryCacheEntryOptions ??= new()
        {
            SlidingExpiration = null,
            AbsoluteExpirationRelativeToNow = duration
        };

        memoryCache.Set(key, item, memoryCacheEntryOptions);
    }

    #region Core

    #region SetAsync

    private async ValueTask SetSingleItemAsync(string source, string cacheKey, TItem item, CancellationToken cancellationToken)
    {
        #region Distributed Cache

        if (IsDistributedCacheEnabled(source))
        {
            await SetDistributedSingleAsync(source, cacheKey, item, cancellationToken);
        }

        #endregion Distributed Cache

        #region Memory Cache

        if (IsMemoryCacheEnabled(source))
        {
            SetSingleItem(source, cacheKey, item);
        }

        #endregion Memory Cache
    }

    private void SetSingleItem(string source, string cacheKey, TItem item)
    {
        try
        {
            SetMemoryCache(cacheKey, item, _cacheOptions.MemoryCacheDuration);
        }
        catch (Exception ex)
        {
            LogMemoryCacheError(source, ex);
        }
    }

    private async Task SetDistributedSingleAsync(string source, string cacheKey, TItem item, CancellationToken cancellationToken)
    {
        try
        {
            ValidateDistributedCache();

            await distributedCacheService!.SetAsync(cacheKey, item, _cacheOptions.DistributedCacheDuration, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDistributedCacheError(source, ex);
        }
    }

    #endregion SetAsync

    #region SetItemsAsync

    private async ValueTask SetManyItemsAsync(string source, IReadOnlyDictionary<string, TItem> keyedItems, CancellationToken cancellationToken)
    {
        #region Distributed Cache

        if (IsDistributedCacheEnabled(source))
        {
            await SetDistributedManyAsync(source, keyedItems, cancellationToken);
        }

        #endregion Distributed Cache

        #region Memory Cache

        if (IsMemoryCacheEnabled(source))
        {
            SetManyItems(source, keyedItems);
        }

        #endregion Memory Cache
    }

    private void SetManyItems(string source, IReadOnlyDictionary<string, TItem> items)
    {
        try
        {
            MemoryCacheEntryOptions memoryCacheEntryOptions = new()
            {
                SlidingExpiration = null,
                AbsoluteExpirationRelativeToNow = _cacheOptions.MemoryCacheDuration
            };

            foreach (KeyValuePair<string, TItem> item in items)
            {
                SetMemoryCache(item.Key, item.Value, _cacheOptions.MemoryCacheDuration, memoryCacheEntryOptions: memoryCacheEntryOptions);
            }
        }
        catch (Exception ex)
        {
            LogMemoryCacheError(source, ex);
        }
    }

    private async Task SetDistributedManyAsync(string source, IReadOnlyDictionary<string, TItem> items, CancellationToken cancellationToken)
    {
        try
        {
            ValidateDistributedCache();

            await distributedCacheService!.SetItemsAsync(items, _cacheOptions.DistributedCacheDuration, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDistributedCacheError(source, ex);
        }
    }

    #endregion SetItemsAsync

    #endregion Core

    #endregion Set

    #endregion Privates

    #region Logs

    #region General

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - No action taken for '{Source}'. Both cache are disabled.")]
    private partial void LogCacheDisabled(string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Memory cache is disabled for '{Source}'.")]
    private partial void LogMemoryCacheDisabled(string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Distributed cache is disabled for '{Source}'.")]
    private partial void LogDistributedCacheDisabled(string source);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Memory cache failed for '{Source}'.")]
    private partial void LogMemoryCacheError(string source, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Distributed cache failed for '{Source}'.")]
    private partial void LogDistributedCacheError(string source, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - No item cached for '{Source}'. No items received.")]
    private partial void LogNoItemsToCacheDebug(string source);

    #endregion General

    #region Get

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - No item fetched from the memory cache for '{Source}'. No item found.")]
    private partial void LogNoMemoryItemCachedDebug(string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - No item fetched from the distributed cache for '{Source}'. No item found")]
    private partial void LogNoDistributedItemCachedDebug(string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Memory cache is disabled for '{Source}'. No item synced between the distributed cache to the memory cache.")]
    private partial void LogMemoryCacheDisabledForWriting(string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - No items fetched from distributed cache. No missed item found from memory cache for '{Source}'.")]
    private partial void LogNoMissedMemoryCacheItemsDebug(string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Syncinc the memory cache items from the distributed cache items for '{Source}'")]
    private partial void LogSyncMemoryCacheFromDistributedCacheDebug(string source);

    #endregion Get

    #endregion Logs
}