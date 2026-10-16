using Bacon.Caching.Interfaces.Services;
using System.Collections.Concurrent;

namespace Bacon.Caching.Tests.Fakes;

internal sealed class FakeDistributedCacheService<TItem> : IDistributedCacheService<TItem>
{
    private readonly ConcurrentDictionary<string, TItem?> _store = [];

    /// <summary>
    /// When set, every operation throws this exception (simulates an unreachable distributed cache)
    /// </summary>
    public Exception? FailWith { get; set; }

    /// <summary>
    /// The keys requested through <see cref="GetItemsAsync"/>
    /// </summary>
    public List<string> RequestedKeys { get; } = [];

    public Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        _store.TryGetValue(key, out TItem? value);

        return Task.FromResult(value);
    }

    public Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        string[] keyArray = [.. keys];

        RequestedKeys.AddRange(keyArray);

        Dictionary<string, TItem?> result = [];
        foreach (string key in keyArray)
        {
            _store.TryGetValue(key, out TItem? value);
            result[key] = value;
        }

        return Task.FromResult(result);
    }

    public Task SetAsync(string key, TItem value, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        _store[key] = value;

        return Task.CompletedTask;
    }

    public Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        foreach (KeyValuePair<string, TItem> item in items)
        {
            _store[item.Key] = item.Value;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        _store.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    public Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        foreach (string key in keys)
        {
            _store.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public void Seed(string key, TItem value) => _store[key] = value;
    public void Clear() => _store.Clear();
    public bool Contains(string key) => _store.ContainsKey(key);

    private void ThrowIfFailing()
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }
    }
}