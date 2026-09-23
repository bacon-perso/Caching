namespace Bacon.Caching.Interfaces.Services;

/// <summary>
/// Cache service interface
/// </summary>
public interface ICacheService<TItem>
{
    #region Get

    #region Items

    /// <summary>
    /// Get a single item from the cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task<TItem?> GetItemAsync(string? tenantId = null);

    /// <summary>
    /// Get a single item from the cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task<TItem?> GetItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Gets a list of items from the cache using their cache key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>A dictionary containing the objects cached : Key : suffix received, Value : object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    Task<IDictionary<TSuffix, TItem?>> GetItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull;

    #endregion Items

    #region Distributed

    /// <summary>
    /// Get a single item from the Distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task<TItem?> GetDistributedItemAsync(string? tenantId = null);

    /// <summary>
    /// Get a single item from the Distributed cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task<TItem?> GetDistributedItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Gets a list of items from the Distributed cache using their cache key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>A dictionary containing the objects cached : Key : suffix received, Value : object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    Task<IDictionary<TSuffix, TItem?>> GetDistributedItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull;

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Get a single item from the memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    TItem? GetMemoryItem(string? tenantId = null);

    /// <summary>
    /// Get a single item from the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>Object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    TItem? GetMemoryItem<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Get a list of items from the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <returns>A dictionary containing the objects cached : Key : suffix received, Value : object returned, null/default if not present</returns>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    IDictionary<TSuffix, TItem?> GetMemoryItems<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull;

    #endregion Memory

    #endregion Get

    #region Set

    #region Items

    /// <summary>
    /// Add an item to both the memory and distributed cache using it's key suffix
    /// </summary>
    /// <param name="item">The object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task SetItemAsync(TItem item, string? tenantId = null);

    /// <summary>
    /// Add an item to both the memory and distributed cache using it's key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="item">The object to cache</param>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task SetItemAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Add a list of items to both the memory and distributed cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="items">Dictionary containing objects to cache. Key : cache key suffix, Value : object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The items cannot be null</exception>
	/// <exception cref="ArgumentException">The items cannot be empty</exception>
    Task SetItemsAsync<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null) where TSuffix : notnull;

    #endregion Items

    #region Distributed

    /// <summary>
    /// Add an item to the distributed cache using it's key suffix
    /// </summary>
    /// <param name="item">The object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task SetDistributedItemAsync(TItem item, string? tenantId = null);

    /// <summary>
    /// Add an item to the distributed cache using it's key suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="item">The object to cache</param>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task SetDistributedItemAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Add a list of items to the distributed cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="items">Dictionary containing objects to cache. Key : cache key suffix, Value : object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The items cannot be null</exception>
	/// <exception cref="ArgumentException">The items cannot be empty</exception>
    Task SetDistributedItemsAsync<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null) where TSuffix : notnull;

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Add an item to the memory cache
    /// </summary>
    /// <param name="item">The object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    void SetMemoryItem(TItem item, string? tenantId = null);

    /// <summary>
    /// Add an item to the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="item">The object to cache</param>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    void SetMemoryItem<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Add a list of items to the memory cache
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="items">Dictionary containing objects to cache. Key : cache key suffix, Value : object to cache</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="ArgumentNullException">The items cannot be null</exception>
    /// <exception cref="ArgumentException">The items cannot be empty</exception>
    void SetMemoryItems<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null) where TSuffix : notnull;

    #endregion Memory

    #endregion Set

    #region Remove

    #region Items

    /// <summary>
    /// Remove a an item from the memory and distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task RemoveItemAsync(string? tenantId = null);

    /// <summary>
    /// Remove a an item from the memory and distributed cache from it's suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task RemoveItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Remove a list of items from the memory and distributed cache from their suffixes
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    Task RemoveItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull;

    #endregion Items

    #region Distributed

    /// <summary>
    /// Remove a an item from the distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task RemoveDistributedItemAsync(string? tenantId = null);

    /// <summary>
    /// Remove a an item from the distributed cache from it's suffix
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task RemoveDistributedItemAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Remove a list of items from the distributed cache from their suffixes
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    Task RemoveDistributedItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull;

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Remove a an item from the memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    void RemoveMemoryItem(string? tenantId = null);

    /// <summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// Remove a an item from the memory cache from it's suffix
    /// </summary>
    /// <param name="cacheKeySuffix">The cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    void RemoveMemoryItem<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null) where TSuffix : notnull;

    /// <summary>
    /// Remove a list of items from the memory cache from their suffixes
    /// </summary>
    /// <typeparam name="TSuffix">The typeof for the suffix. IE: Guid, string, int, etc..</typeparam>
    /// <param name="cacheKeySuffixes">A collection of cache key suffix. IE : Could be a specific id</param>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="ArgumentNullException">The cacheKeySuffixes cannot be null</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot be empty</exception>
    /// <exception cref="ArgumentException">The cacheKeySuffixes cannot contain an empty suffix</exception>
    void RemoveMemoryItems<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null) where TSuffix : notnull;

    #endregion Memory

    #endregion Remove

    #region Flush

    #region Items

    /// <summary>
    /// Flush items matching the key from the distributed and memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task FlushItemsAsync(string? tenantId = null);

    #endregion Items

    #region Distributed

    /// <summary>
    /// Flush items matching the key from the distributed cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    /// <exception cref="InvalidOperationException">Distributed Cache not initialized</exception>
    Task FlushDistributedItemsAsync(string? tenantId = null);

    #endregion Distributed

    #region Memory

    /// <summary>
    /// Flush items matching the key from the memory cache
    /// </summary>
    /// <param name="tenantId">Optional tenantId that overrides the global setting. Required for multi-tenancy.</param>
    /// <exception cref="ArgumentNullException">The tenant ID cannot be null</exception>
    /// <exception cref="ArgumentException">The tenant ID cannot be empty</exception>
    void FlushMemoryItems(string? tenantId = null);

    #endregion Memory

    #endregion Flush
}