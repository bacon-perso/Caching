using System.Runtime.CompilerServices;

namespace Bacon.Caching.Interfaces.Services;

/// <summary>
/// Dual-layer (memory and distributed) cache service for items of type <typeparamref name="TItem"/>.
/// </summary>
/// <typeparam name="TItem">The type of the cached items.</typeparam>
/// <remarks>
/// Reads check the memory cache first, then fall back to the distributed cache and backfill the memory cache on a hit.
/// Writes and removals are applied to both layers. A layer is disabled for a type when its duration is <see cref="TimeSpan.Zero"/>.
/// Failures of either layer (for example an unreachable distributed cache) are logged and are not thrown to the caller.
/// The cache key has the format <c>{tenantId}_{CacheKey}{suffix}</c> for tenant types (<see cref="Bacon.Caching.Models.CacheScopes.Tenant"/>) and <c>|{CacheKey}{suffix}</c> for global types (<see cref="Bacon.Caching.Models.CacheScopes.Global"/>), which are shared by all tenants and cannot be accessed with a tenant ID.
/// </remarks>
public interface ICacheService<TItem>
{
    #region Get

    #region Get

    /// <summary>
    /// Gets the item cached without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>), for example a configuration object. The memory cache is checked first, then the distributed cache, and an item found only in the distributed cache is copied into the memory cache. Nothing is loaded from the source: a miss returns null/default. To load the item on a miss, use <c>GetOrCreateAsync</c>.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached item, or null/default if not present.</returns>
    /// <remarks>
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>GetAsync("abc")</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>GetAsync(tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">No tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask<TItem?> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single item from the cache using its cache key suffix. The memory cache is checked first, then the distributed cache, and an item found only in the distributed cache is copied into the memory cache. Nothing is loaded from the source: a miss returns null/default. To load the item on a miss, use <c>GetOrCreateAsync</c>.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached item, or null/default if not present.</returns>
    /// <exception cref="ArgumentNullException">The cache key suffix is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask<TItem?> GetAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Gets several items from the cache using their cache key suffixes. The memory cache is checked first, then the distributed cache for the items missing from memory, and the items found only in the distributed cache are copied into the memory cache. Nothing is loaded from the source: a miss is returned as null/default. To load the missing items, use <c>GetOrCreateItemsAsync</c>.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffixes">The cache key suffixes, for example the identifiers of the items. Duplicates are ignored.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A new read-only dictionary containing one entry per distinct suffix received: the key is the suffix, the value is the cached item, or null/default if not present. An empty collection of suffixes returns an empty dictionary.</returns>
    /// <exception cref="ArgumentNullException">The cache key suffixes are null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The cache key suffixes contain a null value or a value whose string representation is empty or whitespace, the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask<IReadOnlyDictionary<TSuffix, TItem?>> GetItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion Get

    #region GetOrCreate

    /// <summary>
    /// Gets the item cached without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>), or loads it with <paramref name="factory"/> when it is not cached, stores it in the memory and distributed cache and returns it.
    /// </summary>
    /// <param name="factory">Loads the item from the source (for example a database) when it is not cached. It receives a cancellation token shared by the callers waiting on the load, not the caller's own (see the remarks). Returns null/default when the item does not exist.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token. It cancels the wait of this caller; the load itself is canceled only when every caller waiting on it has canceled.</param>
    /// <returns>The cached or loaded item, or null/default if the factory returned it.</returns>
    /// <remarks>
    /// Stampede protection: concurrent misses for the same key share a single call to <paramref name="factory"/>, and every caller receives its result. This only protects within one process: with several nodes, each node loads once.
    /// The factory's cancellation token is shared by every caller waiting on the load and is canceled only when all of them have canceled, so a caller that gives up never cancels the load for the others. A cancellation callback registered on that token runs while a lock is held, so it must not block waiting for the load to finish. A factory that ignores the token runs to completion and its result is cached.
    /// Once <paramref name="factory"/> has returned, its result is stored even if every caller has left in the meantime. A removal made while a load is in flight does not stop that load from storing its result, so the item can be cached again with data read before the removal.
    /// When the memory cache is disabled for the type, a caller that missed just as another load finished cannot see that result and may load the same key again.
    /// If <paramref name="factory"/> throws, the exception is thrown to every caller waiting on that load (it is not logged), nothing is cached, and the next call retries.
    /// A null/default result is returned and is not cached. If both layers are disabled for the type, <paramref name="factory"/> is called directly, without sharing.
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>GetOrCreateAsync("abc", factory)</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>GetOrCreateAsync(factory, tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The factory is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask<TItem?> GetOrCreateAsync(Func<CancellationToken, ValueTask<TItem?>> factory, string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an item using its cache key suffix, or loads it with <paramref name="factory"/> when it is not cached, stores it in the memory and distributed cache and returns it.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="factory">Loads the item from the source (for example a database) when it is not cached. It receives a cancellation token shared by the callers waiting on the load, not the caller's own (see the remarks). Returns null/default when the item does not exist.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token. It cancels the wait of this caller; the load itself is canceled only when every caller waiting on it has canceled.</param>
    /// <returns>The cached or loaded item, or null/default if the factory returned it.</returns>
    /// <remarks>
    /// Stampede protection: concurrent misses for the same key share a single call to <paramref name="factory"/>, and every caller receives its result. This only protects within one process: with several nodes, each node loads once.
    /// The factory's cancellation token is shared by every caller waiting on the load and is canceled only when all of them have canceled, so a caller that gives up never cancels the load for the others. A cancellation callback registered on that token runs while a lock is held, so it must not block waiting for the load to finish. A factory that ignores the token runs to completion and its result is cached.
    /// Once <paramref name="factory"/> has returned, its result is stored even if every caller has left in the meantime. A removal made while a load is in flight does not stop that load from storing its result, so the item can be cached again with data read before the removal.
    /// When the memory cache is disabled for the type, a caller that missed just as another load finished cannot see that result and may load the same key again.
    /// If <paramref name="factory"/> throws, the exception is thrown to every caller waiting on that load (it is not logged), nothing is cached, and the next call retries.
    /// A null/default result is returned and is not cached. If both layers are disabled for the type, <paramref name="factory"/> is called directly, without sharing.
    /// Do not capture request-scoped objects in <paramref name="factory"/>: the factory of the first caller runs on behalf of every caller waiting on the same key.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The cache key suffix or the factory is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask<TItem?> GetOrCreateAsync<TSuffix>(TSuffix cacheKeySuffix, Func<CancellationToken, ValueTask<TItem?>> factory, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Gets several items using their cache key suffixes, and loads the ones that are not cached with <paramref name="factory"/>, stores them in the memory and distributed cache and returns everything together.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffixes">The cache key suffixes, for example the identifiers of the items. Duplicates are ignored.</param>
    /// <param name="factory">Loads the items from the source (for example a database in a single query). It receives only the suffixes that are not cached and that no other caller is already loading, so it can be called with fewer suffixes than requested, or not at all. It also receives a cancellation token shared by the callers waiting on the load, not the caller's own (see the remarks). It returns the items found, keyed by suffix: a suffix that is missing from the result, or whose value is null/default, is considered not found.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token. It cancels the wait of this caller; the load itself is canceled only when every caller waiting on it has canceled.</param>
    /// <returns>A new read-only dictionary containing one entry per distinct suffix received: the key is the suffix, the value is the cached or loaded item, or null/default if it does not exist. An empty collection of suffixes returns an empty dictionary without calling <paramref name="factory"/>.</returns>
    /// <remarks>
    /// Stampede protection: concurrent misses for the same key share a single load, so a key is loaded once even when several callers ask for it at the same time, and a caller can load some of its keys and wait for the others. This only protects within one process: with several nodes, each node loads once.
    /// The factory's cancellation token is shared by every caller waiting on the load and is canceled only when all of them have canceled, so a caller that gives up never cancels the load for the others. A cancellation callback registered on that token runs while a lock is held, so it must not block waiting for the load to finish. A factory that ignores the token runs to completion and its result is cached.
    /// Once <paramref name="factory"/> has returned, its result is stored even if every caller has left in the meantime. A removal made while a load is in flight does not stop that load from storing its result, so the item can be cached again with data read before the removal.
    /// When the memory cache is disabled for the type, a caller that missed just as another load finished cannot see that result and may load the same key again.
    /// The items returned by <paramref name="factory"/> are stored in batches. A suffix the factory did not return is not cached, so it is loaded again on the next call. To treat a missing item as an error, check the result for null/default values after the call instead of throwing in <paramref name="factory"/>, which would fail the whole batch and prevent the items found from being cached.
    /// If <paramref name="factory"/> throws, the exception is thrown to every caller waiting on those keys (it is not logged), nothing is cached for them, and the next call retries. If both layers are disabled for the type, <paramref name="factory"/> is called directly with all the suffixes, without sharing. If it returns null instead of a dictionary, an <see cref="InvalidOperationException"/> is thrown to the callers waiting on those keys.
    /// Do not capture request-scoped objects in <paramref name="factory"/>: the factory of the caller that starts a load runs on behalf of every caller waiting on the same keys.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The cache key suffixes or the factory are null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The cache key suffixes contain a null value or a value whose string representation is empty or whitespace, the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask<IReadOnlyDictionary<TSuffix, TItem?>> GetOrCreateItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, Func<IReadOnlyCollection<TSuffix>, CancellationToken, ValueTask<IReadOnlyDictionary<TSuffix, TItem?>>> factory, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion GetOrCreate

    #endregion Get
    
    #region Set

    /// <summary>
    /// Adds an item to the memory and distributed cache without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>), for example a configuration object.
    /// </summary>
    /// <param name="item">The object to cache. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>SetAsync(item, "abc")</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>SetAsync(item, tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The item is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask SetAsync(TItem item, string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an item to the memory and distributed cache using its cache key suffix.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="item">The object to cache. Cannot be null.</param>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException">The item or the cache key suffix is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask SetAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Adds several items to the memory and distributed cache. An empty collection of items does nothing.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="items">The objects to cache. Key: cache key suffix, Value: object to cache (cannot be null).</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException">The items are null, one of the values is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">A key is null or its string representation is empty or whitespace, the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask SetItemsAsync<TSuffix>(IReadOnlyDictionary<TSuffix, TItem> items, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion Set

    #region Remove

    /// <summary>
    /// Removes the item cached without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>) from the memory and distributed cache.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>RemoveAsync("abc")</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>RemoveAsync(tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">No tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask RemoveAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item from the memory and distributed cache using its cache key suffix.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException">The cache key suffix is null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask RemoveAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Removes several items from the memory and distributed cache using their cache key suffixes. An empty collection of suffixes does nothing.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffixes">The cache key suffixes, for example the identifiers of the items. Duplicates are ignored.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the default tenant. Required when no default tenant is configured (multi-tenancy). Must be null for global types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// The removal from the distributed cache is done in batches, so a failure part-way through can leave some keys removed and others not. The error is logged, and the memory cache is still cleared for every key.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The cache key suffixes are null, or no tenant ID was passed and no default tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The cache key suffixes contain a null value or a value whose string representation is empty or whitespace, the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'), or a tenant ID was passed for a global type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask RemoveItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion Remove
}