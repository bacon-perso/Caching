using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Redis.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Bacon.Caching.Redis.Services;

internal sealed class StandaloneCacheService<TItem>(RedisCacheConnection redisCacheConnection, ICacheSerializerService cacheSerializerService, IOptions<RedisCacheOptions> options)
    : BaseRedisCacheService<TItem>(redisCacheConnection, cacheSerializerService, options)
{
    #region Get

    public override async Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(ConnectionErrorMessage + nameof(GetItemsAsync));

        string[] cacheKeys = [.. keys];
        TItem?[] values = new TItem?[cacheKeys.Length];

        int batchSize = RedisOptions.GetBatchSize;
        int batchCount = (cacheKeys.Length + batchSize - 1) / batchSize;

        await Parallel.ForEachAsync(Enumerable.Range(0, batchCount), CreateParallelOptions(cancellationToken), async (batchIndex, _) =>
        {
            int start = batchIndex * batchSize;
            int length = Math.Min(batchSize, cacheKeys.Length - start);

            RedisKey[] redisKeys = new RedisKey[length];
            for (int idx = 0; idx < length; ++idx)
            {
                redisKeys[idx] = cacheKeys[start + idx];
            }

            RedisValue[] redisValues = await Connection.Database.StringGetAsync(redisKeys, CommandFlags.None);

            for (int idx = 0; idx < redisValues.Length; ++idx)
            {
                if (redisValues[idx].HasValue)
                {
                    values[start + idx] = CacheSerializerService.Deserialize<TItem>(redisValues[idx].ToString());
                }
            }
        });

        return ToDictionary(cacheKeys, values);
    }

    #endregion Get

    #region Remove

    public override async Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string errorMessage = ConnectionErrorMessage + nameof(RemoveItemsAsync);

        ValidateIsConnected(errorMessage);

        RedisKey[] redisKeys = [.. keys.Select(s => (RedisKey)s)];

        ConcurrentQueue<Exception> exceptions = [];
        RedisKey[][] batches = [.. redisKeys.Chunk(RedisOptions.RemoveBatchSize)];

        await Parallel.ForEachAsync(batches, CreateParallelOptions(cancellationToken), async (batch, _) =>
        {
            try
            {
                ValidateIsConnected(errorMessage);

                await Connection.Database.KeyDeleteAsync(batch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                exceptions.Enqueue(ex);
            }
        });

        ThrowIfAnyFailed("removing", exceptions, batches.Length);
    }

    #endregion Remove
}