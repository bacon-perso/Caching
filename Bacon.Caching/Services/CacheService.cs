using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

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

    #region Items

    /// <summary>
    /// Get a single item from the cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task<TItem?> GetItemAsync(string? tenantId = null)
    {
        return await GetItemAsync(string.Empty, tenantId);
    }

    /// <summary>
    /// Get a single item from the cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task<TItem?> GetItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        TItem? defaultValue = default;

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero) && _cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetItemNoCacheDebug();

            return defaultValue;
        }

        string key = GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        TItem? obj = defaultValue;

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetItemNoMemoryCacheDebug();
        }
        else
        {
            obj = memoryCache.Get<TItem>(key);
        }

        if (obj != null && !obj.Equals(defaultValue))
        {
            return obj;
        }
        else
        {
            LogGetItemNoMemoryItemCachedDebug();
        }

        if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetItemNoDistributedCacheDebug();
        }
        else
        {
            try
            {
                obj = await distributedCacheService!.GetItemAsync(key);

                if (obj == null)
                {
                    LogGetItemNoDistributedItemCachedDebug();
                }
                else if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
                {
                    LogGetItemNoMemoryCacheSyncDebug();
                }
                else
                {
                    LogGetItemSyncMemoryCacheFromDistributedCacheDebug();

                    SetMemoryCache(key, obj, _cacheOptions.MemoryCacheDuration);
                }
            }
            catch (Exception ex)
            {
                LogGetItemError(ex);
            }
        }

        return obj;
    }

    /// <summary>
    /// Gets a list of items from the cache using their cache key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>A dictionary containing the objects cached : Key : suffix received, Value : object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    public async Task<IDictionary<TSuffix, TItem?>> GetItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();
        ValidateCacheKeySuffixes(cacheKeySuffixes);

        cacheKeySuffixes = cacheKeySuffixes.Distinct();

        TItem? defaultValue = default;

        Dictionary<string, TItem?> items = [];
        Dictionary<string, TSuffix> relations = [];
        foreach (TSuffix cacheKeySuffix in cacheKeySuffixes)
        {
            string key = GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

            items.Add(key, defaultValue);
            relations.Add(key, cacheKeySuffix);
        }

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero) && _cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetItemsNoCacheDebug();

            return MapCachedItems(items, relations);
        }

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetItemsNoMemoryCacheDebug();
        }
        else
        {
            foreach (KeyValuePair<string, TItem?> keyValuePair in items)
            {
                items[keyValuePair.Key] = memoryCache.Get<TItem>(keyValuePair.Key);
            }
        }

        IEnumerable<string> missingDistCache = items.Where(w => w.Value == null || w.Value.Equals(defaultValue)).Select(s => s.Key);

        if (!missingDistCache.Any())
        {
            LogGetItemsNoMemoryItemsCachedDebug();
        }
        else if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetItemsNoDistributedCacheDebug();
        }
        else
        {
            Dictionary<string, TItem?> distributedCacheValues = [];

            try
            {
                distributedCacheValues = await distributedCacheService!.GetItemsAsync(missingDistCache);
            }
            catch (Exception ex)
            {
                LogGetItemsError(ex);
            }

            if (distributedCacheValues.Count == 0)
            {
                LogGetItemsNoDistributedItemsCachedDebug();
            }
            else if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
            {
                LogGetItemsNoMemoryCacheSyncDebug();
            }
            else
            {
                LogGetItemsSyncMemoryCacheFromDistributedCacheDebug();

                foreach (KeyValuePair<string, TItem?> kvp in distributedCacheValues)
                {
                    if (kvp.Value != null)
                    {
                        SetMemoryCache(kvp.Key, kvp.Value, _cacheOptions.MemoryCacheDuration);
                    }

                    items[kvp.Key] = kvp.Value;
                }
            }
        }

        return MapCachedItems(items, relations);
    }

    #endregion Items

    #region Distributed

    /// <summary>
    /// Get a single item from the distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task<TItem?> GetDistributedItemAsync(string? tenantId = null)
    {
        return await GetDistributedItemAsync(string.Empty, tenantId);
    }

    /// <summary>
    /// Get a single item from the distributed cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task<TItem?> GetDistributedItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetDistributedItemNoCacheDebug();

            return default;
        }

        try
        {
            return await distributedCacheService!.GetItemAsync(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix));
        }
        catch (Exception ex)
        {
            LogGetDistributedItemError(ex);
        }

        return default;
    }

    /// <summary>
    /// Gets a list of items from the distributed cache using their cache key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>A dictionary containing the objects cached : Key : suffix received, Value : object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    public async Task<IDictionary<TSuffix, TItem?>> GetDistributedItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        ValidateCacheKeySuffixes(cacheKeySuffixes);

        cacheKeySuffixes = cacheKeySuffixes.Distinct();

        Dictionary<string, TItem?> items = [];
        Dictionary<string, TSuffix> relations = [];
        foreach (TSuffix cacheKeySuffix in cacheKeySuffixes)
        {
            string key = GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix);
            items.Add(key, default);
            relations.Add(key, cacheKeySuffix);
        }

        if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetDistributedItemsNoCacheDebug();

            return MapCachedItems(items, relations);
        }

        try
        {
            items = await distributedCacheService!.GetItemsAsync(cacheKeySuffixes.Select(s => GetCacheKey(tenantId, _cacheOptions.CacheKey, s)));
        }
        catch (Exception ex)
        {
            LogGetDistributedItemsError(ex);
        }

        return MapCachedItems(items, relations);
    }

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Get a single item from the memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public TItem? GetMemoryItem(string? tenantId = null)
    {
        return GetMemoryItem(string.Empty, tenantId);
    }

    /// <summary>
    /// Get a single item from the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public TItem? GetMemoryItem<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetMemoryItemNoCacheDebug();

            return default;
        }

        return memoryCache.Get<TItem>(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix));
    }

    /// <summary>
    /// Get a list of items from the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns>A dictionary containing the objects cached : Key : suffix received, Value : object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    public IDictionary<TSuffix, TItem?> GetMemoryItems<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull
    {
        ValidateCacheKeySuffixes(cacheKeySuffixes);

        cacheKeySuffixes = cacheKeySuffixes.Distinct();

        Dictionary<string, TItem?> items = [];
        Dictionary<string, TSuffix> relations = [];
        foreach (TSuffix cacheKeySuffix in cacheKeySuffixes)
        {
            string key = GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix);
            items.Add(key, default);
            relations.Add(key, cacheKeySuffix);
        }

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
        {
            LogGetMemoryItemsNoCacheDebug();

            return MapCachedItems(items, relations);
        }

        foreach (KeyValuePair<string, TItem?> keyValuePair in items)
        {
            items[keyValuePair.Key] = memoryCache.Get<TItem>(keyValuePair.Key);
        }

        return MapCachedItems(items, relations);
    }

    #endregion Memory

    #endregion Get

    #region Set

    #region Items

    /// <summary>
    /// Add an item to both the memory and distributed cache
    /// </summary>
    /// <param name="item">The object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task SetItemAsync(TItem item, string? tenantId = null)
    {
        await SetItemAsync(item, string.Empty, tenantId);
    }

    /// <summary>
    /// Add an item to both the memory and distributed cache using it's key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="item">The object to cache</param>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task SetItemAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        ArgumentNullException.ThrowIfNull(item, nameof(item));

        string key = GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix);

        try
        {
            if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
            {
                LogSetItemDistributedItemNotCachedDebug();
            }
            else
            {
                await distributedCacheService!.SetItemAsync(key, item, _cacheOptions.DistributedCacheDuration);
            }

            if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
            {
                LogSetItemMemoryItemNotCachedDebug();
                return;
            }

            SetMemoryCache(key, item, _cacheOptions.MemoryCacheDuration);
        }
        catch (Exception ex)
        {
            LogSetItemError(ex);
        }
    }

    /// <summary>
    /// Add a list of items to both the memory and distributed cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="items">Dictionary containing objects to cache. Key : cache key suffix, Value : object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The items cannot be null</exception>
	/// <exception cref="ArgumentException">The items cannot be empty</exception>
    public async Task SetItemsAsync<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();
        ValidateCacheKeySuffixes(items);
        ValidateNullItem(items);

        try
        {
            if (!_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
            {
                await SetDistributedCacheAsync(tenantId, _cacheOptions.CacheKey, items, _cacheOptions.DistributedCacheDuration);
            }
            else
            {
                LogSetItemsDistributedItemsNotCachedDebug();
            }

            if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
            {
                LogSetItemsMemoryItemsNotCachedDebug();
                return;
            }

            foreach (KeyValuePair<TSuffix, TItem> item in items)
            {
                SetMemoryCache(GetCacheKey(tenantId, _cacheOptions.CacheKey, item.Key), item.Value, _cacheOptions.MemoryCacheDuration);
            }
        }
        catch (Exception ex)
        {
            LogSetItemsError(ex);
        }
    }

    #endregion Items

    #region Distributed

    /// <summary>
    /// Add an item to the distributed cache
    /// </summary>
    /// <param name="item">The object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task SetDistributedItemAsync(TItem item, string? tenantId = null)
    {
        await SetDistributedItemAsync(item, string.Empty, tenantId);
    }

    /// <summary>
    /// Add an item to the distributed cache using it's key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="item">The object to cache</param>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task SetDistributedItemAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        ArgumentNullException.ThrowIfNull(item, nameof(item));

        if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogSetDistributedItemNotCachedDebug();
            return;
        }

        try
        {
            await distributedCacheService!.SetItemAsync(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix), item, _cacheOptions.DistributedCacheDuration);
        }
        catch (Exception ex)
        {
            LogSetMemoryItemNotCachedError(ex);
        }
    }

    /// <summary>
    /// Add a list of items to the distributed cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="items">Dictionary containing objects to cache. Key : cache key suffix, Value : object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The items cannot be null</exception>
	/// <exception cref="ArgumentException">The items cannot be empty</exception>
    public async Task SetDistributedItemsAsync<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();
        ValidateCacheKeySuffixes(items);
        ValidateNullItem(items);

        if (_cacheOptions.DistributedCacheDuration.Equals(TimeSpan.Zero))
        {
            LogSetDistributedItemsNotCachedDebug();
            return;
        }

        try
        {
            await SetDistributedCacheAsync(tenantId, _cacheOptions.CacheKey, items, _cacheOptions.DistributedCacheDuration);
        }
        catch (Exception ex)
        {
            LogSetMemoryItemsNotCachedError(ex);
        }
    }

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Add an item to the memory cache
    /// </summary>
    /// <param name="item">The object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public void SetMemoryItem(TItem item, string? tenantId = null)
    {
        SetMemoryItem(item, string.Empty, tenantId);
    }

    /// <summary>
    /// Add an item to the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="item">The object to cache</param>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public void SetMemoryItem<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ArgumentNullException.ThrowIfNull(item, nameof(item));

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
        {
            LogSetMemoryItemNotCachedDebug();
            return;
        }

        SetMemoryCache(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix), item, _cacheOptions.MemoryCacheDuration);
    }

    /// <summary>
    /// Add a list of items to the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="items">Dictionary containing objects to cache. Key : cache key suffix, Value : object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="ArgumentNullException">The items cannot be null</exception>
    /// <exception cref="ArgumentException">The items cannot be empty</exception>
    public void SetMemoryItems<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null) where TSuffix : notnull
    {
        ValidateCacheKeySuffixes(items);
        ValidateNullItem(items);

        if (_cacheOptions.MemoryCacheDuration.Equals(TimeSpan.Zero))
        {
            LogSetMemoryItemsNotCachedDebug();
            return;
        }

        foreach (KeyValuePair<TSuffix, TItem> item in items)
        {
            SetMemoryCache(GetCacheKey(tenantId, _cacheOptions.CacheKey, item.Key), item.Value, _cacheOptions.MemoryCacheDuration);
        }
    }

    #endregion Memory

    #endregion Set

    #region Remove

    #region Items

    /// <summary>
    /// Remove a an item from the memory and distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task RemoveItemAsync(string? tenantId = null)
    {
        await RemoveItemAsync(string.Empty, tenantId);
    }

    /// <summary>
    /// Remove a an item from the memory and distributed cache from it's suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task RemoveItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        try
        {
            await distributedCacheService!.RemoveItemAsync(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix));
            RemoveMemoryItem(cacheKeySuffix);
        }
        catch (Exception ex)
        {
            LogRemoveItemError(ex);
        }
    }

    /// <summary>
    /// Remove a list of items from the memory and distributed cache from their suffixes
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    public async Task RemoveItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();
        ValidateCacheKeySuffixes(cacheKeySuffixes);

        cacheKeySuffixes = cacheKeySuffixes.Distinct();

        try
        {
            await distributedCacheService!.RemoveItemsAsync(cacheKeySuffixes.Select(s => GetCacheKey(tenantId, _cacheOptions.CacheKey, s)));
            RemoveMemoryItems(cacheKeySuffixes);
        }
        catch (Exception ex)
        {
            LogRemoveItemsError(ex);
        }
    }

    #endregion Items

    #region Distributed

    /// <summary>
    /// Remove a an item from the distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task RemoveDistributedItemAsync(string? tenantId = null)
    {
        await RemoveDistributedItemAsync(string.Empty, tenantId);
    }

    /// <summary>
    /// Remove a an item from the distributed cache from it's suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task RemoveDistributedItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();

        try
        {
            await distributedCacheService!.RemoveItemAsync(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix));
        }
        catch (Exception ex)
        {
            LogRemoveDistributedItemError(ex);
        }
    }

    /// <summary>
    /// Remove a list of items from the distributed cache from their suffixes
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    public async Task RemoveDistributedItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull
    {
        ValidateDistributedCache();
        ValidateCacheKeySuffixes(cacheKeySuffixes);

        cacheKeySuffixes = cacheKeySuffixes.Distinct();

        try
        {
            await distributedCacheService!.RemoveItemsAsync(cacheKeySuffixes.Select(s => GetCacheKey(tenantId, _cacheOptions.CacheKey, s)));
        }
        catch (Exception ex)
        {
            LogRemoveDistributedItemsError(ex);
        }
    }

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Remove a an item from the memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public void RemoveMemoryItem(string? tenantId = null)
    {
        RemoveMemoryItem(string.Empty, tenantId);
    }

    /// <summary>
    /// Remove a an item from the memory cache from it's suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public void RemoveMemoryItem<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull
    {
        memoryCache.Remove(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix));
    }

    /// <summary>
    /// Remove a list of items from the memory cache from their suffixes
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    public void RemoveMemoryItems<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull
    {
        ValidateCacheKeySuffixes(cacheKeySuffixes);

        cacheKeySuffixes = cacheKeySuffixes.Distinct();

        foreach (TSuffix cacheKeySuffix in cacheKeySuffixes)
        {
            memoryCache.Remove(GetCacheKey(tenantId, _cacheOptions.CacheKey, cacheKeySuffix));
        }
    }

    #endregion Memory

    #endregion Remove

    #region Flush

    #region Items

    /// <summary>
    /// Flush items matching the key from the distributed and memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task FlushItemsAsync(string? tenantId = null)
    {
        ValidateDistributedCache();

        string key = GetCacheKey(tenantId, _cacheOptions.CacheKey, string.Empty);

        try
        {
            await distributedCacheService!.FlushItemsAsync(key + "*");
            RemoveMemoryCacheByKey(key);
        }
        catch (Exception ex)
        {
            LogFlushItemsError(ex);
        }
    }

    #endregion Items

    #region Distributed

    /// <summary>
    /// Flush items matching the key from the distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed cache not initialized</exception>
    public async Task FlushDistributedItemsAsync(string? tenantId = null)
    {
        ValidateDistributedCache();

        try
        {
            await distributedCacheService!.FlushItemsAsync(GetCacheKey(tenantId, _cacheOptions.CacheKey, string.Empty) + "*");
        }
        catch (Exception ex)
        {
            LogFlushDistributedItemsError(ex);
        }
    }

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Flush items matching the key from the memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    public void FlushMemoryItems(string? tenantId = null)
    {
        try
        {
            RemoveMemoryCacheByKey(GetCacheKey(tenantId, _cacheOptions.CacheKey, string.Empty));
        }
        catch (Exception ex)
        {
            LogFlushMemoryItemsError(ex);
        }
    }

    #endregion Memory

    #endregion Flush

    #endregion Public

    #region Privates

    #region Global

    private string GetCacheKey<TSuffix>(string? tenantId, string cacheKey, TSuffix? cacheKeySuffix)
    {
        if (tenantId is not null)
        {
            TenantValidationService.ValidateTenantId(tenantId);
        }

        string _tenantId = (tenantId ?? _cacheServerOptions.TenantId) ?? throw new ArgumentNullException(nameof(tenantId), "The tenant ID cannot be null");

        ArgumentException.ThrowIfNullOrWhiteSpace(_tenantId, nameof(tenantId));

        return $"{_tenantId}_{cacheKey}{(cacheKeySuffix?.ToString() ?? string.Empty)}";
    }

    private static Dictionary<TSuffix, TItem?> MapCachedItems<TSuffix>(IDictionary<string, TItem?> items, IDictionary<string, TSuffix> relations) where TSuffix : notnull
    {
        return items.ToDictionary(key => relations[key.Key], value => value.Value);
    }

    #region Validations

    private static void ValidateCacheKeySuffixes<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes) where TSuffix : notnull
    {
        if (cacheKeySuffixes == null)
        {
            throw new ArgumentNullException(nameof(cacheKeySuffixes), $"The {nameof(cacheKeySuffixes)} cannot be null");
        }

        if (!cacheKeySuffixes.Any())
        {
            throw new ArgumentException($"The {nameof(cacheKeySuffixes)} cannot be empty", nameof(cacheKeySuffixes));
        }

        if (typeof(TSuffix).Equals(typeof(string)))
        {
            if (cacheKeySuffixes.Any(a => string.IsNullOrEmpty(a.ToString())))
            {
                throw new ArgumentException($"The {nameof(cacheKeySuffixes)} cannot be empty", nameof(cacheKeySuffixes));
            }
        }
    }

    private static void ValidateCacheKeySuffixes<TSuffix>(IDictionary<TSuffix, TItem> items) where TSuffix : notnull
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "The items cannot be null");
        }

        if (!items.Any())
        {
            throw new ArgumentException("The items cannot be empty", nameof(items));
        }

        if (typeof(TSuffix).Equals(typeof(string)))
        {
            if (items.Any(a => string.IsNullOrWhiteSpace(a.Key.ToString())))
            {
                throw new ArgumentException("The items cannot contain a key with an empty value", nameof(items));
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

    private static void ValidateNullItem<TSuffix>(IDictionary<TSuffix, TItem> items) where TSuffix : notnull
    {
        IEnumerable<TSuffix> invalidItems = items.Where(w => w.Value == null).Select(s => s.Key);

        if (invalidItems.Any())
        {
            throw new ArgumentNullException(null, $"The following keys contain invalid null values: {string.Join(", ", invalidItems)}");
        }
    }

    #endregion Validations

    #endregion Global

    #region Memory Cache

    #region Set

    private void SetMemoryCache(string key, TItem item, TimeSpan duration)
    {
        MemoryCacheEntryOptions memoryCacheEntryOptions = new()
        {
            SlidingExpiration = null,
            AbsoluteExpirationRelativeToNow = duration
        };

        memoryCache.Set(key, item, memoryCacheEntryOptions);
    }

    #endregion Set

    #region Remove

    private void RemoveMemoryCacheByKey(string key)
    {
        IEnumerable<string> result = Keys.Where(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase));

        foreach (string _key in result)
        {
            memoryCache.Remove(_key);
        }
    }

    private IEnumerable<string> Keys
    {
        get
        {
            if (memoryCache is MemoryCache _memCache)
            {
                return _memCache.Keys.Select(s => s.ToString() ?? string.Empty);
            }

            return [];
        }
    }

    #endregion Remove

    #endregion Memory Cache

    #region Distributed Cache

    #region Set

    private async Task SetDistributedCacheAsync<TSuffix>(string? tenantId, string cacheKey, IDictionary<TSuffix, TItem> items, TimeSpan duration) where TSuffix : notnull
    {
        Dictionary<string, TItem> dic = [];
        foreach (KeyValuePair<TSuffix, TItem> item in items)
        {
            dic.Add(GetCacheKey(tenantId, cacheKey, item.Key), item.Value);
        }

        await distributedCacheService!.SetItemsAsync(dic, duration);
    }

    #endregion Set

    #endregion Distributed Cache

    #endregion Privates

    #region Logs

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Get item failed")]
    private partial void LogGetItemError(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - Syncinc the memory cache item from the distributed cache item")]
    private partial void LogGetItemSyncMemoryCacheFromDistributedCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - No item fetched from the distributed cache. No item found")]
    private partial void LogGetItemNoDistributedItemCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - No item fetched from the memory cache. No item found")]
    private partial void LogGetItemNoMemoryItemCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - No item fetched from the cache. Both the memory cache duration and the distributed cache duration were set to 0")]
    private partial void LogGetItemNoCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - No item fetched from the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogGetItemNoDistributedCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - No item fetched from the memory cache. The memory cache duration was set to 0")]
    private partial void LogGetItemNoMemoryCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Item - No item synced from the distributed cache. The memory cache duration was set to 0")]
    private partial void LogGetItemNoMemoryCacheSyncDebug();

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Get Items failed")]
    private partial void LogGetItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - No items synced from the distributed cache. The memory cache duration was set to 0")]
    private partial void LogGetItemsNoMemoryCacheSyncDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - Syncinc the memory cache items from the distributed cache items")]
    private partial void LogGetItemsSyncMemoryCacheFromDistributedCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - No items fetched from the distributed cache. No items found")]
    private partial void LogGetItemsNoDistributedItemsCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - No items fetched from the cache. No items found")]
    private partial void LogGetItemsNoMemoryItemsCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - No items fetched from the cache. Both the memory cache duration and the distributed cache duration were set to 0")]
    private partial void LogGetItemsNoCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - No items fetched from the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogGetItemsNoDistributedCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Items - No items fetched from the memory cache. The memory cache duration was set to 0")]
    private partial void LogGetItemsNoMemoryCacheDebug();

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Get distributed item failed")]
    private partial void LogGetDistributedItemError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Get distributed items failed")]
    private partial void LogGetDistributedItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Distributed Items - No item fetched from the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogGetDistributedItemNoCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Distributed Items - No items fetched from the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogGetDistributedItemsNoCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Memory Item - No item fetched from the memory cache. The memory cache duration was set to 0")]
    private partial void LogGetMemoryItemNoCacheDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Get Memory Items - No items fetched from the memory cache. The memory cache duration was set to 0")]
    private partial void LogGetMemoryItemsNoCacheDebug();

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Set item failed")]
    private partial void LogSetItemError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Set items failed")]
    private partial void LogSetItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Item - No item was not cached in the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogSetItemDistributedItemNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Item - No item was not cached in memory. The memory cache duration was set to 0")]
    private partial void LogSetItemMemoryItemNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Items - No items was not cached in the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogSetItemsDistributedItemsNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Items - No items was not cached in memory. The memory cache duration was set to 0")]
    private partial void LogSetItemsMemoryItemsNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Set distributed Item failed")]
    private partial void LogSetMemoryItemNotCachedError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Set distributed Items failed")]
    private partial void LogSetMemoryItemsNotCachedError(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Distributed Item - No item was not cached in the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogSetDistributedItemNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Distributed Items - No item was not cached in the distributed cache. The distributed cache duration was set to 0")]
    private partial void LogSetDistributedItemsNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Memory Item - No item was not cached in memory. The memory cache duration was set to 0")]
    private partial void LogSetMemoryItemNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching - Set Memory Items - No item was not cached in memory. The memory cache duration was set to 0")]
    private partial void LogSetMemoryItemsNotCachedDebug();

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Remove item failed")]
    private partial void LogRemoveItemError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Remove items failed")]
    private partial void LogRemoveItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Remove distributed item failed")]
    private partial void LogRemoveDistributedItemError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Remove distributed items failed")]
    private partial void LogRemoveDistributedItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Flush items failed")]
    private partial void LogFlushItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Flush distributed items failed")]
    private partial void LogFlushDistributedItemsError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Caching - Flush memory items failed")]
    private partial void LogFlushMemoryItemsError(Exception exception);

    #endregion Logs
}