using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace Bacon.Caching.Services;

internal sealed partial class CacheService<TItem>(IOptions<CacheServerOptions> cacheServerOptionsOptions, IOptions<CacheOptions<TItem>> cacheOptionsOptions, IMemoryCache memoryCache, ILogger<TItem> logger, IDistributedCacheService<TItem>? distributedCacheService = null)
    : ICacheService<TItem>
{
    #region CTOR

    private readonly CacheServerOptions _cacheServerOptions = cacheServerOptionsOptions.Value;
    private readonly CacheOptions<TItem> _cacheOptions = cacheOptionsOptions.Value;

    #endregion CTOR

    #region Public

    #region Get

    public async ValueTask<TItem?> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        return await GetAsync(string.Empty, tenantId: tenantId, cancellationToken: cancellationToken);
    }

    [OverloadResolutionPriority(1)]
    public async ValueTask<TItem?> GetAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(GetAsync);

        TItem? defaultValue = default;

        if (IsCacheDisabled(source))
        {
            return defaultValue;
        }

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        TItem? obj = defaultValue;

        if (IsMemoryCacheEnabled(source))
        {
            try
            {
                obj = memoryCache.Get<TItem>(cacheKey);
            }
            catch (Exception ex)
            {
                LogMemoryCacheError(source, ex);
            }
        }

        if (obj != null && !obj.Equals(defaultValue))
        {
            return obj;
        }

        LogNoMemoryItemCachedDebug(source);

        if (IsDistributedCacheEnabled(source))
        {
            try
            {
                ValidateDistributedCache();

                obj = await distributedCacheService!.GetAsync(cacheKey, cancellationToken: cancellationToken);

                if (obj == null)
                {
                    LogNoDistributedItemCachedDebug(source);
                    return obj;
                }

                if (IsMemoryCacheEnabled(source, isWrite: true))
                {
                    SetMemoryCache(cacheKey, obj, _cacheOptions.MemoryCacheDuration);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDistributedCacheError(source, ex);
            }
        }

        return obj;
    }

    public async ValueTask<IDictionary<TSuffix, TItem?>> GetItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        ArgumentNullException.ThrowIfNull(cacheKeySuffixes, nameof(cacheKeySuffixes));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(GetItemsAsync);

        if (!cacheKeySuffixes.Any())
        {
            LogNoItemsToCacheDebug(source);
            return new Dictionary<TSuffix, TItem?>();
        }

        TSuffix[] typedSuffixes = [.. cacheKeySuffixes.Distinct()];

        ValidateCacheKeySuffixes(typedSuffixes, nameof(cacheKeySuffixes));

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

        if (IsMemoryCacheEnabled(source))
        {
            try
            {
                foreach (KeyValuePair<string, TItem?> keyValuePair in items)
                {
                    items[keyValuePair.Key] = memoryCache.Get<TItem>(keyValuePair.Key);
                }
            }
            catch (Exception ex)
            {
                LogMemoryCacheError(source, ex);
            }
        }

        string[] missedFromMemory = [.. items.Where(w => w.Value == null || w.Value.Equals(defaultValue)).Select(s => s.Key)];

        bool isDistributedCacheEnabled = IsDistributedCacheEnabled(source);

        if (isDistributedCacheEnabled && missedFromMemory.Length == 0)
        {
            LogNoMissedMemoryCacheItemsDebug(source);
        }
        else if (isDistributedCacheEnabled && missedFromMemory.Length != 0)
        {
            try
            {
                ValidateDistributedCache();

                Dictionary<string, TItem?> distributedCacheValues = await distributedCacheService!.GetItemsAsync(missedFromMemory, cancellationToken: cancellationToken);

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

                        items[distributedCacheValue.Key] = distributedCacheValue.Value;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDistributedCacheError(source, ex);
            }
        }

        return MapCachedItems(items, relations);
    }

    #endregion Get

    #region Set

    public async ValueTask SetAsync(TItem item, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        await SetAsync(item, cacheKeySuffix: string.Empty, tenantId: tenantId, cancellationToken: cancellationToken);
    }

    [OverloadResolutionPriority(1)]
    public async ValueTask SetAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));
        ArgumentNullException.ThrowIfNull(item, nameof(item));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(SetAsync);

        if (IsCacheDisabled(source))
        {
            return;
        }

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        if (IsDistributedCacheEnabled(source))
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

        if (IsMemoryCacheEnabled(source))
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
    }

    public async ValueTask SetItemsAsync<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
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

        ValidateCacheKeySuffixes(typedSuffixes, nameof(items));
        ValidateNullValues(items);

        Dictionary<string, TItem> keyedItems = new(items.Count);
        foreach (KeyValuePair<TSuffix, TItem> item in items)
        {
            keyedItems.Add(GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, item.Key), item.Value);
        }

        if (IsDistributedCacheEnabled(source))
        {
            try
            {
                ValidateDistributedCache();

                await distributedCacheService!.SetItemsAsync(keyedItems, _cacheOptions.DistributedCacheDuration, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDistributedCacheError(source, ex);
            }
        }

        if (IsMemoryCacheEnabled(source))
        {
            try
            {
                MemoryCacheEntryOptions memoryCacheEntryOptions = new()
                {
                    SlidingExpiration = null,
                    AbsoluteExpirationRelativeToNow = _cacheOptions.MemoryCacheDuration
                };

                foreach (KeyValuePair<string, TItem> item in keyedItems)
                {
                    SetMemoryCache(item.Key, item.Value, _cacheOptions.MemoryCacheDuration, memoryCacheEntryOptions: memoryCacheEntryOptions);
                }
            }
            catch (Exception ex)
            {
                LogMemoryCacheError(source, ex);
            }
        }
    }

    #endregion Set

    #region Remove

    public async ValueTask RemoveAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        await RemoveAsync(cacheKeySuffix: string.Empty, tenantId: tenantId, cancellationToken: cancellationToken);
    }

    [OverloadResolutionPriority(1)]
    public async ValueTask RemoveAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
        ArgumentNullException.ThrowIfNull(cacheKeySuffix, nameof(cacheKeySuffix));

        cancellationToken.ThrowIfCancellationRequested();

        string source = nameof(RemoveAsync);

        if (IsCacheDisabled(source))
        {
            return;
        }

        string cacheKey = GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

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
    }

    public async ValueTask RemoveItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull
    {
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

        ValidateCacheKeySuffixes(typedSuffixes, nameof(cacheKeySuffixes));

        string[] cacheKeys = [.. typedSuffixes.Select(s => GetCacheKey(_cacheOptions.Scope, tenantId, _cacheOptions.CacheKey, s))];

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
    }

    #endregion Remove

    #endregion Public

    #region Privates

    #region Global

    private string GetCacheKey<TSuffix>(CacheScopes cacheScope, string? tenantId, string cacheKey, TSuffix? cacheKeySuffix)
    {
        string suffix = cacheKeySuffix?.ToString() ?? string.Empty;

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

    private static void ValidateCacheKeySuffixes<TSuffix>(TSuffix[] cacheKeySuffixes, string source) where TSuffix : notnull
    {
        if (cacheKeySuffixes.Length == 0)
        {
            throw new ArgumentException($"The {source} cannot be empty", source);
        }

        ValidateCacheKeyNullSuffixes(cacheKeySuffixes, source);
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
            throw new InvalidOperationException("Distributed cache not initialized");
        }
    }

    private static void ValidateNullValues<TSuffix>(IDictionary<TSuffix, TItem> items) where TSuffix : notnull
    {
        IEnumerable<TSuffix> invalidItems = items.Where(w => w.Value == null).Select(s => s.Key);

        if (invalidItems.Any())
        {
            throw new ArgumentNullException(null, $"The following keys contain invalid null values: {string.Join(", ", invalidItems)}");
        }
    }

    #endregion Validations

    #endregion Global

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