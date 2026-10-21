using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Redis.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace Bacon.Caching.Redis.Services;

internal sealed class ClusterCacheService<TItem>(RedisCacheConnection redisCacheConnection, ICacheSerializerService cacheSerializerService, IOptions<RedisCacheOptions> options)
    : BaseRedisCacheService<TItem>(redisCacheConnection, cacheSerializerService, options)
{
    #region Get

    public override async Task<Dictionary<string, TItem?>> GetItemsAsync(ReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(ConnectionErrorMessage + nameof(GetItemsAsync));

        TItem?[] values = new TItem?[keys.Count];

        (string Key, int Index)[][] batches = CreateBatches(keys, RedisOptions.GetBatchSize, key => Connection.Multiplexer.HashSlot(key));

        foreach ((string Key, int Index)[][] wave in CreateWaves(batches, RedisOptions.GetBatchSize * MaxParallelBatches))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(wave.Select(batch => GetBatchAsync(batch, values)));
        }

        return ToDictionary(keys, values);
    }

    #endregion Get

    #region Remove

    public override async Task RemoveItemsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string errorMessage = ConnectionErrorMessage + nameof(RemoveItemsAsync);

        ValidateIsConnected(errorMessage);

        ConcurrentQueue<Exception> exceptions = [];
        (string Key, int Index)[][] batches = CreateBatches([.. keys], RedisOptions.RemoveBatchSize, key => Connection.Multiplexer.HashSlot(key));

        foreach ((string Key, int Index)[][] wave in CreateWaves(batches, RedisOptions.RemoveBatchSize * MaxParallelBatches))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(wave.Select(async batch =>
            {
                try
                {
                    ValidateIsConnected(errorMessage);

                    RedisKey[] redisKeys = new RedisKey[batch.Length];
                    for (int idx = 0; idx < batch.Length; ++idx)
                    {
                        redisKeys[idx] = batch[idx].Key;
                    }

                    await Connection.Database.KeyDeleteAsync(redisKeys);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Enqueue(ex);
                }
            }));
        }

        ThrowIfAnyFailed("removing", exceptions, batches.Length);
    }

    #endregion Remove

    #region Privates

    /// <summary>
    /// Splits the keys into batches of at most <paramref name="batchSize"/> keys that never mix hash slots, keeping each key's original index.
    /// </summary>
    internal static (string Key, int Index)[][] CreateBatches(ReadOnlyCollection<string> keys, int batchSize, Func<string, int> getSlot)
    {
        return [.. keys
            .Select((key, index) => (Key: key, Index: index))
            .GroupBy(item => getSlot(item.Key))
            .SelectMany(group => group.Chunk(batchSize))];
    }

    /// <summary>
    /// Groups the batches into waves holding at most <paramref name="maxKeysInFlight"/> keys (a wave always holds at least one batch).
    /// The batches of a wave are sent together without waiting for each reply, and the next wave starts once the whole wave is done.
    /// </summary>
    internal static List<(string Key, int Index)[][]> CreateWaves((string Key, int Index)[][] batches, int maxKeysInFlight)
    {
        List<(string Key, int Index)[][]> waves = [];
        List<(string Key, int Index)[]> wave = [];
        int keysInWave = 0;

        foreach ((string Key, int Index)[] batch in batches)
        {
            if (wave.Count > 0 && keysInWave + batch.Length > maxKeysInFlight)
            {
                waves.Add([.. wave]);
                wave.Clear();
                keysInWave = 0;
            }

            wave.Add(batch);
            keysInWave += batch.Length;
        }

        if (wave.Count > 0)
        {
            waves.Add([.. wave]);
        }

        return waves;
    }

    private async Task GetBatchAsync((string Key, int Index)[] batch, TItem?[] values)
    {
        RedisKey[] redisKeys = new RedisKey[batch.Length];
        for (int idx = 0; idx < batch.Length; ++idx)
        {
            redisKeys[idx] = batch[idx].Key;
        }

        RedisValue[] redisValues = await Connection.Database.StringGetAsync(redisKeys, CommandFlags.None);

        for (int idx = 0; idx < redisValues.Length; ++idx)
        {
            if (redisValues[idx].HasValue)
            {
                values[batch[idx].Index] = CacheSerializerService.Deserialize<TItem>(redisValues[idx].ToString());
            }
        }
    }

    #endregion Privates
}