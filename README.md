# Bacon.Caching

A .NET dual-layer cache library. Items are stored in both in-process memory and a distributed cache (Redis). On reads, memory is checked first; on a miss the distributed cache is queried and the result is backfilled into memory. If the distributed cache is unavailable the library degrades gracefully — errors are logged and reads return `null` instead of throwing. `GetOrCreateAsync` and `GetOrCreateItemsAsync` also load the items that are not cached from your own source and protect it from stampedes: concurrent misses for the same key share a single load.

## Installation

```bash
dotnet add package Bacon.Caching
dotnet add package Bacon.Caching.Redis   # only if you want Redis as the distributed cache
```

`Bacon.Caching` is the core library (memory cache, `ICacheService<T>`, tenants and scopes). The Redis distributed cache is a separate package, `Bacon.Caching.Redis`, that plugs into it.

| Package | Version | Requires |
|---|---|---|
| `Bacon.Caching` | 3.0.0 | |
| `Bacon.Caching.Redis` | 1.0.0 | `Bacon.Caching` 3.0.0 or later |

### Upgrading from Bacon.Caching 2.x

Redis moved out of the core package, which is a breaking change:

- Add the `Bacon.Caching.Redis` package and register Redis with `.AddRedis(...)` after `AddCache(...)`.
- `CacheServerOptions.DistributedCacheType`, `DistributedCacheOptions`, `HealthCheckOptions` and `LoggerFactory` are gone. Set the Redis options in the `AddRedis` action instead (`Server`, `Port`, `Password`, `UseSsl`, `HealthCheckOptions`, `LoggerFactory`, …).
- `RedisCacheOptions` is now in the `Bacon.Caching.Redis.Models` namespace.
- A new `RedisCacheOptions.ServerType` (`Standalone` by default) declares whether you connect to a standalone server or to a Redis Cluster.

### Upgrading to the release with `GetOrCreateAsync`

- `GetItemsAsync` returns an `IReadOnlyDictionary<TSuffix, TItem?>` (a new dictionary per call) instead of an `IDictionary`, and `SetItemsAsync` takes an `IReadOnlyDictionary<TSuffix, TItem>`. A `Dictionary` can still be passed to `SetItemsAsync`; code that modified the dictionary returned by `GetItemsAsync` must copy it first.
- `GetOrCreateAsync` and `GetOrCreateItemsAsync` are new: see "Load on a miss".
- A distributed cache provider that implements `IDistributedCacheService<T>` must update three signatures: `GetItemsAsync(ReadOnlyCollection<string> keys, ...)`, `RemoveItemsAsync(IReadOnlyCollection<string> keys, ...)` and `SetItemsAsync(IReadOnlyDictionary<string, TItem> items, ...)`.

## Setup

### Memory-only

```csharp
using Bacon.Caching.Extensions;

builder.Services.AddCache(o =>
{
    o.TenantId = "my-app";
})
.AddItem<WeatherForecast>(o =>
{
    o.CacheKey            = "weather_";
    o.MemoryCacheDuration = TimeSpan.FromSeconds(60);
});
```

### Memory + Redis

```csharp
using Bacon.Caching.Extensions;
using Bacon.Caching.Redis.Extensions;
using Bacon.Caching.Redis.Models;

builder.Services.AddCache(o =>
{
    o.TenantId = "my-app";
})
.AddRedis(o =>
{
    o.Server     = "redis-host";
    o.Port       = 6379;
    o.Password   = "secret";
    o.UseSsl     = false;
    o.ServerType = RedisServerTypes.Standalone;   // RedisServerTypes.Cluster for a Redis Cluster
    // o.LoggerFactory      = loggerFactory;      // optional: logs the Redis connection events
    // o.HealthCheckOptions = new();              // optional: registers a health check
})
.AddItem<WeatherForecast>(o =>
{
    o.CacheKey                 = "weather_";
    o.MemoryCacheDuration      = TimeSpan.FromSeconds(60);
    o.DistributedCacheDuration = TimeSpan.FromSeconds(300);
});
```

Call `.AddItem<T>()` once per type you want to cache. Each type gets its own key prefix and TTL settings. Setting a duration to `TimeSpan.Zero` (the default) disables that layer for the type, so the same library covers memory-only, distributed-only and mixed setups.

