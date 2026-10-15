# Bacon.Caching

A .NET dual-layer cache library. Items are stored in both in-process memory and a distributed cache (Redis). On reads, memory is checked first; on a miss the distributed cache is queried and the result is backfilled into memory. If the distributed cache is unavailable the library degrades gracefully — errors are logged and reads return `null` instead of throwing.

## Installation

```bash
dotnet add package Bacon.Caching
```

## Setup

### Memory-only

```csharp
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
builder.Services.AddCache(o =>
{
    o.TenantId              = "my-app";
    o.DistributedCacheType  = DistributedCacheTypes.redis;
    o.DistributedCacheOptions = new RedisCacheOptions
    {
        Server   = "redis-host",
        Port     = 6379,
        Password = "secret",
        UseSsl   = false,
    };
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

The Redis connection is created when `AddCache` runs. If Redis is unreachable at startup the application still starts: the failure is logged (when a `LoggerFactory` is configured) and the Redis client keeps retrying in the background, which can make startup slightly longer.

Registration validates its input and throws on invalid values: a null `configureOptions` (`ArgumentNullException`), an invalid tenant ID, a missing or wrongly typed `DistributedCacheOptions`, a server containing a port, port `0`, batch sizes outside 1 – 5 000, an empty `CacheKey` or one longer than 50 characters, a type registered twice, and negative durations (`ArgumentException` / `ArgumentOutOfRangeException`).

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
        // Try the cache first
        WeatherForecast? cached = await weatherForecastCacheService.GetAsync(cancellationToken: cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        WeatherForecast forecast = FetchFromSource();

        // Store in the memory and the distributed cache
        await weatherForecastCacheService.SetAsync(forecast, cancellationToken: cancellationToken);

        return forecast;
    }
}
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
IDictionary<int, WeatherForecast?> results = await weatherForecastCacheService.GetItemsAsync(weatherDic.Keys, cancellationToken: cancellationToken);

// Remove several items at once
await weatherForecastCacheService.RemoveItemsAsync([1, 2, 3], cancellationToken: cancellationToken);
```

Duplicate suffixes are ignored. The collections cannot be null; an empty collection does nothing (`GetItemsAsync` returns an empty dictionary). Suffixes cannot be null or blank, and `SetItemsAsync` values cannot be null.

In Redis these operations are executed in batches (`GetBatchSize`, `InsertBatchSize`, `RemoveBatchSize`, up to two batches concurrently) so very large collections do not block the server.

### Multi-tenancy

`TenantId` can be set globally in `AddCache` or overridden on each call. A tenant ID is 1 to 50 characters of `A-Z a-z 0-9 . _ - :`. If no global tenant is set, every call must pass one.

```csharp
await weatherForecastCacheService.SetAsync(forecast, dayIndex, tenantId: "tenant-abc");
WeatherForecast? result = await weatherForecastCacheService.GetAsync(dayIndex, tenantId: "tenant-abc");
```

### Passing a tenant ID without a suffix

`tenantId` and a string suffix are both strings, so a positional string argument is **always treated as the suffix**. To pass only a tenant ID, name the argument:

```csharp
await cache.GetAsync("abc");                  // suffix "abc",         global tenant
await cache.GetAsync(tenantId: "abc");        // no suffix,           tenant "abc"
await cache.GetAsync("abc", "tenant-abc");    // suffix "abc",         tenant "tenant-abc"
await cache.GetAsync();                       // no suffix,           global tenant

await cache.SetAsync(item, tenantId: "abc");  // same rule for SetAsync and RemoveAsync
```

Be careful when the tenant comes from a variable that can be `null`: `cache.GetAsync(tenant)` binds to the *suffix* overload and throws `ArgumentNullException` for `null`. Always use the named form (`GetAsync(tenantId: tenant)`) or branch on the value.

This resolution is implemented with `[OverloadResolutionPriority(1)]` on the suffix overloads.

### Invalidation

Items are always removed by explicit key through `RemoveAsync` (no suffix), `RemoveAsync(suffix)` and `RemoveItemsAsync(suffixes)`; there is no prefix or "flush all" operation.

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
- **Cancellation.** The token is checked before an operation starts and between Redis batches; an operation already sent to Redis cannot be interrupted. A canceled token throws `OperationCanceledException`.

### Exceptions

| Situation | Exception |
|---|---|
| Null suffix, null item, null collection, null value in `SetItemsAsync` | `ArgumentNullException` |
| Null or blank suffix inside a collection | `ArgumentException` |
| No tenant ID passed and no global tenant configured | `ArgumentNullException` |
| Empty or invalid tenant ID | `ArgumentException` |
| Canceled `CancellationToken` | `OperationCanceledException` |

## Configuration reference

### `CacheServerOptions`

| Property | Description |
|---|---|
| `TenantId` | Global tenant ID, used when no tenant is passed to a call. 1 – 50 characters of `A-Z a-z 0-9 . _ - :`. Required unless every call passes one. |
| `DistributedCacheType` | `none` (default) or `redis`. |
| `DistributedCacheOptions` | `RedisCacheOptions` instance. Required when `DistributedCacheType` is `redis`. |
| `HealthCheckOptions` | Optional (`null` by default). Registers an ASP.NET Core health check for the Redis connection. |
| `LoggerFactory` | Optional. Passed to the StackExchange.Redis multiplexer for connection-event logging. |

### `CacheOptions<T>`

| Property | Description |
|---|---|
| `CacheKey` | Key prefix for this type (e.g. `"weather_"`). Required, cannot be empty or whitespace, maximum 50 characters. |
| `MemoryCacheDuration` | TTL for in-process memory. `TimeSpan.Zero` (default) disables memory caching. Cannot be negative. |
| `DistributedCacheDuration` | TTL in Redis. `TimeSpan.Zero` (default) disables distributed caching. Cannot be negative. |

### `RedisCacheOptions`

| Property | Default | Description |
|---|---|---|
| `Server` | — | Hostname or IP only, no port. Required. |
| `Port` | `6379` | Redis port. Cannot be 0. |
| `Password` | `null` | Redis password. |
| `UseSsl` | `false` | Enable TLS. |
| `GetBatchSize` | `100` | Max keys per read command when getting several items (1 – 5 000). |
| `InsertBatchSize` | `100` | Max items per batch when setting several items (1 – 5 000). |
| `RemoveBatchSize` | `100` | Max keys per delete command when removing several items (1 – 5 000). |
| `SyncTimeout` | `5000` | Redis operation timeout in ms, applied to both synchronous and asynchronous operations. It is also the longest a call can be delayed while Redis is down. |

### `DistributedHealthCheckOptions`

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
