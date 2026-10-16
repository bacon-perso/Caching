namespace Bacon.Caching.Interfaces.Services;

/// <summary>
/// Contract implemented by a distributed cache provider (for example Redis). The keys received are the final cache keys built by the library, so a provider stores and reads them as they are.
/// </summary>
/// <typeparam name="TItem">The type of the cached items.</typeparam>
/// <remarks>
/// A provider registers its implementation as the open generic <c>IDistributedCacheService&lt;&gt;</c>. The library validates the arguments before calling it (no null item, no blank key, no empty collection),
/// and logs any exception thrown by a provider (except <see cref="OperationCanceledException"/>) instead of throwing it to the caller, so the memory layer keeps working when the provider fails.
/// </remarks>
public interface IDistributedCacheService<TItem>
{
    #region Get

    /// <summary>
    /// Gets the item stored under a key.
    /// </summary>
    /// <param name="key">The final cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The item, or null/default if not present.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets several items.
    /// </summary>
    /// <param name="keys">The final cache keys.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary containing one entry per requested key: the key, and the item or null/default if not present.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion Get

    #region Set

    /// <summary>
    /// Stores an item under a key, replacing any existing item.
    /// </summary>
    /// <param name="key">The final cache key.</param>
    /// <param name="value">The item to store. Never null.</param>
    /// <param name="cacheDuration">How long the item stays in the distributed cache.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    Task SetAsync(string key, TItem value, TimeSpan cacheDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores several items, replacing any existing items.
    /// </summary>
    /// <param name="items">The items to store. Key: final cache key, Value: item to store (never null).</param>
    /// <param name="cacheDuration">How long the items stay in the distributed cache. It applies to every item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default);

    #endregion Set

    #region Remove

    /// <summary>
    /// Removes the item stored under a key. A key that does not exist is not an error.
    /// </summary>
    /// <param name="key">The final cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes several items. Keys that do not exist are not an error.
    /// </summary>
    /// <param name="keys">The final cache keys.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="OperationCanceledException">The operation was canceled through the cancellation token.</exception>
    Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion Remove
}