The `Bacon.Caching.Samples` project shows a complete registration: `Program.cs` reads the Redis settings from the `CacheSettings:RedisSettings` configuration section, passes a logger factory for connection events, and registers a `WeatherForecast` type.

Without `AddRedis` only the memory cache is used. A type with a `DistributedCacheDuration` but no distributed cache registered keeps working with the memory cache, and the missing distributed cache is logged.

The Redis connection is created when `AddRedis` runs. If Redis is unreachable at startup the application still starts: the failure is logged (when a `LoggerFactory` is configured in the Redis options) and the Redis client keeps retrying in the background, which can make startup slightly longer.

Registration validates its input and throws on invalid values: a null `configureOptions` (`ArgumentNullException`), an invalid tenant ID, an empty server, a server containing a port, port `0`, an undefined `ServerType`, batch sizes outside 1 – 5 000, an empty `CacheKey` or one longer than 50 characters, a type registered twice, an undefined `Scope`, and negative durations (`ArgumentException` / `ArgumentOutOfRangeException`). Registering a second distributed cache throws an `InvalidOperationException`: only one is supported.

### Redis Cluster

Set `ServerType = RedisServerTypes.Cluster` when you connect to a Redis Cluster; keep the default `Standalone` for a single server or a primary/replica pair. The library does not detect it for you.

```csharp
.AddRedis(o =>
{
    o.Server     = "redis-cluster-node-1";   // a seed node used to discover the cluster
    o.Port       = 6379;
    o.ServerType = RedisServerTypes.Cluster;
})
```

- **Why it matters.** A Redis Cluster only accepts a command with several keys when all of them are in the same hash slot. Cache keys that differ by suffix usually are not, so with `Cluster` the library groups the keys by slot and sends the resulting small commands together (pipelined) in waves, instead of one `MGET` / `DEL` for the whole batch. At most twice the batch size (`GetBatchSize` / `RemoveBatchSize`) in keys are in flight at a time, as with a standalone server.
- **A wrong setting.** `Cluster` on a standalone server works but adds a small overhead when several items are read or removed. `Standalone` on a cluster makes reading or removing several items fail: the errors are logged and the results are missing. When a `LoggerFactory` is configured and a server is connected at startup, a mismatch is logged as a warning, in both directions.
- **Connection.** `Server` and `Port` are used as the seed node to discover the cluster. Add more seed nodes with `AdditionalEndpoints` so the connection can start even when the main server is down; the other nodes are discovered from any reachable seed. An endpoint without a `Port` uses the main `Port`. Additional endpoints are only allowed with `ServerType = RedisServerTypes.Cluster`.

```csharp
.AddRedis(o =>
{
    o.Server              = "redis-cluster-node-1";
    o.Port                = 6379;
    o.ServerType          = RedisServerTypes.Cluster;
    o.AdditionalEndpoints =
    [
        new RedisEndpoint { Server = "redis-cluster-node-2" },                 // uses the main Port
        new RedisEndpoint { Server = "redis-cluster-node-3", Port = 6380 }
    ];
})
```

## Usage

### Inject `ICacheService<T>`

```csharp
[ApiController]
[Route("api/weather")]
public class WeatherForecastController(ICacheService<WeatherForecast> weatherForecastCacheService) : ControllerBase
{
    [HttpGet]
    public async Task<WeatherForecast> GetAsync(CancellationToken cancellationToken)
    {
        // The cached forecast, or loaded once from the source and cached (see "Load on a miss")
        WeatherForecast? forecast = await weatherForecastCacheService.GetOrCreateAsync(
            async ct => await FetchFromSourceAsync(ct),
            cancellationToken: cancellationToken);

        return forecast ?? throw new InvalidOperationException("No forecast available");
    }
}
```

To work with the cache yourself, `GetAsync` only looks: it returns the cached item or `null` and never loads anything (an item found only in the distributed cache is copied into memory). `SetAsync` stores an item in both layers:

```csharp
WeatherForecast? cached = await weatherForecastCacheService.GetAsync(cancellationToken: cancellationToken);
if (cached is not null)
{
    return cached;
}

WeatherForecast forecast = FetchFromSource();

await weatherForecastCacheService.SetAsync(forecast, cancellationToken: cancellationToken);
```

