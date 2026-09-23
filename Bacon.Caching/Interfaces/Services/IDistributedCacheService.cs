namespace Bacon.Caching.Interfaces.Services;

internal interface IDistributedCacheService<TItem>
{
    #region Get

    Task<TItem?> GetItemAsync(string key);

    Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys);

    #endregion Get

    #region Set

    Task SetItemAsync(string key, TItem value, TimeSpan cacheDuration);

    Task SetItemsAsync(IDictionary<string, TItem> dicKeyValues, TimeSpan cacheDuration);

    #endregion Set

    #region Remove

    Task RemoveItemAsync(string key);

    Task RemoveItemsAsync(IEnumerable<string> keys);

    #endregion Remove

    #region Flush

    Task FlushItemsAsync(string keyPattern);

    #endregion Flush
}