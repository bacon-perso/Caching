using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Redis.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Bacon.Caching.Redis.Services;

internal abstract class BaseRedisCacheService<TItem>(RedisCacheConnection redisCacheConnection, ICacheSerializerService cacheSerializerService, IOptions<RedisCacheOptions> options) : IDistributedCacheService<TItem>
{
    #region DI

    protected RedisCacheConnection Connection { get; } = redisCacheConnection;
    protected ICacheSerializerService CacheSerializerService { get; } = cacheSerializerService;
    protected RedisCacheOptions RedisOptions { get; } = options.Value;
    protected const string ConnectionErrorMessage = "Error connecting to redis while executing ";
    protected const int MaxParallelBatches = 2;

    #endregion DI

    #region Get

    public async Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(ConnectionErrorMessage + nameof(GetAsync));

        RedisValue redisValue = await Connection.Database.StringGetAsync(key, CommandFlags.None);

        if (!redisValue.HasValue)
        {
            return default;
        }

        return CacheSerializerService.Deserialize<TItem?>(redisValue.ToString());
    }

    public abstract Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion Get

    #region Set

    public async Task SetAsync(string key, TItem value, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(ConnectionErrorMessage + nameof(SetAsync));

        await Connection.Database.StringSetAsync(key, CacheSerializerService.Serialize(value), cacheDuration);
    }

    public async Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string errorMessage = ConnectionErrorMessage + nameof(SetItemsAsync);

        ValidateIsConnected(errorMessage);

        ConcurrentQueue<Exception> exceptions = [];
        KeyValuePair<string, TItem>[][] batches = [.. items.Chunk(RedisOptions.InsertBatchSize)];

        await Parallel.ForEachAsync(batches, CreateParallelOptions(cancellationToken), async (batch, _) =>
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

        ThrowIfAnyFailed("setting", exceptions, batches.Length);
    }

    #endregion Set

    #region Remove

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIsConnected(ConnectionErrorMessage + nameof(RemoveAsync));

        await Connection.Database.KeyDeleteAsync(key);
    }

    public abstract Task RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    #endregion Remove

    #region Protected

    protected void ValidateIsConnected(string message)
    {
        if (!Connection.Multiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, message);
        }
    }

    protected static ParallelOptions CreateParallelOptions(CancellationToken cancellationToken)
    {
        return new()
        {
            MaxDegreeOfParallelism = MaxParallelBatches,
            CancellationToken = cancellationToken
        };
    }

    protected static void ThrowIfAnyFailed(string action, ConcurrentQueue<Exception> exceptions, int batchCount)
    {
        if (!exceptions.IsEmpty)
        {
            throw new AggregateException($"Error while {action} redis items: {exceptions.Count} of {batchCount} batches failed", exceptions);
        }
    }

    protected static Dictionary<string, TItem?> ToDictionary(string[] keys, TItem?[] values)
    {
        Dictionary<string, TItem?> returnValues = new(keys.Length);
        for (int idx = 0; idx < keys.Length; ++idx)
        {
            returnValues.Add(keys[idx], values[idx]);
        }

        return returnValues;
    }

    #endregion Protected

    #region Privates

    private async Task AddAllAsync(KeyValuePair<string, TItem>[] items, TimeSpan cacheDuration)
    {
        // Serialize the whole chunk first: if one item fails, no command has been sent yet.
        RedisValue[] values = new RedisValue[items.Length];
        for (int idx = 0; idx < items.Length; ++idx)
        {
            values[idx] = CacheSerializerService.Serialize(items[idx].Value);
        }

        Task[] tasks = new Task[items.Length];
        for (int idx = 0; idx < items.Length; ++idx)
        {
            tasks[idx] = Connection.Database.StringSetAsync(items[idx].Key, values[idx], cacheDuration, when: When.Always);
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

    #endregion Privates
}