All methods return `ValueTask`/`ValueTask<T>` and take an optional `tenantId` and `CancellationToken`.

Without a suffix, the call targets a single item stored at `{tenantId}_{CacheKey}` — useful for something like a configuration object.

### Suffix keys — cache multiple items of the same type

Pass a suffix to distinguish individual items. The final key is `{tenantId}_{CacheKey}{suffix}`.

```csharp
// Store a forecast keyed by day index
await weatherForecastCacheService.SetAsync(forecast, dayIndex);        // key: "my-app_weather_1"

// Retrieve it later
WeatherForecast? result = await weatherForecastCacheService.GetAsync(dayIndex);

// Remove it
await weatherForecastCacheService.RemoveAsync(dayIndex);
```

The suffix can be any non-null value (`string`, `int`, `Guid`, an enum, …). It is converted with `ToString()` to build the key, so **the value must produce a deterministic string**: custom types must override `ToString()`, and note that numbers and dates are formatted using the current culture, so instances running with different cultures sharing one Redis could build different keys for negative numbers, decimals and dates.

A `null` suffix throws `ArgumentNullException`.

### Load on a miss: `GetOrCreateAsync`

`GetOrCreateAsync` returns the cached item and, when it is not cached, loads it with a factory you provide, stores it in both layers and returns it. It replaces the usual "get, load when null, set" code:

```csharp
// Without a suffix: a single item, such as a configuration object
WeatherForecast? current = await weatherForecastCacheService.GetOrCreateAsync(
    async ct => await repository.LoadCurrentAsync(ct),
    cancellationToken: cancellationToken);

// With a suffix
WeatherForecast? forecast = await weatherForecastCacheService.GetOrCreateAsync(
    dayIndex,
    async ct => await repository.LoadForecastAsync(dayIndex, ct),
    cancellationToken: cancellationToken);

// With a tenant, named like on every other call
WeatherForecast? other = await weatherForecastCacheService.GetOrCreateAsync(
    dayIndex,
    async ct => await repository.LoadForecastAsync(dayIndex, ct),
    tenantId: "tenant-abc",
    cancellationToken: cancellationToken);
```

The factory is where you call your database (or any other source) and build the final object: whatever it returns is what gets cached. It returns `null` when the item does not exist.

**Stampede protection.** When a popular key expires, every concurrent request would reach your source. Here, concurrent misses for the same key share one call to the factory, and every caller receives its result:

- A cache hit never touches this mechanism: it is as cheap as `GetAsync`.
- **Cancellation.** Your `CancellationToken` only cancels *your* wait. The token passed to the factory is shared by every caller waiting on the load and is canceled only when all of them have canceled, so pass it to your database call. A factory that ignores it runs to completion and its result is still cached.
- **Failures.** If the factory throws, every caller waiting on that load receives the exception (it is not logged), nothing is cached, and the next call tries again.
- **Not found.** A `null` result is returned and **not cached**, so repeated requests for a missing key reach your source every time. Throw after the call if a missing item is an error for you.
- **Scope.** The protection is per process. With several nodes a hot key can still be loaded once per node when it expires, which is far fewer than once per request.
- The factory of the first caller runs on behalf of everyone waiting on the same key, so do not capture request-scoped objects in it (resolve what it needs from a singleton, or inside the factory).
- If both layers are disabled for the type, the factory is simply called every time.

### Batch operations

```csharp
// Store several items at once (key = suffix, value = item)
Dictionary<int, WeatherForecast> weatherDic = new()
{
    [1] = forecastDay1,
    [2] = forecastDay2,
    [3] = forecastDay3,
};

await weatherForecastCacheService.SetItemsAsync(weatherDic, cancellationToken: cancellationToken);

// Retrieve all of them in one call: one entry per distinct suffix, null when not cached
IReadOnlyDictionary<int, WeatherForecast?> results = await weatherForecastCacheService.GetItemsAsync(weatherDic.Keys, cancellationToken: cancellationToken);

// Remove several items at once
await weatherForecastCacheService.RemoveItemsAsync([1, 2, 3], cancellationToken: cancellationToken);
```

Duplicate suffixes are ignored. The collections cannot be null; an empty collection does nothing (`GetItemsAsync` returns an empty dictionary). Suffixes cannot be null or blank, and `SetItemsAsync` values cannot be null.

