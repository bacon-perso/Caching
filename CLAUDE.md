# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~Tests.Test1"

# Run the sample API
dotnet run --project Bacon.Caching.Samples
```

## Architecture

This is a .NET 10 dual-layer caching library. Memory cache is always checked first; on a miss it falls through to distributed (Redis). If Redis is unreachable the library degrades gracefully instead of throwing.

### DI registration (fluent builder)

Consumers call `AddCache(...)` then chain `.AddItem<T>(...)` once per cached type:

```csharp
services.AddCache(o => {
    o.TenantId = "...";
    o.DistributedCacheType = DistributedCacheTypes.redis;
    o.DistributedCacheOptions = new RedisCacheOptions { Server = "...", Port = 6379 };
})
.AddItem<MyClass>(o => {
    o.CacheKey = "prefix_";
    o.MemoryCacheDuration = TimeSpan.FromSeconds(60);
    o.DistributedCacheDuration = TimeSpan.FromSeconds(60);
});
```

`AddItem<T>` registers `CacheOptions<T>` via `IOptions<CacheOptions<T>>`, so each `ICacheService<T>` gets its own strongly-typed options. Setting either duration to `TimeSpan.Zero` disables that layer for that type.

### Service layering

```
ICacheService<T>  (public, open-generic singleton)
    └─ CacheService<T>          — orchestrates memory + distributed
        ├─ IMemoryCache          — Microsoft.Extensions.Caching.Memory
        └─ IDistributedCacheService<T>  (internal, optional)
               └─ RedisCacheService<T>  — StackExchange.Redis
                       └─ ICacheSerializerService
                              └─ CacheSerializerService  — System.Text.Json
```

### Cache key format

`{tenantId}_{CacheKey}{suffix}`

`tenantId` resolves from the per-call override → `CacheServerOptions.TenantId`. It is required and cannot be null/empty.

### Serialization pipeline (`CacheSerializerService`)

Two separate `JsonSerializerOptions` instances are built in the constructor:

- **Serialize** — uses `IgnoreJsonIgnoreAttributesModifier`: manually rebuilds all public readable properties, intentionally bypassing `[JsonIgnore]` so every field is written to Redis.
- **Deserialize** — uses `TOtoModifier`: rewrites each `JsonPropertyInfo.Name` back to the CLR member name (undoes any `[JsonPropertyName]` rename) so JSON keys match the actual property names.

`JsonResolverExtensions` in `Bacon.Caching/DataContracts/` contains these two modifiers.

### Memory cache flush

`FlushMemoryItems` / `FlushItemsAsync` iterate keys via reflection on the internal `_coherentState._StringEntriesCollection` of `MemoryCache`. This is tied to the internal implementation of `Microsoft.Extensions.Caching.Memory` and may break on runtime upgrades.

### Test project

`Bacon.Caching.Tests` uses NUnit 4. The test file is currently a placeholder.
