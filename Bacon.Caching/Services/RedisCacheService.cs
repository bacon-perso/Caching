using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Bacon.Caching.Services;

internal sealed class RedisCacheService<TItem>(IOptions<CacheServerOptions> cacheServerOptionsOptions, ConnectionMultiplexer connectionMultiplexer, IDatabase database, ICacheSerializerService cacheSerializerService) : IDistributedCacheService<TItem>
{
    #region DI

    private readonly RedisCacheOptions _redisCacheOptions = (cacheServerOptionsOptions.Value.DistributedCacheOptions as RedisCacheOptions)!;
    private static readonly string _connectionErrorMessage = "Error connecting to redis while executing ";

    #endregion DI

    #region Get

    public async Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(_connectionErrorMessage + nameof(GetAsync));

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

        ValidateIsConnected(_connectionErrorMessage + nameof(GetItemsAsync));

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

        ValidateIsConnected(_connectionErrorMessage + nameof(SetAsync));

        await database.StringSetAsync(key, cacheSerializerService.Serialize(value), cacheDuration);
    }

    public async Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string errorMessage = _connectionErrorMessage + nameof(SetItemsAsync);

        ValidateIsConnected(errorMessage);

        ConcurrentQueue<Exception> exceptions = [];
        KeyValuePair<string, TItem>[][] batches = [.. items.Chunk(_redisCacheOptions.InsertBatchSize)];

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(batches, parallelOptions, async (batch, _) =>
        {
            try
            {
                ValidateIsConnected(errorMessage);

                await AddAllAsync(batch, cacheDuration);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                exceptions.Enqueue(ex);
            }
        });

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException($"Error while setting redis items: {exceptions.Count} of {batches.Length} batches failed", exceptions);
        }
    }

    #endregion Set

    #region Remove

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(_connectionErrorMessage + nameof(RemoveAsync));

        await database.KeyDeleteAsync(key);
    }

    public async Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string errorMessage = _connectionErrorMessage + nameof(RemoveItemsAsync);

        ValidateIsConnected(errorMessage);

        RedisKey[] redisKeys = [.. keys.Select(s => (RedisKey)s)];

        ConcurrentQueue<Exception> exceptions = [];
        RedisKey[][] batches = [.. redisKeys.Chunk(_redisCacheOptions.RemoveBatchSize)];

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(batches, parallelOptions, async (batch, _) =>
        {
            try
            {
                ValidateIsConnected(errorMessage);

                await database.KeyDeleteAsync(batch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                exceptions.Enqueue(ex);
            }
        });

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException($"Error while removing redis items: {exceptions.Count} of {batches.Length} batches failed", exceptions);
        }
    }

    #endregion Remove

    #region Privates

    private async Task AddAllAsync(KeyValuePair<string, TItem>[] items, TimeSpan cacheDuration)
    {
        // Serialize the whole chunk first: if one item fails, no command has been sent yet.
        RedisValue[] values = new RedisValue[items.Length];
        for (int idx = 0; idx < items.Length; ++idx)
        {
            values[idx] = cacheSerializerService.Serialize(items[idx].Value);
        }

        Task[] tasks = new Task[items.Length];
        for (int idx = 0; idx < items.Length; ++idx)
        {
            tasks[idx] = database.StringSetAsync(items[idx].Key, values[idx], cacheDuration, when: When.Always);
        }

        // Task.WhenAll rethrows only the first exception when awaited; keep the task so every failure is rethrown together.
        Task allTasks = Task.WhenAll(tasks);

        try
        {
            await allTasks;
        }
        catch
        {
            throw allTasks.Exception!;
        }
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