In Redis these operations are executed in batches (`GetBatchSize`, `InsertBatchSize`, `RemoveBatchSize`, up to two batches in flight at a time) so very large collections do not block the server. See "Redis Cluster" for how this works with a cluster.

### Load several on a miss: `GetOrCreateItemsAsync`

`GetOrCreateItemsAsync` does the same for several items, and is the natural way to fill the cache in bulk: it reads what is cached, calls your factory **once** with only the suffixes that are missing (one database query instead of one per item), stores what the factory found in a single batch and returns everything together.

```csharp
int[] days = [1, 2, 3, 4, 5];

IReadOnlyDictionary<int, WeatherForecast?> forecasts = await weatherForecastCacheService.GetOrCreateItemsAsync(
    days,
    async (missingDays, ct) => await repository.LoadForecastsAsync(missingDays, ct),   // returns IReadOnlyDictionary<int, WeatherForecast?>
    cancellationToken: cancellationToken);

// The result has one entry per distinct suffix: null when the item exists nowhere
int[] notFound = [.. forecasts.Where(w => w.Value is null).Select(s => s.Key)];

if (notFound.Length != 0)
{
    throw new KeyNotFoundException($"No forecast for days: {string.Join(", ", notFound)}");
}
```

- With 100 suffixes of which 90 are cached, the factory receives the 10 missing ones. The factory returns the items it found keyed by suffix; a suffix it leaves out, or returns with a `null` value, is considered not found. Not-found items are not cached.
- **Check for missing items after the call, not inside the factory.** Throwing from the factory fails the whole batch: the items that were found are not cached and every waiting caller receives the exception.
- Concurrent callers share the loads per key, even when their suffixes only overlap: caller A asking for days 1–3 and caller B asking for days 3–5 load day 3 once. The factory may therefore receive fewer suffixes than were missing for the call, or not be called at all when everything is cached or already being loaded by someone else. Loads started by `GetOrCreateAsync` are shared in the same way.
- Cancellation, failures, the per-process scope and the advice about request-scoped objects are the same as for `GetOrCreateAsync`. A factory that returns `null` instead of a dictionary fails the load with an `InvalidOperationException`.
- Duplicate suffixes are ignored, and an empty collection returns an empty dictionary without calling the factory.
- If both layers are disabled for the type, the factory is called directly with every suffix.

### Multi-tenancy

`TenantId` can be set once in `AddCache` (the default tenant) or overridden on each call. A tenant ID is 1 to 50 characters of `A-Z a-z 0-9 . _ - :`. If no default tenant is set, every call must pass one.

```csharp
await weatherForecastCacheService.SetAsync(forecast, dayIndex, tenantId: "tenant-abc");
WeatherForecast? result = await weatherForecastCacheService.GetAsync(dayIndex, tenantId: "tenant-abc");
```

### Global items (shared by all tenants)

Some data is not tenant specific, such as the tenant list itself or system information. Set `Scope = CacheScopes.Global` on the type to share its items between all tenants:

```csharp
using Bacon.Caching.Models;

builder.Services.AddCache(o => { /* ... */ })
.AddItem<SystemInformation>(o =>
{
    o.CacheKey                 = "system_info_";
    o.Scope                    = CacheScopes.Global;
    o.MemoryCacheDuration      = TimeSpan.FromMinutes(5);
    o.DistributedCacheDuration = TimeSpan.FromMinutes(30);
});
```

| Scope | Cache key | Tenant ID |
|---|---|---|
| `Tenant` (default) | `{tenantId}_{CacheKey}{suffix}` | Required, per call or from `CacheServerOptions.TenantId` |
| `Global` | `\|{CacheKey}{suffix}` | Not used: do not pass one |

- A global type never needs a tenant, so `CacheServerOptions.TenantId` can be left unset. Passing a `tenantId` to a global type throws an `ArgumentException`, since it would be a mistake rather than something to ignore.
- The leading `|` cannot appear in a tenant key (tenant IDs do not allow it), so a global key can never collide with a tenant key. It also makes global keys easy to spot when browsing Redis.
- Removing or overwriting a global item affects every tenant.
- A type has a single scope. A type can only be registered once, so if the same class must be both global and per tenant, register a separate type for each use.

