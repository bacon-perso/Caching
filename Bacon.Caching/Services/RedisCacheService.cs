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

    #endregion DI

    #region Get

    public async Task<TItem?> GetItemAsync(string key)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while getting redis item");
        }

        RedisValue redisValue = await database.StringGetAsync(key, CommandFlags.None);

        if (!redisValue.HasValue)
        {
            return default;
        }
        
        return cacheSerializerService.Deserialize<TItem?>(redisValue.ToString());
    }

    public async Task<Dictionary<string, TItem?>> GetItemsAsync(IEnumerable<string> keys)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while getting redis items");
        }

        RedisValue[] redisValues = await database.StringGetAsync([.. keys.Select(s => new RedisKey(s))], CommandFlags.None);

        Dictionary<string, TItem?> returnValues = [];
        for (int idx = 0; idx < redisValues.Length; ++idx)
        {
            TItem? value = redisValues[idx].HasValue ? cacheSerializerService.Deserialize<TItem>(redisValues[idx].ToString()) : default;

            returnValues.Add(keys.ElementAt(idx), value);
        }

        return returnValues;
    }

    #endregion Get

    #region Set

    public async Task SetItemAsync(string key, TItem value, TimeSpan cacheDuration)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while setting redis item");
        }

        await database.StringSetAsync(key, cacheSerializerService.Serialize(value), cacheDuration, flags: CommandFlags.FireAndForget);
    }

    public async Task SetItemsAsync(IDictionary<string, TItem> items, TimeSpan cacheDuration)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while setting redis items");
        }

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 2
        };

        ConcurrentDictionary<int, List<KeyValuePair<string, TItem>>> concurrentDic = new();

        //insert the items in batch using parallelism
        Parallel.ForEach(items, parallelOptions, kv =>
        {
            int currentThreadId = Environment.CurrentManagedThreadId;

            if (!concurrentDic.ContainsKey(currentThreadId))
            {
                concurrentDic.TryAdd(currentThreadId, []);
            }

            KeyValuePair<string, TItem> keyValuePair = new(kv.Key, kv.Value);

            concurrentDic[currentThreadId].Add(keyValuePair);

            if (concurrentDic[currentThreadId].Count % _redisCacheOptions.InsertBatchSize == 0)
            {
                AddAllAsync(concurrentDic[currentThreadId], cacheDuration);
                concurrentDic[currentThreadId].Clear();
            }
        });

        //insert the remaining items that did not fit in a batch
        Parallel.ForEach(concurrentDic, parallelOptions, kv =>
        {
            if (kv.Value.Count != 0)
            {
                AddAllAsync(kv.Value, cacheDuration);
                kv.Value.Clear();
            }
        });

        await Task.CompletedTask;
    }

    #endregion Set

    #region Remove

    public async Task RemoveItemAsync(string key)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while removing redis item");
        }

        await database.KeyDeleteAsync(key);
    }

    public async Task RemoveItemsAsync(IEnumerable<string> keys)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while removing redis items");
        }

        int count = keys.Count();

        if (count == 0)
        {
            return;
        }

        RedisKey[] redisKeys = new RedisKey[count];

        for (int idx = 0; idx < count; ++idx)
        {
            redisKeys[idx] = keys.ElementAt(idx);
        }

        await database.KeyDeleteAsync(redisKeys);
    }

    #endregion Remove

    #region Flush

    public async Task FlushItemsAsync(string keyPattern)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Error while flusing redis items");
        }

        // Lua script to delete keys
        string script = $@"
local cursor = 0
local keyNum = 0  
repeat
   local res = redis.call('scan',cursor,'MATCH',@key,'COUNT',{_redisCacheOptions.FlushBatchSize})
   if(res ~= nil and #res>=0) 
   then
      cursor = tonumber(res[1])
      local ks = res[2]
      if(ks ~= nil and #ks>0) 
      then
         for i=1,#ks,1 do
            local key = tostring(ks[i])
            redis.call('DEL',key)
         end
         keyNum = keyNum + #ks
      end
     end
until( cursor <= 0 )
return keyNum
";

        IServer server = connectionMultiplexer.GetServer($"{_redisCacheOptions.Server}:{_redisCacheOptions.Port}");

        LuaScript preparedScript = LuaScript.Prepare(script);
        LoadedLuaScript loaded = await preparedScript.LoadAsync(server);
        await loaded.EvaluateAsync(database, new { key = keyPattern });
    }

    #endregion Flush

    #region Privates

    private async void AddAllAsync(IEnumerable<KeyValuePair<string, TItem>> items, TimeSpan cacheDuration)
    {
        CommandFlags commandFlags = CommandFlags.FireAndForget;
        When when = When.Always;

        KeyValuePair<RedisKey, RedisValue>[] values = SerializeIterations(items);

        Task[] tasks = new Task[values.Length];
        await database.StringSetAsync(values, when: when, flags: commandFlags);

        for (int idx = 0; idx < values.Length; ++idx)
        {
            tasks[idx] = database.KeyExpireAsync(values[idx].Key, cacheDuration, commandFlags);
        }

        await Task.WhenAll(tasks);
    }

    private KeyValuePair<RedisKey, RedisValue>[] SerializeIterations(IEnumerable<KeyValuePair<string, TItem>> items)
    {
        List<KeyValuePair<RedisKey, RedisValue>> redisValues = [];
        foreach (KeyValuePair<string, TItem> item in items)
        {
            redisValues.Add(new(new(item.Key), new(cacheSerializerService.Serialize(item.Value))));
        }

        return [.. redisValues];
    }

    #endregion Privates
}