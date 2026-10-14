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
/// The cache key has the format <c>{tenantId}_{CacheKey}{suffix}</c>.
/// </remarks>
public interface ICacheService<TItem>
{
    #region Get

    /// <summary>
    /// Gets the item cached without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>), for example a configuration object.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached item, or null/default if not present.</returns>
    /// <remarks>
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>GetAsync("abc")</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>GetAsync(tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">No tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask<TItem?> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single item from the cache using its cache key suffix.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached item, or null/default if not present.</returns>
    /// <exception cref="ArgumentNullException">The cache key suffix is null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask<TItem?> GetAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Gets several items from the cache using their cache key suffixes.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffixes">The cache key suffixes, for example the identifiers of the items. Duplicates are ignored.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary containing one entry per distinct suffix received: the key is the suffix, the value is the cached item, or null/default if not present.</returns>
    /// <exception cref="ArgumentNullException">The cache key suffixes are null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The cache key suffixes are empty, contain a null value or a value whose string representation is empty or whitespace, or the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask<IDictionary<TSuffix, TItem?>> GetItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion Get

    #region Set

    /// <summary>
    /// Adds an item to the memory and distributed cache without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>), for example a configuration object.
    /// </summary>
    /// <param name="item">The object to cache. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>SetAsync(item, "abc")</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>SetAsync(item, tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The item is null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask SetAsync(TItem item, string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an item to the memory and distributed cache using its cache key suffix.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="item">The object to cache. Cannot be null.</param>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException">The item or the cache key suffix is null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask SetAsync<TSuffix>(TItem item, TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Adds several items to the memory and distributed cache.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="items">The objects to cache. Key: cache key suffix, Value: object to cache (cannot be null).</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException">The items are null, one of the values is null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The items are empty, a key is null or its string representation is empty or whitespace, or the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask SetItemsAsync<TSuffix>(IDictionary<TSuffix, TItem> items, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion Set

    #region Remove

    /// <summary>
    /// Removes the item cached without a suffix (the cache key is <c>{tenantId}_{CacheKey}</c>) from the memory and distributed cache.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Because <paramref name="tenantId"/> is a string, a positional string argument (for example <c>RemoveAsync("abc")</c>) is treated as a cache key suffix.
    /// To pass only a tenant ID, name the argument: <c>RemoveAsync(tenantId: "abc")</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">No tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask RemoveAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item from the memory and distributed cache using its cache key suffix.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffix">The cache key suffix, for example the identifier of the item. Cannot be null.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException">The cache key suffix is null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    [OverloadResolutionPriority(1)]
    ValueTask RemoveAsync<TSuffix>(TSuffix cacheKeySuffix, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    /// <summary>
    /// Removes several items from the memory and distributed cache using their cache key suffixes.
    /// </summary>
    /// <typeparam name="TSuffix">The type of the cache key suffix, for example <see cref="Guid"/>, <see cref="string"/> or <see cref="int"/>. The suffix is converted with <c>ToString()</c> to build the cache key, so custom types must override it with a deterministic value.</typeparam>
    /// <param name="cacheKeySuffixes">The cache key suffixes, for example the identifiers of the items. Duplicates are ignored.</param>
    /// <param name="tenantId">Optional tenant ID that overrides the global tenant. Required when no global tenant is configured (multi-tenancy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// The removal from the distributed cache is done in batches, so a failure part-way through can leave some keys removed and others not. The error is logged, and the memory cache is still cleared for every key.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The cache key suffixes are null, or no tenant ID was passed and no global tenant ID is configured.</exception>
    /// <exception cref="ArgumentException">The cache key suffixes are empty, contain a null value or a value whose string representation is empty or whitespace, or the tenant ID is empty or invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    ValueTask RemoveItemsAsync<TSuffix>(IEnumerable<TSuffix> cacheKeySuffixes, string? tenantId = null, CancellationToken cancellationToken = default) where TSuffix : notnull;

    #endregion Remove
}