### Passing a tenant ID without a suffix

`tenantId` and a string suffix are both strings, so a positional string argument is **always treated as the suffix**. To pass only a tenant ID, name the argument:

```csharp
await cache.GetAsync("abc");                  // suffix "abc",         default tenant
await cache.GetAsync(tenantId: "abc");        // no suffix,           tenant "abc"
await cache.GetAsync("abc", "tenant-abc");    // suffix "abc",         tenant "tenant-abc"
await cache.GetAsync();                       // no suffix,           default tenant

await cache.SetAsync(item, tenantId: "abc");  // same rule for SetAsync and RemoveAsync

await cache.GetOrCreateAsync(factory, tenantId: "abc");   // and for GetOrCreateAsync(factory), no suffix
```

Be careful when the tenant comes from a variable that can be `null`: `cache.GetAsync(tenant)` binds to the *suffix* overload and throws `ArgumentNullException` for `null`. Always use the named form (`GetAsync(tenantId: tenant)`) or branch on the value.

This resolution is implemented with `[OverloadResolutionPriority(1)]` on the suffix overloads.

### Invalidation

Items are always removed by explicit key through `RemoveAsync` (no suffix), `RemoveAsync(suffix)` and `RemoveItemsAsync(suffixes)`; there is no prefix or "flush all" operation.

A removal made while a `GetOrCreateAsync` load is in flight does not stop that load from storing its result, so the item can be cached again with data read before the removal. It expires normally.

`RemoveItemsAsync` deletes Redis keys in batches of `RemoveBatchSize`, so a failure can leave some keys removed and others not. A failing batch does not stop the others: every batch is attempted, the failures are logged together, and the memory cache is still cleared for every key.

## Behavior

| Configuration | Reads | Writes / removals |
|---|---|---|
| Memory + distributed | Memory first, then distributed (the result is backfilled into memory) | Distributed first, then memory |
| Memory only (`DistributedCacheDuration = Zero`, the default) | Memory | Memory |
| Distributed only (`MemoryCacheDuration = Zero`) | Distributed | Distributed |
| Both durations `Zero` | Nothing is cached: reads return `null` | Do nothing |

- **Errors are logged, not thrown.** A failure in either layer (for example Redis being unreachable) is logged and the other layer is still used: a failed Redis write still stores in memory, and a failed Redis removal still clears memory. Invalid arguments and cancellation do throw (see below).
- **A Redis outage costs latency, not exceptions.** While Redis is down, a read that misses the memory cache has to reach Redis. At best the client already knows it is disconnected and the call fails instantly; at worst the outage has not been detected yet and the call waits for the Redis timeout (`SyncTimeout`, 5 seconds by default). Either way the failure is logged and the read returns `null`, so the outage shows up as a delay at worst, never as an exception. Memory hits are not affected. Be careful when lowering `SyncTimeout` to shorten that worst-case delay: a busy Redis that is simply slower than usual would then start timing out, and its calls would be logged as failures instead of succeeding.
- **Redis writes are awaited.** `SetAsync` / `SetItemsAsync` wait for Redis to confirm the write. Each key is written with its TTL in a single atomic command. `SetItemsAsync` attempts every batch even if one fails; the failures are logged together and the memory cache is still written.
- **Synchronous completion.** When no distributed I/O is needed (memory-only, a memory hit, or disabled caches) the returned `ValueTask` is already completed, so awaiting it neither allocates a task nor yields.
- **Cancellation.** The token is checked before an operation starts and between Redis batches; an operation already sent to Redis cannot be interrupted. A canceled token throws `OperationCanceledException`. For `GetOrCreateAsync` / `GetOrCreateItemsAsync` it only cancels the caller's wait; see "Load on a miss".
- **Loading on a miss.** Unlike cache errors, an exception thrown by your factory is not logged: it is thrown to every caller waiting on that load.

### Exceptions

