# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test class / test
dotnet test --filter "FullyQualifiedName~CacheServiceTests"
dotnet test --filter "FullyQualifiedName~CacheServiceTests.GetItemsAsync_MemoryDisabled_ReturnsDistributedValues"

# Run the sample API
dotnet run --project Bacon.Caching.Samples
```

## Code style

Always use braces (even for single statements) and explicit types instead of `var`. Files use CRLF line endings in the working tree (`core.autocrlf=true`), except the test files, `CLAUDE.md` and files created by tools, which are LF; keep a file's existing line endings.

## Architecture

This is a .NET 10 dual-layer caching library. Memory cache is always checked first; on a miss it falls through to distributed (Redis). If Redis is unreachable the library degrades gracefully: errors from either layer are logged (source-generated `[LoggerMessage]`) and not thrown. Argument validation errors and `OperationCanceledException` are thrown.

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

Both methods take a `configureOptions` action and throw `ArgumentNullException` for a null argument. Options are validated at registration (`CacheExtensions`):

- Tenant ID (if set): 1-50 characters of `A-Z a-z 0-9 . _ - :`.
- `CacheKey`: not empty/whitespace, at most 50 characters. A type can only be configured once. Durations cannot be negative.
- Redis: server without a port, port not 0, `GetBatchSize` / `InsertBatchSize` / `RemoveBatchSize` between 1 and 5000 (default 100).

`AddCache` creates the Redis connection during registration (`AbortOnConnectFail = false`).

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

### Public API shape

`ICacheService<T>` is `ValueTask`-based with three families, each with a no-suffix and a suffix form: `GetAsync` / `GetAsync<TSuffix>` / `GetItemsAsync`, `SetAsync` / `SetAsync<TSuffix>` / `SetItemsAsync`, `RemoveAsync` / `RemoveAsync<TSuffix>` / `RemoveItemsAsync`. There are no layer-specific (memory-only / distributed-only) methods.

- The no-suffix form targets a single item stored at `{tenantId}_{CacheKey}` (for example a configuration object).
- `tenantId` is optional on every method (falls back to `CacheServerOptions.TenantId`); `cancellationToken` is last.
- **Overload resolution:** `GetAsync(string? tenantId)` and `GetAsync<TSuffix>(TSuffix suffix, string? tenantId)` are ambiguous for a string argument. The suffix overloads are marked `[OverloadResolutionPriority(1)]`, so a positional string is a *suffix*; pass the tenant by name (`GetAsync(tenantId: "abc")`). The attribute is not inherited, so it must be repeated on both `ICacheService<T>` and `CacheService<T>` (and only matters for calls through the concrete type when forgotten, where `sut.GetAsync("x")` becomes ambiguous).
- The suffix is converted with `ToString()`, so it must be deterministic. Null suffixes/items throw `ArgumentNullException`. Plural methods de-duplicate suffixes and reject empty collections and null/blank suffixes (`ArgumentException`).
- `IDistributedCacheService<T>` (internal, `Task`-based, string keys) mirrors this with `GetAsync`, `GetItemsAsync`, `SetAsync`, `SetItemsAsync`, `RemoveAsync`, `RemoveItemsAsync`.

### Layer ordering and error handling

Reads check memory, then distributed, and backfill memory on a distributed hit (also when only the distributed layer returns values: memory disabled means the values are returned without being written to memory). Writes and removals do the distributed layer first, then memory, each in its **own** `try/catch`, so a Redis failure never skips the memory operation (a failed removal must not leave stale memory entries). `ValidateDistributedCache()` ("Distributed cache not initialized") runs inside the distributed `try`, so a missing service is logged, not thrown.

Redis outage behavior: at startup the failure is logged (if a `LoggerFactory` is configured) and the client's retries can slow startup slightly. At runtime a read that misses memory still has to reach Redis: at best `RedisCacheService.ValidateIsConnected` fails it instantly, at worst (outage not yet detected) it waits for the Redis timeout (`RedisCacheOptions.SyncTimeout`, applied to sync and async operations) before failing, being logged and returning null. The cost of an outage is latency, not exceptions.

### Cache key format

`{tenantId}_{CacheKey}{suffix}`

`tenantId` resolves from the per-call override → `CacheServerOptions.TenantId`. It is required: null throws `ArgumentNullException`, empty or an invalid format throws `ArgumentException`.

### Serialization pipeline (`CacheSerializerService`)

Two separate `JsonSerializerOptions` instances are built in the constructor:

- **Serialize** — uses `IgnoreJsonIgnoreAttributesModifier`: manually rebuilds all public readable properties, intentionally bypassing `[JsonIgnore]` so every field is written to Redis.
- **Deserialize** — uses `TOtoModifier`: rewrites each `JsonPropertyInfo.Name` back to the CLR member name (undoes any `[JsonPropertyName]` rename) so JSON keys match the actual property names.

`JsonResolverExtensions` in `Bacon.Caching/DataContracts/` contains these two modifiers.

### Redis batching

`RedisCacheService` splits large operations into batches and processes up to 2 batches concurrently with `Parallel.ForEachAsync` (honoring the cancellation token between batches):

- `GetItemsAsync` reads in `MGET` batches of `RedisCacheOptions.GetBatchSize`; each batch writes into its own index range of a pre-sized array (lock-free), then the dictionary is built.
- `SetItemsAsync` uses `InsertBatchSize`; each key is written with `SET key value EX` (atomic with its TTL, pipelined, `FireAndForget`). Do not use `MSET` + `EXPIRE` (not atomic: a failure in between leaves keys without a TTL).
- `RemoveItemsAsync` uses `RemoveBatchSize` (`DEL` per batch). Batched removal is not atomic: if a batch fails, earlier batches stay deleted; the error is logged and the memory cache is still cleared.

### No flush operation

There is intentionally no prefix/"flush all" operation (it was removed along with the Lua `SCAN` script, the `AllowAdmin` Redis option and the reflection into `MemoryCache` internals). Items are removed by explicit key via `RemoveAsync` / `RemoveItemsAsync`.

### Test project

`Bacon.Caching.Tests` uses NUnit 4 (`Assert.ThrowsAsync` must be awaited; `ValueTask` results need `.AsTask()` for it).

- `CacheServiceTests` runs `CacheService<T>` against a real `MemoryCache` and `Fakes/FakeDistributedCacheService<T>`. The fake exposes `Seed`, `Clear`, `Contains`, `RequestedKeys` (keys asked of `GetItemsAsync`) and `FailWith` (makes every operation throw, to test graceful degradation). Tests call the concrete class positionally and through `ICacheService<T>` to cover the overload-resolution rule above. The three modes are covered separately (memory only, distributed only, mixed), plus both layers disabled; the "Synchronous completion" tests assert `IsCompletedSuccessfully` on the returned `ValueTask` before awaiting it, which protects the allocation-free memory-only/memory-hit path.
- `CacheExtensionsTests` covers registration validation. It must not use valid Redis options (that would try to connect).
- `RedisCacheService` (batching, `SET EX`) has no unit tests: it takes a concrete `ConnectionMultiplexer` and `IDatabase`, so it needs a real Redis or an abstraction seam first.
