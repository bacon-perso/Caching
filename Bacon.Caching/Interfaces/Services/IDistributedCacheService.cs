namespace Bacon.Caching.Interfaces.Services;

internal interface IDistributedCacheService<TItem>
{
    #region Get

    Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion Get

    #region Set

    Task SetAsync(string key, TItem value, TimeSpan cacheDuration, CancellationToken cancellationToken = default);

    Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default);

    #endregion Set

    #region Remove

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion Remove
}