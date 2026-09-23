# Bacon.Caching

A .NET dual-layer cache library. Items are stored in both in-process memory and a distributed cache (Redis). On reads, memory is checked first; on a miss the distributed cache is queried and the result is backfilled into memory. If the distributed cache is unavailable the library degrades gracefully — errors are logged and `null` is returned instead of throwing.

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

Call `.AddItem<T>()` once per type you want to cache. Each type gets its own key prefix and TTL settings.

## Usage

### Inject `ICacheService<T>`

```csharp
[ApiController]
[Route("api/weather")]
public class WeatherForecastController(ICacheService<WeatherForecast> cache) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<WeatherForecast>> GetAsync()
    {
        // Try the cache first
        WeatherForecast? cached = await cache.GetItemAsync();
        if (cached is not null)
            return [cached];

        WeatherForecast forecast = FetchFromSource();

        // Store in both memory and distributed cache
        await cache.SetItemAsync(forecast);

        return [forecast];
    }
}
```

### Suffix keys — cache multiple items of the same type

Pass any non-null value as a suffix to distinguish individual items. The final Redis key is `{tenantId}_{CacheKey}{suffix}`.

```csharp
// Store a forecast keyed by day index
await cache.SetItemAsync(forecast, dayIndex);          // key: "my-app_weather_1"

// Retrieve it later
WeatherForecast? result = await cache.GetItemAsync(dayIndex);
```

### Batch operations

```csharp
// Store several items at once (key = suffix, value = item)
Dictionary<int, WeatherForecast> forecasts = new()
{
    [1] = forecastDay1,
    [2] = forecastDay2,
    [3] = forecastDay3,
};

await cache.SetItemsAsync(forecasts);

// Retrieve all of them in one call
IDictionary<int, WeatherForecast?> results = await cache.GetItemsAsync([1, 2, 3]);
```

### Target a specific layer

```csharp
// Memory only (synchronous, no distributed overhead)
cache.SetMemoryItem(forecast, dayIndex);
WeatherForecast? memResult = cache.GetMemoryItem(dayIndex);

// Distributed only
await cache.SetDistributedItemAsync(forecast, dayIndex);
WeatherForecast? distResult = await cache.GetDistributedItemAsync(dayIndex);
```

### Invalidation

```csharp
// Remove a single item from both layers
await cache.RemoveItemAsync(dayIndex);

// Remove several items at once
await cache.RemoveItemsAsync([1, 2, 3]);

// Remove every key that matches this type's prefix
await cache.FlushItemsAsync();
```

### Multi-tenancy

`TenantId` can be set globally in `AddCache` or overridden per call:

```csharp
await cache.SetItemAsync(forecast, dayIndex, tenantId: "tenant-abc");
WeatherForecast? result = await cache.GetItemAsync(dayIndex, tenantId: "tenant-abc");
```

## Configuration reference

### `CacheServerOptions`

| Property | Description |
|---|---|
| `TenantId` | Global tenant prefix. Required unless overridden on every call. |
| `DistributedCacheType` | `none` (default) or `redis`. |
| `DistributedCacheOptions` | `RedisCacheOptions` instance when using Redis. |
| `HealthCheckOptions` | Optional. Registers an ASP.NET Core health check for the Redis connection. |
| `LoggerFactory` | Optional. Passed to the StackExchange.Redis multiplexer for connection-event logging. |

### `CacheOptions<T>`

| Property | Description |
|---|---|
| `CacheKey` | Key prefix for this type (e.g. `"weather_"`). Required, cannot be empty. |
| `MemoryCacheDuration` | TTL for in-process memory. Set to `TimeSpan.Zero` to disable memory caching. |
| `DistributedCacheDuration` | TTL in Redis. Set to `TimeSpan.Zero` to disable distributed caching. |

### `RedisCacheOptions`

| Property | Default | Description |
|---|---|---|
| `Server` | — | Hostname only, no port. Required. |
| `Port` | `6379` | Redis port. |
| `Password` | `null` | Redis password. |
| `UseSsl` | `false` | Enable TLS. |
| `InsertBatchSize` | `100` | Max keys per batch write (≤ 5 000). |
| `FlushBatchSize` | `1000` | Keys scanned per SCAN iteration during flush (≤ 8 000). |
| `SyncTimeout` | `5000` | Synchronous operation timeout in ms. |

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

MIT