| Situation | Exception |
|---|---|
| Null suffix, null item, null collection, null value in `SetItemsAsync` | `ArgumentNullException` |
| Null or blank suffix inside a collection | `ArgumentException` |
| No tenant ID passed and no default tenant configured (tenant types) | `ArgumentNullException` |
| Empty or invalid tenant ID | `ArgumentException` |
| Tenant ID passed to a `Global` type | `ArgumentException` |
| Canceled `CancellationToken` | `OperationCanceledException` |
| Null factory passed to `GetOrCreateAsync` / `GetOrCreateItemsAsync` | `ArgumentNullException` |
| Exception thrown by your factory | The same exception, to every caller waiting on that load |
| `GetOrCreateItemsAsync` factory returns `null` instead of a dictionary | `InvalidOperationException`, to every caller waiting on that load |

## Configuration reference

### `CacheServerOptions`

| Property | Description |
|---|---|
| `TenantId` | Default tenant ID, used when no tenant is passed to a call. 1 – 50 characters of `A-Z a-z 0-9 . _ - :`. Required unless every call passes one. Not used by `Global` types. |

### `CacheOptions<T>`

| Property | Description |
|---|---|
| `CacheKey` | Key prefix for this type (e.g. `"weather_"`). Required, cannot be empty or whitespace, maximum 50 characters. |
| `MemoryCacheDuration` | TTL for in-process memory. `TimeSpan.Zero` (default) disables memory caching. Cannot be negative. |
| `DistributedCacheDuration` | TTL in Redis. `TimeSpan.Zero` (default) disables distributed caching. Cannot be negative. |
| `Scope` | `CacheScopes.Tenant` (default): items are isolated per tenant. `CacheScopes.Global`: items are shared by all tenants and no tenant ID is used. Must be a defined value. |

### `RedisCacheOptions` (`Bacon.Caching.Redis`)

| Property | Default | Description |
|---|---|---|
| `Server` | — | Hostname or IP only, no port. Required. |
| `Port` | `6379` | Redis port. Cannot be 0. |
| `Password` | `null` | Redis password. |
| `UseSsl` | `false` | Enable TLS. |
| `ServerType` | `Standalone` | `Standalone` for a single server or a primary/replica pair, `Cluster` for a Redis Cluster. See "Redis Cluster". |
| `AdditionalEndpoints` | `null` | Extra seed nodes (`RedisEndpoint`: `Server` without port, optional `Port` that defaults to the main `Port`). Only with `ServerType = Cluster`. See "Redis Cluster". |
| `GetBatchSize` | `100` | Max keys per read command when getting several items (1 – 5 000). With `Cluster` a command only holds keys of the same hash slot, so it can hold fewer. At most twice this number of keys are in flight at a time. |
| `InsertBatchSize` | `100` | Max items per batch when setting several items (1 – 5 000). Each item is written with its own command; at most twice this number of items are in flight at a time. |
| `RemoveBatchSize` | `100` | Max keys per delete command when removing several items (1 – 5 000). With `Cluster` a command only holds keys of the same hash slot, so it can hold fewer. At most twice this number of keys are in flight at a time. |
| `SyncTimeout` | `5000` | Redis operation timeout in ms, applied to both synchronous and asynchronous operations. It is also the longest a call can be delayed while Redis is down. |
| `LoggerFactory` | `null` | Optional. Passed to the StackExchange.Redis multiplexer for connection-event logging, and used to log a `ServerType` mismatch at startup. |
| `HealthCheckOptions` | `null` | Optional. Registers an ASP.NET Core health check for the Redis connection. |

### `DistributedHealthCheckOptions` (`HealthCheckOptions` of the Redis options)

The values left unset get the Redis defaults:

| Property | Default | Description |
|---|---|---|
| `Name` | `"redis"` | Unique name of the health check. |
| `FailureStatus` | `Degraded` | Status reported when the check fails. |
| `Timeout` | `null` | Time before the check is considered timed out. |
| `Tags` | `["Cache"]` | Tags of the health check. |

## Serialization

Values are stored in Redis as JSON. The serializer intentionally bypasses both `[JsonIgnore]` and `[JsonPropertyName]` — every public readable property is stored using its CLR name as the JSON key. This means properties marked `[JsonIgnore]` for API responses are still fully round-tripped through the distributed cache.

```csharp
public class WeatherForecast
{
    [JsonPropertyName("temperature_celcius")]  // stored as "TemperatureC" in Redis
    public int TemperatureC { get; set; }

    [JsonIgnore]                               // still stored and restored from Redis
    public Guid RandomGuid { get; set; }
}
```

## License

Apache-2.0