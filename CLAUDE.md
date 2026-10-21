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
dotnet test --filter "FullyQualifiedName~ClusterCacheServiceTests"

# Run the sample API
dotnet run --project Bacon.Caching.Samples
```

## Code style

Always use braces (even for single statements) and explicit types instead of `var`. Files use CRLF line endings in the working tree (`core.autocrlf=true`), and source files are UTF-8 with BOM. A new file must match: UTF-8 with BOM and CRLF (tools such as Claude Code's Write tool create plain UTF-8 with LF, so add the BOM and convert the line endings afterwards, and re-check a BOM/CRLF file after every edit); keep a file's existing encoding and line endings. Files end at their last closing brace, with no trailing newline or empty line.

## Architecture

This is a .NET 10 dual-layer caching library, split in two packages:

- **`Bacon.Caching`**: the memory cache, `ICacheService<T>`, tenants and scopes, and the contract distributed cache providers implement. It has no dependency on Redis.
- **`Bacon.Caching.Redis`**: the Redis provider (`AddRedis`). It references `Bacon.Caching`. Other providers can be written the same way (see "Writing a provider").

Memory cache is always checked first; on a miss it falls through to the distributed cache (when a provider is registered). If the distributed cache is unreachable the library degrades gracefully: errors from either layer are logged (source-generated `[LoggerMessage]`) and not thrown. Argument validation errors and `OperationCanceledException` are thrown.

The solution has five projects: `Bacon.Caching`, `Bacon.Caching.Redis`, `Bacon.Caching.Tests`, `Bacon.Caching.Redis.Tests` and `Bacon.Caching.Samples`. A root `Directory.Build.props` sets the shared compiler settings (target framework, nullable, implicit usings, language version, analysis level); `Version`, `GenerateDocumentationFile` and packaging are set per project.

### DI registration (fluent builder)

Consumers call `AddCache(...)`, then chain `AddRedis(...)` (optional) and `.AddItem<T>(...)` once per cached type:

```csharp
services.AddCache(o => {
    o.TenantId = "...";
})
.AddRedis(o => {
    o.Server = "...";
    o.Port = 6379;
    o.ServerType = RedisServerTypes.Standalone; // or Cluster
})
.AddItem<MyClass>(o => {
    o.CacheKey = "prefix_";
    o.MemoryCacheDuration = TimeSpan.FromSeconds(60);
    o.DistributedCacheDuration = TimeSpan.FromSeconds(60);
});
```

`AddItem<T>` registers `CacheOptions<T>` via `IOptions<CacheOptions<T>>`, so each `ICacheService<T>` gets its own strongly-typed options. Setting either duration to `TimeSpan.Zero` disables that layer for that type. `Scope` (`CacheScopes.Tenant` by default) can be set to `CacheScopes.Global` for data shared by all tenants (tenant data, system information): see "Cache key format". Without a provider the distributed layer is skipped (a type with a distributed duration but no provider logs "No distributed cache provider is registered" and keeps working with the memory cache).

All the registration methods take a `configureOptions` action and throw `ArgumentNullException` for a null argument. Options are validated at registration:

- `AddCache` (`CacheExtensions`): tenant ID (if set): 1-50 characters of `A-Z a-z 0-9 . _ - :`.
- `AddItem` (`CacheExtensions`): `CacheKey` not empty/whitespace, at most 50 characters. A type can only be configured once. Durations cannot be negative. `Scope` must be a defined `CacheScopes` value.
- `AddRedis` (`RedisCacheExtensions`): a distributed provider must not already be registered (`InvalidOperationException`, checked first, before the options action runs); server not empty/whitespace and without a port, port not 0, `ServerType` a defined `RedisServerTypes` value, `AdditionalEndpoints` (if not null/empty) only with `ServerType` `Cluster` and each endpoint non-null with a server not empty/whitespace and without a port, and a port not 0 (a null port uses the main `Port`), `GetBatchSize` / `InsertBatchSize` / `RemoveBatchSize` between 1 and 5000 (default 100).

`AddRedis` creates the Redis connection during registration (`AbortOnConnectFail = false`), so an unreachable Redis does not stop the application from starting. Right after connecting it compares the configured `ServerType` with the type the connected servers report (`CheckServerType`) and logs a warning in both mismatch cases: `Standalone` configured for a cluster (multi-key calls would fail) and `Cluster` configured for a standalone server (it works, with a small overhead). It only logs: it needs a `LoggerFactory` and a connected server, and never changes behavior. The multiplexer and its database are wrapped in an internal `RedisCacheConnection` registered with a factory (so the container disposes it), and are not registered on their own, so they cannot collide with the consumer's own `IDatabase` / `ConnectionMultiplexer`. The validated options instance is registered as `IOptions<RedisCacheOptions>`.

### Service layering

```
ICacheService<T>  (public, open-generic singleton)
    └─ CacheService<T>          — orchestrates memory + distributed
        ├─ IMemoryCache          — Microsoft.Extensions.Caching.Memory
        └─ IDistributedCacheService<T>  (public provider contract, optional: registered by a provider package)
               └─ BaseRedisCacheService<T>   — Bacon.Caching.Redis: shared logic over RedisCacheConnection
                       ├─ StandaloneCacheService<T>   — ServerType Standalone: MGET / DEL batches
                       └─ ClusterCacheService<T>      — ServerType Cluster: slot-grouped, pipelined waves
