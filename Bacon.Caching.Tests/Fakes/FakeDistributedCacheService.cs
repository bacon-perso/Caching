using Bacon.Caching.Interfaces.Services;
using System.Collections.Concurrent;

namespace Bacon.Caching.Tests.Fakes;

internal sealed class FakeDistributedCacheService<TItem> : IDistributedCacheService<TItem>
{
    private readonly ConcurrentDictionary<string, TItem?> _store = [];

    public Task<TItem?> GetItemAsync(string key)
    {
        _store.TryGetValue(key, out TItem? value);
        return Task.FromResult(value);
    }

    public Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys)
    {
        Dictionary<string, TItem?> result = [];
        foreach (string key in keys)
        {
            _store.TryGetValue(key, out TItem? value);
            result[key] = value;
        }
        return Task.FromResult(result);
    }

    public Task SetItemAsync(string key, TItem value, TimeSpan cacheDuration)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration)
    {
        foreach (KeyValuePair<string, TItem> item in items)
            _store[item.Key] = item.Value;
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveItemsAsync(IEnumerable<string> keys)
    {
        foreach (string key in keys)
            _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task FlushItemsAsync(string keyPattern)
    {
        string prefix = keyPattern.TrimEnd('*');
        foreach (string key in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public void Seed(string key, TItem value) => _store[key] = value;
    public void Clear() => _store.Clear();
    public bool Contains(string key) => _store.ContainsKey(key);
}
