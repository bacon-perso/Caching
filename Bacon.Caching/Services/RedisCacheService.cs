using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Bacon.Caching.Services;

internal sealed class RedisCacheService<TItem>(IOptions<CacheServerOptions> cacheServerOptionsOptions, ConnectionMultiplexer connectionMultiplexer, IDatabase database, ICacheSerializerService cacheSerializerService) : IDistributedCacheService<TItem>
{
    #region DI

    private readonly RedisCacheOptions _redisCacheOptions = (cacheServerOptionsOptions.Value.DistributedCacheOptions as RedisCacheOptions)!;

    #endregion DI

    #region Get

    public async Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected("Error while getting redis item");

        RedisValue redisValue = await database.StringGetAsync(key, CommandFlags.None);

        if (!redisValue.HasValue)
        {
            return default;
        }
        
        return cacheSerializerService.Deserialize<TItem?>(redisValue.ToString());
    }

    public async Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected("Error while getting redis items");

        string[] cacheKeys = [.. keys];
        TItem?[] values = new TItem?[cacheKeys.Length];

        int batchSize = _redisCacheOptions.GetBatchSize;
        int batchCount = (cacheKeys.Length + batchSize - 1) / batchSize;

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, batchCount), parallelOptions, async (batchIndex, _) =>
        {
            int start = batchIndex * batchSize;
            int length = Math.Min(batchSize, cacheKeys.Length - start);

            RedisKey[] redisKeys = new RedisKey[length];
            for (int idx = 0; idx < length; ++idx)
            {
                redisKeys[idx] = cacheKeys[start + idx];
            }

            RedisValue[] redisValues = await database.StringGetAsync(redisKeys, CommandFlags.None);

            for (int idx = 0; idx < redisValues.Length; ++idx)
            {
                if (redisValues[idx].HasValue)
                {
                    values[start + idx] = cacheSerializerService.Deserialize<TItem>(redisValues[idx].ToString());
                }
            }
        });

        Dictionary<string, TItem?> returnValues = new(cacheKeys.Length);
        for (int idx = 0; idx < cacheKeys.Length; ++idx)
        {
            returnValues.Add(cacheKeys[idx], values[idx]);
        }

        return returnValues;
    }

    #endregion Get

    #region Set

    public async Task SetAsync(string key, TItem value, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected("Error while setting redis item");

        await database.StringSetAsync(key, cacheSerializerService.Serialize(value), cacheDuration, flags: CommandFlags.FireAndForget);
    }

    public async Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected("Error while setting redis items");

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(items.Chunk(_redisCacheOptions.InsertBatchSize), parallelOptions, async (batch, _) =>
        {
            await AddAllAsync(batch, cacheDuration);
        });
    }

    #endregion Set

    #region Remove

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected("Error while removing redis item");

        await database.KeyDeleteAsync(key);
    }

    public async Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected("Error while removing redis items");

        RedisKey[] redisKeys = [.. keys.Select(s => (RedisKey)s)];

        if (redisKeys.Length == 0)
        {
            return;
        }

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(redisKeys.Chunk(_redisCacheOptions.RemoveBatchSize), parallelOptions, async (batch, _) =>
        {
            await database.KeyDeleteAsync(batch);
        });
    }

    #endregion Remove

    #region Privates

    private async Task AddAllAsync(IEnumerable<KeyValuePair<string, TItem>> items, TimeSpan cacheDuration)
    {
        List<Task> tasks = [];
        foreach (KeyValuePair<string, TItem> item in items)
        {
            tasks.Add(database.StringSetAsync(item.Key, cacheSerializerService.Serialize(item.Value), cacheDuration, when: When.Always, flags: CommandFlags.FireAndForget));
        }

        await Task.WhenAll(tasks);
    }

    private void ValidateIsConnected(string message)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, message);
        }
    }

    #endregion Privates
}