ICacheSerializerService (public, used by providers)
    └─ CacheSerializerService  — System.Text.Json
```

`AddRedis` registers the open generic `IDistributedCacheService<>` with `StandaloneCacheService<>` or `ClusterCacheService<>` according to `RedisCacheOptions.ServerType`. The user declares the topology: nothing is detected at runtime.

### Public API shape

`ICacheService<T>` is `ValueTask`-based with four families, each with a no-suffix and a suffix form: `GetAsync` / `GetAsync<TSuffix>` / `GetItemsAsync`, `GetOrCreateAsync` / `GetOrCreateAsync<TSuffix>` / `GetOrCreateItemsAsync` (see "Stampede protection"), `SetAsync` / `SetAsync<TSuffix>` / `SetItemsAsync`, `RemoveAsync` / `RemoveAsync<TSuffix>` / `RemoveItemsAsync`. Results of the plural reads are `IReadOnlyDictionary`; `SetItemsAsync` takes an `IReadOnlyDictionary`. There are no layer-specific (memory-only / distributed-only) methods.

- The no-suffix form targets a single item stored at `{tenantId}_{CacheKey}` (`|{CacheKey}` for global types), for example a configuration object.
- `tenantId` is optional on every method (falls back to `CacheServerOptions.TenantId`); `cancellationToken` is last. For `CacheScopes.Global` types it must be null: passing one throws `ArgumentException`.
- **Overload resolution:** `GetAsync(string? tenantId)` and `GetAsync<TSuffix>(TSuffix suffix, string? tenantId)` are ambiguous for a string argument. The suffix overloads are marked `[OverloadResolutionPriority(1)]`, so a positional string is a *suffix*; pass the tenant by name (`GetAsync(tenantId: "abc")`). The attribute is not inherited, so it must be repeated on both `ICacheService<T>` and `CacheService<T>` (and only matters for calls through the concrete type when forgotten, where `sut.GetAsync("x")` becomes ambiguous).
- The suffix is converted with `ToString()`, so it must be deterministic. Null suffixes/items throw `ArgumentNullException`. Plural methods de-duplicate suffixes, return immediately for empty collections (nothing is read, written or removed; `GetItemsAsync` returns an empty dictionary) and reject null/blank suffixes (`ArgumentException`).
- `IDistributedCacheService<T>` (public, `Task`-based, string keys) mirrors this with `GetAsync`, `GetItemsAsync`, `SetAsync`, `SetItemsAsync`, `RemoveAsync`, `RemoveItemsAsync`.

### Writing a provider

A distributed provider lives in its own package that references `Bacon.Caching`, and exposes an `AddXxx(this CacheBuilder, Action<XxxOptions>)` extension. It must:

- Implement the open generic `IDistributedCacheService<TItem>` (the keys it receives are the final cache keys) and register it with `cacheBuilder.Services.AddSingleton(typeof(IDistributedCacheService<>), typeof(XxxService<>))`.
- Check first that no `IDistributedCacheService<>` is registered yet and throw `InvalidOperationException` otherwise (the check is inline in each provider; there is deliberately no shared helper). A closed registration for one type is not a conflict: DI resolves it before the open generic.
- Use `ICacheSerializerService` if it stores text. Its options class can implement `IDistributedCacheOptions` (`Server`, `Password`, `LoggerFactory`, `HealthCheckOptions`), and `DistributedHealthCheckOptions` is shared; the provider supplies the defaults (Redis: name `redis`, status `Degraded`, tags `Cache`).
- Validate its options eagerly, before connecting or registering anything, and throw the usual argument exceptions.

### Layer ordering and error handling

Reads check memory, then distributed, and backfill memory on a distributed hit (also when only the distributed layer returns values: memory disabled means the values are returned without being written to memory). Writes and removals do the distributed layer first, then memory, each in its **own** `try/catch`, so a Redis failure never skips the memory operation (a failed removal must not leave stale memory entries). `ValidateDistributedCache()` ("No distributed cache provider is registered...") runs inside the distributed `try`, so a missing service is logged, not thrown. Cache keys are built **before** either `try` (`SetItemsAsync` builds one key/item dictionary shared by both layers), so argument errors are thrown instead of logged: an invalid or missing tenant ID, a tenant ID passed to a global type, or two suffixes with the same string representation.

Redis outage behavior: at startup the failure is logged (if a `LoggerFactory` is configured) and the client's retries can slow startup slightly. At runtime a read that misses memory still has to reach Redis: at best `BaseRedisCacheService.ValidateIsConnected` fails it instantly, at worst (outage not yet detected) it waits for the Redis timeout (`RedisCacheOptions.SyncTimeout`, applied to sync and async operations) before failing, being logged and returning null. The cost of an outage is latency, not exceptions.

### Cache key format

`{tenantId}_{CacheKey}{suffix}` for `CacheScopes.Tenant` (the default), `|{CacheKey}{suffix}` for `CacheScopes.Global`.

`tenantId` resolves from the per-call override → `CacheServerOptions.TenantId`. It is required: null throws `ArgumentNullException`, empty or an invalid format throws `ArgumentException`. Global types never use a tenant (neither the per-call value nor `CacheServerOptions.TenantId`, which can be unset). The leading `|` cannot occur in a tenant key (tenant IDs only allow `A-Z a-z 0-9 . _ - :`), so global and tenant keys never collide; the only protection between two types sharing a `CacheKey` remains the naming of `CacheKey`.

### Serialization pipeline (`CacheSerializerService`)

One `JsonSerializerOptions` instance is built in the constructor, with `IgnoreJsonIgnoreAttributesModifier` (in `Bacon.Caching/DataContracts/JsonResolverExtensions.cs`): it manually rebuilds all public readable properties using their CLR names, intentionally bypassing both `[JsonIgnore]` and `[JsonPropertyName]` so every field is written to Redis and read back. The same options are used to serialize and deserialize.

### Redis batching

The services differ only in how several keys are handled (`GetItemsAsync`, `RemoveItemsAsync`); everything else is in `BaseRedisCacheService`. `MaxParallelBatches` (2) is the shared concurrency constant.

**Standalone** (`StandaloneCacheService`): splits the work into batches and processes up to `MaxParallelBatches` of them concurrently with `Parallel.ForEachAsync` (honoring the cancellation token between batches):

- `GetItemsAsync` reads in `MGET` batches of `GetBatchSize`; each batch writes into its own index range of a pre-sized array (lock-free), then the dictionary is built.
- `RemoveItemsAsync` uses `RemoveBatchSize` (`DEL` per batch). Batched removal is not atomic.

**Cluster** (`ClusterCacheService`): a Redis Cluster rejects (`CROSSSLOT`) a command whose keys are not all in the same hash slot, even on the same node, and our keys differ by suffix. So the keys are grouped by `ConnectionMultiplexer.HashSlot` (`CreateBatches`, which keeps each key's original index) and chunked by batch size, which with random suffixes often gives one-key batches. The batches are then sent in waves (`CreateWaves`): a wave holds at most `batch size * MaxParallelBatches` keys, all its commands are started before any reply is awaited (pipelined), and the next wave starts once the whole wave is done. That keeps the keys in flight bounded by the batch size option (the same bound as standalone) while avoiding one round trip per tiny batch. `GetBatchAsync` fills `values[batch[idx].Index]`, so it stays lock-free and keeps the original key order. Hash tags in the key are deliberately not used (they would change the key format and put a tenant on one shard).

**Both:**

- `SetItemsAsync` (in the base class) uses `InsertBatchSize`; each chunk is serialized first, then every key is written with `SET key value EX` (atomic with its TTL, pipelined, awaited: no `FireAndForget`, so Redis-side errors surface). Single-key commands are valid in a cluster, so it needs no split. Do not use `MSET` + `EXPIRE` (not atomic: a failure in between leaves keys without a TTL).
- **Failures are per batch** (set and remove): each batch has its own `try/catch` (with its own `ValidateIsConnected`), so a failing batch never stops the others. Failures are collected in a `ConcurrentQueue<Exception>` and thrown once at the end as an `AggregateException` ("N of M batches failed", `ThrowIfAnyFailed`); `CacheService` logs it and still runs the memory operation. `OperationCanceledException` is not collected. The `ValidateIsConnected` before the loop throws a single exception instead.
- `ServerType` is declared in `RedisCacheOptions`. `Cluster` on a standalone server works (`HashSlot` returns `-1`, so there is a single group) with a small overhead (the grouping and the pipelined waves instead of the plain `MGET` / `DEL` loop), while `Standalone` on a cluster fails the multi-key calls (logged, results missing): hence the startup diagnostic, which warns in both directions. The cluster path is covered by unit tests of the batching helpers but has not been run against a live cluster yet. `Server` / `Port` is the main seed node; `RedisCacheOptions.AdditionalEndpoints` (`RedisEndpoint`: `Server`, optional `Port` defaulting to the main `Port`) adds more seed nodes, only with `ServerType` `Cluster`, so the connection can start when the main node is down (the rest of the cluster is discovered from any reachable seed).

### No flush operation

There is intentionally no prefix/"flush all" operation (it was removed along with the Lua `SCAN` script, the `AllowAdmin` Redis option and the reflection into `MemoryCache` internals). Items are removed by explicit key via `RemoveAsync` / `RemoveItemsAsync`.

### Stampede protection (`GetOrCreateAsync`)

`GetOrCreateAsync` (one item) and `GetOrCreateItemsAsync` (several) read like `GetAsync` / `GetItemsAsync` and, on a miss, load the missing items with a caller-supplied factory, store them and return them. Concurrent misses for the same key share one load. `GetAsync` stays a pure lookup: it returns null on a miss and never loads (it only copies a distributed-only hit into memory).

- **Structure:** `CacheService<T>` keeps `_inFlight`, a `ConcurrentDictionary<string, InFlightLoad<T>>` keyed by the final cache key. `InFlightLoad<T>` (in `Models`) holds the key's `TaskCompletionSource` and its `LoadScope` (also in `Models`), the cancellation shared by everyone waiting on one load (one key, or the batch of keys one caller claimed). The public methods validate and build the keys once, then call private cores that take prebuilt keys and the caller's `source` (`GetSingleItemAsync`, `GetManyItemsAsync`, `SetSingleItemAsync`, `SetManyItemsAsync`), so the GetOrCreate paths never validate or log twice.
- **Flow (`LoadMissingItemsAsync`):** the caller reads through the normal read core (memory, then distributed, with the memory backfill): a hit never touches the map. For each missing key it does a `GetOrAdd` of a new `InFlightLoad`; the winner owns the key, the others `TryJoin` the winner's scope and wait. The keys a caller owns are loaded in one batch by `LoadItemsAsync`, started without awaiting it: it never throws and completes each key's `TaskCompletionSource`. The caller then waits for all its keys with `WaitAsync(its own token)`. A caller can own some keys and wait for others without deadlock, because every load is started before any wait.
- **`LoadItemsAsync`:** re-checks memory only (a load that finished between the miss and the claim; with memory disabled that gap remains and a duplicate load is possible), calls the factory once with the suffixes still missing, stores the items found (`SetSingleItemAsync` for one, `SetManyItemsAsync` for several, so a single item does not use the batching path), removes its entries from the map **before** completing them (nobody joins a finished or failed load), then completes them. A factory that returns null fails the batch with `InvalidOperationException`.
- **Cancellation:** the factory receives `LoadScope.Token`, canceled when the last waiting caller leaves before the load finishes (`Leave`). A caller's own token only cancels its own wait. `TryJoin` refuses a scope whose waiter count reached 0 (abandoned): the joiner removes the dead entry and claims the key again, and the old load only removes its own entry (`TryRemove` of the exact `KeyValuePair`). `LoadScope` is `IDisposable`: the load disposes it when it ends (a scope nobody claimed is disposed right away), under the same lock `Leave` uses to cancel it (`Cancel` on a disposed source throws, and `Dispose` is safe to call twice); cancellation callbacks therefore run under that lock. The store step uses `CancellationToken.None`: the work is done, so the result is cached even if every caller left.
- **Failures and nulls:** a factory exception is thrown to every caller waiting on those keys (not logged; the task exception is marked observed), nothing is cached, and the next call retries. The factory only loads what it receives and returns what it found; validation and throwing happen outside the cache, in the caller. "Not found" is a null in the result in both forms (it means the item is in neither layer nor in the source) and is not cached, so repeated requests for a missing key reach the source every time. The single factory is `Func<CancellationToken, ValueTask<TItem?>>`: a null return is "no data" (returned as null, nothing cached; internally it becomes an empty dictionary for the shared load path). The plural factory returns `IReadOnlyDictionary<TSuffix, TItem>` with non-null values: a suffix missing from it is not found and comes back as null in the result, while the items found are cached, and the result always has one entry per requested suffix. So check the result for null after the call instead of throwing in the factory (a throw fails the whole batch and nothing is cached, and the exception also reaches other callers that joined one of its keys).
- **Limits:** protection is per process: with N nodes a hot key can load up to N times per expiry (`HybridCache` has the same limit). A `RemoveAsync` made during a load does not stop that load from storing its (older) result. With both layers disabled the factory is called directly. There is no negative caching, no load timeout and no cross-node lock (a Redis `SET NX PX` lock was considered and left out: new failure mode when Redis is down, and extra round trips).
- **Tests:** `CacheServiceGetOrCreateTests` covers both methods: concurrent misses call the factory once, hits never call it, a factory failure reaches every waiter and the next call retries, one caller cancelling does not cancel the others while the last one leaving cancels the factory, a joiner arriving at an abandoned load starts a new one, the result is stored even when every caller left, overlapping and identical batches, sharing between a single and a plural load, argument validation, both layers disabled, a failing distributed cache, and the synchronous memory-hit path. It uses gated factories (`GatedFactory`, `GatedBatchFactory`) built on `TaskCompletionSource`, and waits with a 5 second timeout so a bug fails the test instead of hanging it. A gated factory records its cancellation both in a token callback and in a `catch`: with `WaitAsync(token)` the wait can finish and dispose the callback before it runs.
- Combining with `HybridCache` was considered and rejected: four layers, double serialization and no batch / tenant support.

### Test projects

Both use NUnit 4 (`Assert.ThrowsAsync` must be awaited; `ValueTask` results need `.AsTask()` for it).

**`Bacon.Caching.Tests`**

- `CacheServiceTests` runs `CacheService<T>` against a real `MemoryCache` and `Fakes/FakeDistributedCacheService<T>`. The fake exposes `Seed`, `Clear`, `Contains`, `RequestedKeys` (keys asked of `GetItemsAsync`) and `FailWith` (makes every operation throw, to test graceful degradation). Tests call the concrete class positionally and through `ICacheService<T>` to cover the overload-resolution rule above. The three modes are covered separately (memory only, distributed only, mixed), plus both layers disabled and the global scope (key shape, no tenant required, no collision with tenant keys, tenant ID rejected); the "Synchronous completion" tests assert `IsCompletedSuccessfully` on the returned `ValueTask` before awaiting it, which protects the allocation-free memory-only/memory-hit path.
- `CacheServiceGetOrCreateTests` covers `GetOrCreateAsync` / `GetOrCreateItemsAsync` against the same real `MemoryCache` and `FakeDistributedCacheService<T>` (see "Stampede protection").
- `CacheExtensionsTests` covers the `AddCache` / `AddItem` registration validation.

**`Bacon.Caching.Redis.Tests`** (sees the internals of `Bacon.Caching.Redis` through `InternalsVisibleTo`)

- `RedisCacheExtensionsTests` covers `AddRedis` validation. It must not use valid Redis options (that would try to connect), so the provider-already-registered check, the null arguments and every invalid option are tested, but not a successful registration.
- `ClusterCacheServiceTests` covers the pure `CreateBatches` / `CreateWaves` helpers (no mixed slots, every key once with its index, wave sizes).
- `RedisCacheOptionsTests` checks the defaults (`AdditionalEndpoints` is null).
- The Redis commands themselves (`MGET`, `DEL`, `SET EX`, the batches and waves against a server) have no unit tests: the services take a concrete `ConnectionMultiplexer` and `IDatabase`, so they need a real Redis (standalone or cluster) or an abstraction seam first.