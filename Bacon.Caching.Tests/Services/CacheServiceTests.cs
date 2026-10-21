using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Services;
using Bacon.Caching.Tests.Fakes;
using Bacon.Caching.Tests.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bacon.Caching.Tests.Services;

[TestFixture]
internal sealed class CacheServiceTests
{
    private const string TenantId = "test-tenant";
    private const string CacheKey = "test_";

    private FakeDistributedCacheService<TestItem> _fakeDistCache = null!;
    private MemoryCache _memoryCache = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeDistCache = new FakeDistributedCacheService<TestItem>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    [TearDown]
    public void TearDown() => _memoryCache.Dispose();

    private CacheService<TestItem> CreateService(string? tenantId = TenantId, TimeSpan? memDuration = null, TimeSpan? distDuration = null, bool includeDistributedCache = true, CacheScopes scope = CacheScopes.Tenant)
    {
        IOptions<CacheServerOptions> serverOptions = Options.Create(new CacheServerOptions { TenantId = tenantId });
        IOptions<CacheOptions<TestItem>> cacheOptions = Options.Create(new CacheOptions<TestItem>
        {
            CacheKey = CacheKey,
            MemoryCacheDuration = memDuration ?? TimeSpan.FromMinutes(5),
            DistributedCacheDuration = distDuration ?? TimeSpan.FromMinutes(10),
            Scope = scope,
        });

        return new CacheService<TestItem>(serverOptions, cacheOptions, _memoryCache, NullLogger<TestItem>.Instance, includeDistributedCache ? _fakeDistCache : null);
    }

    private static string Key(string suffix = "", string tenantId = TenantId) => $"{tenantId}_{CacheKey}{suffix}";

    private static string GlobalKey(string suffix = "") => $"|{CacheKey}{suffix}";

    private sealed class FixedSuffix(string value)
    {
        public override string ToString() => value;
    }

    private CacheService<TestItem> CreateMemoryOnlyService() => CreateService(distDuration: TimeSpan.Zero, includeDistributedCache: false);

    private CacheService<TestItem> CreateDistributedOnlyService() => CreateService(memDuration: TimeSpan.Zero);

    #region Get / Set

    [Test]
    public async Task SetAsync_GetAsync_WithoutSuffix_ReturnsItem()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice", Value = 1 });

        TestItem? result = await sut.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Name, Is.EqualTo("Alice"));
            Assert.That(_fakeDistCache.Contains(Key()), Is.True);
        };
    }

    [Test]
    public async Task SetAsync_GetAsync_WithSuffix_IsolatesByKey()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");
        await sut.SetAsync(new TestItem { Name = "Bob" }, "id2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice"));
            Assert.That((await sut.GetAsync("id2"))!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task SetAsync_PositionalStringArgument_IsTreatedAsSuffix_OnConcreteClass()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True);
            Assert.That(_fakeDistCache.Contains(Key(tenantId: "id1")), Is.False);
        };
    }

    [Test]
    public async Task PositionalStringArgument_IsTreatedAsSuffix_OnInterface()
    {
#pragma warning disable CA1859 // The interface type is deliberate: this test covers the overload resolution through ICacheService<T>
        ICacheService<TestItem> sut = CreateService();
#pragma warning restore CA1859

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        TestItem? result = await sut.GetAsync("id1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True);
            Assert.That(result!.Name, Is.EqualTo("Alice"));
        };

        await sut.RemoveAsync("id1");

        Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
    }

    [Test]
    public async Task NamedTenantIdArgument_IsTreatedAsTenant_OnInterface()
    {
#pragma warning disable CA1859 // The interface type is deliberate: this test covers the overload resolution through ICacheService<T>
        ICacheService<TestItem> sut = CreateService();
#pragma warning restore CA1859

        await sut.SetAsync(new TestItem { Name = "Alice" }, tenantId: "tenant-a");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key(tenantId: "tenant-a")), Is.True);
            Assert.That((await sut.GetAsync(tenantId: "tenant-a"))!.Name, Is.EqualTo("Alice"));
            Assert.That(await sut.GetAsync(), Is.Null);
        };

        await sut.RemoveAsync(tenantId: "tenant-a");

        Assert.That(_fakeDistCache.Contains(Key(tenantId: "tenant-a")), Is.False);
    }

    [Test]
    public async Task SetAsync_GetAsync_WithIntSuffix_UsesToStringInKey()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, 42);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("42")), Is.True);
            Assert.That((await sut.GetAsync(42))!.Name, Is.EqualTo("Alice"));
        };
    }

    [Test]
    public async Task SetAsync_GetAsync_WithGuidSuffix_UsesToStringInKey()
    {
        CacheService<TestItem> sut = CreateService();
        Guid id = Guid.NewGuid();

        await sut.SetAsync(new TestItem { Name = "Alice" }, id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key(id.ToString())), Is.True);
            Assert.That((await sut.GetAsync(id))!.Name, Is.EqualTo("Alice"));
        };
    }

    [Test]
    public async Task GetAsync_WhenNotCached_ReturnsNull()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.That(await sut.GetAsync("missing"), Is.Null);
    }

    [Test]
    public async Task SetAsync_StoresInDistributedCache()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True);
    }

    #endregion

    #region Memory-first / fallthrough

    [Test]
    public async Task GetAsync_MemoryMiss_FallsThroughToDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.Seed(Key("myid"), new TestItem { Name = "FromRedis" });

        TestItem? result = await sut.GetAsync("myid");

        Assert.That(result!.Name, Is.EqualTo("FromRedis"));
    }

    [Test]
    public async Task GetAsync_DistributedHit_BackfillsMemoryCache()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.Seed(Key("myid"), new TestItem { Name = "FromRedis" });

        await sut.GetAsync("myid"); // populates memory cache

        _fakeDistCache.Clear(); // wipe distributed

        TestItem? result = await sut.GetAsync("myid"); // should hit memory

        Assert.That(result!.Name, Is.EqualTo("FromRedis"));
    }

    [Test]
    public async Task GetAsync_MemoryHit_DoesNotRequireDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "InMemory" }, "id1");

        _fakeDistCache.Clear(); // remove from distributed after set

        TestItem? result = await sut.GetAsync("id1");

        Assert.That(result!.Name, Is.EqualTo("InMemory"));
    }

    #endregion

    #region Batch operations

    [Test]
    public async Task SetItemsAsync_GetItemsAsync_ReturnsAllItems()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemsAsync(new Dictionary<string, TestItem>
        {
            ["id1"] = new TestItem { Name = "Alice" },
            ["id2"] = new TestItem { Name = "Bob" },
        });

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(result["id2"]!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task SetItemsAsync_StoresInBothLayers()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemsAsync(new Dictionary<string, TestItem>
        {
            ["id1"] = new TestItem { Name = "Alice" },
            ["id2"] = new TestItem { Name = "Bob" },
        });

        bool inDistributed = _fakeDistCache.Contains(Key("id1")) && _fakeDistCache.Contains(Key("id2"));

        _fakeDistCache.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inDistributed, Is.True);
            Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice")); // served from memory
            Assert.That((await sut.GetAsync("id2"))!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task GetItemsAsync_MixedHitsAndMisses_NullForMissing()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "missing"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(result["missing"], Is.Null);
        };
    }

    [Test]
    public async Task GetItemsAsync_MemoryMiss_FallsThroughToDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.Seed(Key("id1"), new TestItem { Name = "FromRedis" });

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1"]);

        Assert.That(result["id1"]!.Name, Is.EqualTo("FromRedis"));
    }

    [Test]
    public async Task GetItemsAsync_OnlyMemoryMissesAreRequestedFromDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "InMemory" }, "id1");

        _fakeDistCache.Clear();
        _fakeDistCache.Seed(Key("id2"), new TestItem { Name = "FromRedis" });
        _fakeDistCache.RequestedKeys.Clear();

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.RequestedKeys, Is.EqualTo(new[] { Key("id2") }));
            Assert.That(result["id1"]!.Name, Is.EqualTo("InMemory"));
            Assert.That(result["id2"]!.Name, Is.EqualTo("FromRedis"));
        };
    }

    [Test]
    public async Task GetItemsAsync_DistributedHit_BackfillsMemoryCache()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.Seed(Key("id1"), new TestItem { Name = "FromRedis" });

        await sut.GetItemsAsync(["id1"]); // populates memory cache

        _fakeDistCache.Clear();

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1"]);

        Assert.That(result["id1"]!.Name, Is.EqualTo("FromRedis"));
    }

    [Test]
    public async Task GetItemsAsync_MemoryDisabled_ReturnsDistributedValues()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero);

        _fakeDistCache.Seed(Key("id1"), new TestItem { Name = "FromRedis" });

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "missing"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id1"]!.Name, Is.EqualTo("FromRedis"));
            Assert.That(result["missing"], Is.Null);
        };
    }

    [Test]
    public async Task GetItemsAsync_DuplicateSuffixes_AreDeduplicated()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(result["id2"], Is.Null);
        };
    }

    [Test]
    public async Task GetItemsAsync_IntSuffixes_MapBackToSuffix()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemsAsync(new Dictionary<int, TestItem>
        {
            [1] = new TestItem { Name = "Alice" },
            [2] = new TestItem { Name = "Bob" },
        });

        IReadOnlyDictionary<int, TestItem?> result = await sut.GetItemsAsync([1, 2, 3]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[1]!.Name, Is.EqualTo("Alice"));
            Assert.That(result[2]!.Name, Is.EqualTo("Bob"));
            Assert.That(result[3], Is.Null);
        };
    }

    [Test]
    public async Task GetItemsAsync_BothDurationsZero_ReturnsNullForEachSuffix()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero, distDuration: TimeSpan.Zero);

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result["id1"], Is.Null);
            Assert.That(result["id2"], Is.Null);
        };
    }

    #endregion

    #region Remove

    [Test]
    public async Task RemoveAsync_WithoutSuffix_RemovesFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "ToRemove" });

        await sut.RemoveAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync(), Is.Null);
            Assert.That(_fakeDistCache.Contains(Key()), Is.False);
        };
    }

    [Test]
    public async Task RemoveAsync_WithSuffix_RemovesFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "ToRemove" }, "key1");
        await sut.SetAsync(new TestItem { Name = "ToKeep" }, "key2");

        await sut.RemoveAsync("key1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync("key1"), Is.Null);
            Assert.That(_fakeDistCache.Contains(Key("key1")), Is.False);
            Assert.That((await sut.GetAsync("key2"))!.Name, Is.EqualTo("ToKeep"));
        };
    }

    [Test]
    public async Task RemoveItemsAsync_RemovesAllSpecifiedKeysFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "A" }, "key1");
        await sut.SetAsync(new TestItem { Name = "B" }, "key2");
        await sut.SetAsync(new TestItem { Name = "C" }, "key3");

        await sut.RemoveItemsAsync(["key1", "key2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync("key1"), Is.Null);
            Assert.That(await sut.GetAsync("key2"), Is.Null);
            Assert.That(_fakeDistCache.Contains(Key("key1")), Is.False);
            Assert.That(_fakeDistCache.Contains(Key("key2")), Is.False);
            Assert.That((await sut.GetAsync("key3"))!.Name, Is.EqualTo("C"));
        };
    }

    [Test]
    public async Task RemoveItemsAsync_DuplicateSuffixes_DoesNotThrow()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "A" }, "key1");

        await sut.RemoveItemsAsync(["key1", "key1"]);

        Assert.That(await sut.GetAsync("key1"), Is.Null);
    }

    #endregion

    #region Distributed failures (graceful degradation)

    [Test]
    public async Task GetAsync_DistributedFailure_DoesNotThrowAndReturnsNull()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        Assert.That(await sut.GetAsync("id1"), Is.Null);
    }

    [Test]
    public async Task GetItemsAsync_DistributedFailure_ReturnsMemoryHitsAndNullForMisses()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "InMemory" }, "id1");

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id1"]!.Name, Is.EqualTo("InMemory"));
            Assert.That(result["id2"], Is.Null);
        };
    }

    [Test]
    public async Task SetAsync_DistributedFailure_StillStoresInMemory()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        _fakeDistCache.FailWith = null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
            Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice")); // served from memory
        };
    }

    [Test]
    public async Task SetItemsAsync_DistributedFailure_StillStoresInMemory()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        await sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } });

        _fakeDistCache.FailWith = null;

        Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice")); // served from memory
    }

    [Test]
    public async Task RemoveAsync_DistributedFailure_StillRemovesFromMemory()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        await sut.RemoveAsync("id1");

        _fakeDistCache.FailWith = null;
        _fakeDistCache.Clear();

        Assert.That(await sut.GetAsync("id1"), Is.Null); // would still be in memory if the removal had been skipped
    }

    [Test]
    public async Task RemoveItemsAsync_DistributedFailure_StillRemovesFromMemory()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "A" }, "key1");
        await sut.SetAsync(new TestItem { Name = "B" }, "key2");

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        await sut.RemoveItemsAsync(["key1", "key2"]);

        _fakeDistCache.FailWith = null;
        _fakeDistCache.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync("key1"), Is.Null);
            Assert.That(await sut.GetAsync("key2"), Is.Null);
        };
    }

    [Test]
    public async Task GetAsync_DistributedEnabledButNotRegistered_DoesNotThrow()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        Assert.That(await sut.GetAsync("id1"), Is.Null);
    }

    #endregion

    #region Tenant isolation

    [Test]
    public async Task GetAsync_WithTenantOverride_IsolatesPerTenant()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "key1", tenantId: "tenant-a");
        await sut.SetAsync(new TestItem { Name = "Bob" }, "key1", tenantId: "tenant-b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await sut.GetAsync("key1", tenantId: "tenant-a"))!.Name, Is.EqualTo("Alice"));
            Assert.That((await sut.GetAsync("key1", tenantId: "tenant-b"))!.Name, Is.EqualTo("Bob"));
            Assert.That(await sut.GetAsync("key1"), Is.Null); // the default tenant is a third, separate tenant
        };
    }

    [Test]
    public async Task SetItemsAsync_GetItemsAsync_WithTenantOverride_IsolatesPerTenant()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } }, tenantId: "tenant-a");

        IReadOnlyDictionary<string, TestItem?> tenantA = await sut.GetItemsAsync(["id1"], tenantId: "tenant-a");
        IReadOnlyDictionary<string, TestItem?> defaultTenant = await sut.GetItemsAsync(["id1"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tenantA["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(defaultTenant["id1"], Is.Null);
        };
    }

    #endregion

    #region Global scope

    [Test]
    public async Task Global_SetAsync_WithoutSuffix_UsesKeyWithoutTenant()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await sut.SetAsync(new TestItem { Name = "Alice" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(GlobalKey()), Is.True);
            Assert.That(_memoryCache.TryGetValue(GlobalKey(), out object? _), Is.True);
            Assert.That(_fakeDistCache.Contains(Key()), Is.False);
        };
    }

    [Test]
    public async Task Global_SetAsync_WithSuffix_UsesKeyWithoutTenant()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(GlobalKey("id1")), Is.True);
            Assert.That(_memoryCache.TryGetValue(GlobalKey("id1"), out object? _), Is.True);
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
            Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice"));
        };
    }

    [Test]
    public async Task Global_WithoutDefaultTenant_Works()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null, scope: CacheScopes.Global);

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");
        await sut.SetAsync(new TestItem { Name = "Config" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice"));
            Assert.That((await sut.GetAsync())!.Name, Is.EqualTo("Config"));
        };
    }

    [Test]
    public async Task Global_IsSharedByAllTenants()
    {
        CacheService<TestItem> tenantA = CreateService(tenantId: "tenant-a", scope: CacheScopes.Global);
        CacheService<TestItem> tenantB = CreateService(tenantId: "tenant-b", scope: CacheScopes.Global);

        await tenantA.SetAsync(new TestItem { Name = "Shared" }, "id1");

        Assert.That((await tenantB.GetAsync("id1"))!.Name, Is.EqualTo("Shared"));
    }

    [Test]
    public async Task Global_DoesNotCollideWithTenantKeys()
    {
        CacheService<TestItem> tenantService = CreateService();
        CacheService<TestItem> globalService = CreateService(scope: CacheScopes.Global);

        await tenantService.SetAsync(new TestItem { Name = "Tenant" }, "id1");
        await globalService.SetAsync(new TestItem { Name = "Global" }, "id1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await tenantService.GetAsync("id1"))!.Name, Is.EqualTo("Tenant"));
            Assert.That((await globalService.GetAsync("id1"))!.Name, Is.EqualTo("Global"));
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True);
            Assert.That(_fakeDistCache.Contains(GlobalKey("id1")), Is.True);
        };
    }

    [Test]
    public async Task Global_SetItemsAsync_GetItemsAsync_UseKeysWithoutTenant()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await sut.SetItemsAsync(new Dictionary<string, TestItem>
        {
            ["id1"] = new TestItem { Name = "Alice" },
            ["id2"] = new TestItem { Name = "Bob" },
        });

        bool inDistributed = _fakeDistCache.Contains(GlobalKey("id1")) && _fakeDistCache.Contains(GlobalKey("id2"));

        _fakeDistCache.Clear();

        IReadOnlyDictionary<string, TestItem?> results = await sut.GetItemsAsync(["id1", "id2"]); // served from memory

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inDistributed, Is.True);
            Assert.That(results["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(results["id2"]!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task Global_GetItemsAsync_RequestsKeysWithoutTenantFromDistributed()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await sut.GetItemsAsync(["id1", "id2"]);

        Assert.That(_fakeDistCache.RequestedKeys, Is.EquivalentTo(new[] { GlobalKey("id1"), GlobalKey("id2") }));
    }

    [Test]
    public async Task Global_RemoveAsync_RemovesFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await sut.SetAsync(new TestItem { Name = "ToRemove" }, "id1");
        await sut.SetAsync(new TestItem { Name = "ToKeep" }, "id2");

        await sut.RemoveAsync("id1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync("id1"), Is.Null);
            Assert.That(_fakeDistCache.Contains(GlobalKey("id1")), Is.False);
            Assert.That((await sut.GetAsync("id2"))!.Name, Is.EqualTo("ToKeep"));
        };
    }

    [Test]
    public async Task Global_RemoveItemsAsync_RemovesFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await sut.SetAsync(new TestItem { Name = "A" }, "id1");
        await sut.SetAsync(new TestItem { Name = "B" }, "id2");
        await sut.SetAsync(new TestItem { Name = "C" }, "id3");

        await sut.RemoveItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync("id1"), Is.Null);
            Assert.That(await sut.GetAsync("id2"), Is.Null);
            Assert.That(_fakeDistCache.Contains(GlobalKey("id1")), Is.False);
            Assert.That(_fakeDistCache.Contains(GlobalKey("id2")), Is.False);
            Assert.That((await sut.GetAsync("id3"))!.Name, Is.EqualTo("C"));
        };
    }

    [Test]
    public async Task Global_GetAsync_WithTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_GetAsync_WithSuffixAndTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync("id1", tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_GetItemsAsync_WithTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemsAsync(["id1"], tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_SetAsync_WithTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetAsync(new TestItem { Name = "Alice" }, "id1", tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_SetItemsAsync_WithTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } }, tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_RemoveAsync_WithTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.RemoveAsync("id1", tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_RemoveItemsAsync_WithTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.RemoveItemsAsync(["id1"], tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task Global_DistributedFailure_StillStoresInMemoryAndDoesNotThrow()
    {
        CacheService<TestItem> sut = CreateService(scope: CacheScopes.Global);
        _fakeDistCache.FailWith = new InvalidOperationException("Redis down");

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        Assert.That((await sut.GetAsync("id1"))!.Name, Is.EqualTo("Alice"));
    }

    #endregion

    #region Duration zero

    [Test]
    public async Task GetAsync_BothDurationsZero_AlwaysReturnsNull()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero, distDuration: TimeSpan.Zero);

        await sut.SetAsync(new TestItem { Name = "ShouldNotBeCached" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync(), Is.Null);
            Assert.That(_fakeDistCache.Contains(Key()), Is.False);
        };
    }

    [Test]
    public async Task SetAsync_WhenMemoryDurationIsZero_StoresOnlyInDistributed()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero);

        await sut.SetAsync(new TestItem { Name = "DistOnly" }, "key1");

        _fakeDistCache.Seed(Key("key1"), new TestItem { Name = "Changed" }); // proves reads go to the distributed cache, not to memory

        Assert.That((await sut.GetAsync("key1"))!.Name, Is.EqualTo("Changed"));
    }

    [Test]
    public async Task SetAsync_WhenDistributedDurationIsZero_DoesNotStoreInDistributed()
    {
        CacheService<TestItem> sut = CreateService(distDuration: TimeSpan.Zero);

        await sut.SetAsync(new TestItem { Name = "MemOnly" }, "key1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("key1")), Is.False);
            Assert.That((await sut.GetAsync("key1"))!.Name, Is.EqualTo("MemOnly"));
        };
    }

    [Test]
    public async Task MemoryOnly_WithoutDistributedService_Works()
    {
        CacheService<TestItem> sut = CreateService(distDuration: TimeSpan.Zero, includeDistributedCache: false);

        await sut.SetAsync(new TestItem { Name = "MemOnly" }, "key1");
        await sut.SetItemsAsync(new Dictionary<string, TestItem> { ["key2"] = new TestItem { Name = "MemOnly2" } });

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["key1", "key2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["key1"]!.Name, Is.EqualTo("MemOnly"));
            Assert.That(result["key2"]!.Name, Is.EqualTo("MemOnly2"));
        };

        await sut.RemoveItemsAsync(["key1", "key2"]);

        Assert.That(await sut.GetAsync("key1"), Is.Null);
    }

    #endregion

    #region Memory only

    [Test]
    public async Task MemoryOnly_WithoutSuffix_SetGetRemove_Works()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "Config" });

        TestItem? stored = await sut.GetAsync();

        await sut.RemoveAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Name, Is.EqualTo("Config"));
            Assert.That(await sut.GetAsync(), Is.Null);
        };
    }

    [Test]
    public async Task MemoryOnly_RemoveAsync_WithSuffix_RemovesOnlyThatItem()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "ToRemove" }, "key1");
        await sut.SetAsync(new TestItem { Name = "ToKeep" }, "key2");

        await sut.RemoveAsync("key1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetAsync("key1"), Is.Null);
            Assert.That((await sut.GetAsync("key2"))!.Name, Is.EqualTo("ToKeep"));
        };
    }

    [Test]
    public async Task MemoryOnly_NeverTouchesTheDistributedCache()
    {
        CacheService<TestItem> sut = CreateService(distDuration: TimeSpan.Zero);

        _fakeDistCache.Seed(Key("seeded"), new TestItem { Name = "FromRedis" });
        _fakeDistCache.RequestedKeys.Clear();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");
        TestItem? seeded = await sut.GetAsync("seeded");
        await sut.GetItemsAsync(["id1", "seeded"]);
        await sut.RemoveAsync("id1");
        await sut.RemoveItemsAsync(["seeded"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeded, Is.Null); // the seeded distributed value is never read
            Assert.That(_fakeDistCache.RequestedKeys, Is.Empty);
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
            Assert.That(_fakeDistCache.Contains(Key("seeded")), Is.True); // and never removed
        };
    }

    #endregion

    #region Distributed only

    [Test]
    public async Task DistributedOnly_WithoutSuffix_SetAndGet_UseDistributedCache()
    {
        CacheService<TestItem> sut = CreateDistributedOnlyService();

        await sut.SetAsync(new TestItem { Name = "Config" });

        _fakeDistCache.Seed(Key(), new TestItem { Name = "Changed" }); // proves the read goes to the distributed cache

        Assert.That((await sut.GetAsync())!.Name, Is.EqualTo("Changed"));
    }

    [Test]
    public async Task DistributedOnly_SetItemsAsync_GetItemsAsync_UseDistributedCache()
    {
        CacheService<TestItem> sut = CreateDistributedOnlyService();

        await sut.SetItemsAsync(new Dictionary<string, TestItem>
        {
            ["id1"] = new TestItem { Name = "Alice" },
            ["id2"] = new TestItem { Name = "Bob" },
        });

        bool storedInDistributed = _fakeDistCache.Contains(Key("id1")) && _fakeDistCache.Contains(Key("id2"));

        _fakeDistCache.Seed(Key("id1"), new TestItem { Name = "Changed" }); // proves the read goes to the distributed cache

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(storedInDistributed, Is.True);
            Assert.That(result["id1"]!.Name, Is.EqualTo("Changed"));
            Assert.That(result["id2"]!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task DistributedOnly_RemoveAsync_RemovesFromDistributed()
    {
        CacheService<TestItem> sut = CreateDistributedOnlyService();

        await sut.SetAsync(new TestItem { Name = "ToRemove" }, "key1");

        await sut.RemoveAsync("key1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("key1")), Is.False);
            Assert.That(await sut.GetAsync("key1"), Is.Null);
        };
    }

    [Test]
    public async Task DistributedOnly_RemoveItemsAsync_RemovesFromDistributed()
    {
        CacheService<TestItem> sut = CreateDistributedOnlyService();

        await sut.SetAsync(new TestItem { Name = "A" }, "key1");
        await sut.SetAsync(new TestItem { Name = "B" }, "key2");
        await sut.SetAsync(new TestItem { Name = "C" }, "key3");

        await sut.RemoveItemsAsync(["key1", "key2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("key1")), Is.False);
            Assert.That(_fakeDistCache.Contains(Key("key2")), Is.False);
            Assert.That(_fakeDistCache.Contains(Key("key3")), Is.True);
        };
    }

    [Test]
    public async Task DistributedOnly_DistributedFailure_DoesNotThrow()
    {
        CacheService<TestItem> sut = CreateDistributedOnlyService();

        _fakeDistCache.FailWith = new InvalidOperationException("redis down");

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");
        await sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id2"] = new TestItem { Name = "Bob" } });

        TestItem? single = await sut.GetAsync("id1");
        IReadOnlyDictionary<string, TestItem?> multiple = await sut.GetItemsAsync(["id1", "id2"]);

        await sut.RemoveAsync("id1");
        await sut.RemoveItemsAsync(["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(single, Is.Null);
            Assert.That(multiple["id1"], Is.Null);
            Assert.That(multiple["id2"], Is.Null);
        };
    }

    #endregion

    #region Both layers disabled

    [Test]
    public async Task BothDurationsZero_RemoveOperations_DoNothing()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero, distDuration: TimeSpan.Zero);

        _fakeDistCache.Seed(Key("id1"), new TestItem { Name = "Seeded" });

        await sut.RemoveAsync("id1");
        await sut.RemoveItemsAsync(["id1"]);

        Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True); // caching is disabled, so nothing is touched
    }

    #endregion

    #region Synchronous completion (ValueTask)

    [Test]
    public async Task MemoryOnly_GetAsync_Miss_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        ValueTask<TestItem?> task = sut.GetAsync("missing");
        bool isCompleted = task.IsCompletedSuccessfully;

        TestItem? result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result, Is.Null);
        };
    }

    [Test]
    public async Task MemoryOnly_GetAsync_Hit_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        ValueTask<TestItem?> task = sut.GetAsync("id1");
        bool isCompleted = task.IsCompletedSuccessfully;

        TestItem? result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result!.Name, Is.EqualTo("Alice"));
        };
    }

    [Test]
    public async Task MemoryOnly_GetAsync_WithoutSuffix_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "Config" });

        ValueTask<TestItem?> task = sut.GetAsync();
        bool isCompleted = task.IsCompletedSuccessfully;

        TestItem? result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result!.Name, Is.EqualTo("Config"));
        };
    }

    [Test]
    public async Task MemoryOnly_GetItemsAsync_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        ValueTask<IReadOnlyDictionary<string, TestItem?>> task = sut.GetItemsAsync(["id1", "missing"]);
        bool isCompleted = task.IsCompletedSuccessfully;

        IReadOnlyDictionary<string, TestItem?> result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(result["missing"], Is.Null);
        };
    }

    [Test]
    public async Task MemoryOnly_SetAsync_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        ValueTask task = sut.SetAsync(new TestItem { Name = "Alice" }, "id1");
        bool isCompleted = task.IsCompletedSuccessfully;

        await task;

        Assert.That(isCompleted, Is.True);
    }

    [Test]
    public async Task MemoryOnly_SetItemsAsync_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        ValueTask task = sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } });
        bool isCompleted = task.IsCompletedSuccessfully;

        await task;

        Assert.That(isCompleted, Is.True);
    }

    [Test]
    public async Task MemoryOnly_RemoveAsync_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        ValueTask task = sut.RemoveAsync("id1");
        bool isCompleted = task.IsCompletedSuccessfully;

        await task;

        Assert.That(isCompleted, Is.True);
    }

    [Test]
    public async Task MemoryOnly_RemoveItemsAsync_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        ValueTask task = sut.RemoveItemsAsync(["id1", "id2"]);
        bool isCompleted = task.IsCompletedSuccessfully;

        await task;

        Assert.That(isCompleted, Is.True);
    }

    [Test]
    public async Task Mixed_GetAsync_MemoryHit_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetAsync(new TestItem { Name = "Alice" }, "id1");

        ValueTask<TestItem?> task = sut.GetAsync("id1");
        bool isCompleted = task.IsCompletedSuccessfully;

        TestItem? result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result!.Name, Is.EqualTo("Alice"));
        };
    }

    [Test]
    public async Task BothDurationsZero_AllOperations_CompleteSynchronously()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero, distDuration: TimeSpan.Zero);

        ValueTask<TestItem?> get = sut.GetAsync("id1");
        bool getCompleted = get.IsCompletedSuccessfully;
        await get;

        ValueTask<IReadOnlyDictionary<string, TestItem?>> getItems = sut.GetItemsAsync(["id1"]);
        bool getItemsCompleted = getItems.IsCompletedSuccessfully;
        await getItems;

        ValueTask set = sut.SetAsync(new TestItem { Name = "Alice" }, "id1");
        bool setCompleted = set.IsCompletedSuccessfully;
        await set;

        ValueTask setItems = sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } });
        bool setItemsCompleted = setItems.IsCompletedSuccessfully;
        await setItems;

        ValueTask remove = sut.RemoveAsync("id1");
        bool removeCompleted = remove.IsCompletedSuccessfully;
        await remove;

        ValueTask removeItems = sut.RemoveItemsAsync(["id1"]);
        bool removeItemsCompleted = removeItems.IsCompletedSuccessfully;
        await removeItems;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(getCompleted, Is.True);
            Assert.That(getItemsCompleted, Is.True);
            Assert.That(setCompleted, Is.True);
            Assert.That(setItemsCompleted, Is.True);
            Assert.That(removeCompleted, Is.True);
            Assert.That(removeItemsCompleted, Is.True);
        };
    }

    #endregion

    #region Validations

    [Test]
    public async Task GetAsync_NullSuffix_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetAsync<string>(null!).AsTask());
    }

    [Test]
    public async Task SetAsync_NullSuffix_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetAsync(new TestItem(), (string)null!).AsTask());
    }

    [Test]
    public async Task SetAsync_NullItem_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetAsync(null!, "id1").AsTask());
    }

    [Test]
    public async Task RemoveAsync_NullSuffix_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RemoveAsync<string>(null!).AsTask());
    }

    [Test]
    public async Task GetItemsAsync_NullSuffixes_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetItemsAsync<string>(cacheKeySuffixes: null!).AsTask());
    }

    [Test]
    public async Task GetItemsAsync_EmptySuffixes_ReturnsEmptyDictionaryWithoutReadingDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetItemsAsync(Array.Empty<string>());

        Assert.That(result, Is.Empty);
        Assert.That(_fakeDistCache.RequestedKeys, Is.Empty);
    }

    [Test]
    public async Task GetItemsAsync_BlankSuffix_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemsAsync(["id1", " "]).AsTask());
    }

    [Test]
    public async Task GetItemsAsync_NullElementInSuffixes_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemsAsync(new string[] { "id1", null! }).AsTask());
    }

    [Test]
    public async Task RemoveItemsAsync_NullSuffixes_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RemoveItemsAsync<string>(cacheKeySuffixes: null!).AsTask());
    }

    [Test]
    public async Task RemoveItemsAsync_EmptySuffixes_DoesNothing()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.DoesNotThrowAsync(() => sut.RemoveItemsAsync(Array.Empty<string>()).AsTask());
    }

    [Test]
    public async Task RemoveItemsAsync_BlankSuffix_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.RemoveItemsAsync(["id1", ""]).AsTask());
    }

    [Test]
    public async Task SetItemsAsync_NullItems_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetItemsAsync<string>(items: null!).AsTask());
    }

    [Test]
    public async Task SetItemsAsync_EmptyItems_DoesNothing()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.DoesNotThrowAsync(() => sut.SetItemsAsync(new Dictionary<string, TestItem>()).AsTask());
    }

    [Test]
    public async Task SetItemsAsync_BlankKey_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetItemsAsync(new Dictionary<string, TestItem> { ["  "] = new TestItem() }).AsTask());
    }

    [Test]
    public async Task SetItemsAsync_ItemWithNullValue_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetItemsAsync(new Dictionary<string, TestItem> { ["key"] = null! }).AsTask());
    }

    [Test]
    public async Task GetAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        CacheService<TestItem> sut = CreateService();
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetAsync(cancellationToken: cancellationTokenSource.Token).AsTask());
    }

    [Test]
    public async Task SetAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        CacheService<TestItem> sut = CreateService();
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.SetAsync(new TestItem(), cancellationToken: cancellationTokenSource.Token).AsTask());
    }

    [Test]
    public async Task RemoveItemsAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        CacheService<TestItem> sut = CreateService();
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.RemoveItemsAsync(["id1"], cancellationToken: cancellationTokenSource.Token).AsTask());
    }

    [Test]
    public async Task GetAsync_NullTenantIdWithNoDefaultTenant_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetAsync().AsTask());
    }

    [Test]
    public async Task GetAsync_NullDefaultTenantWithTenantOverride_Works()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null);

        await sut.SetAsync(new TestItem { Name = "Alice" }, tenantId: "tenant-a");

        Assert.That((await sut.GetAsync(tenantId: "tenant-a"))!.Name, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task SetItemsAsync_InvalidTenantId_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } }, tenantId: "invalid tenant!").AsTask());
    }

    [Test]
    public async Task SetItemsAsync_NullTenantIdWithNoDefaultTenant_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } }).AsTask());
    }

    [Test]
    public async Task SetItemsAsync_InvalidTenantId_DoesNotStoreInAnyLayer()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetItemsAsync(new Dictionary<string, TestItem> { ["id1"] = new TestItem { Name = "Alice" } }, tenantId: "invalid tenant!").AsTask());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("id1", "invalid tenant!")), Is.False);
            Assert.That(await sut.GetAsync("id1"), Is.Null);
        };
    }

    [Test]
    public async Task SetItemsAsync_DifferentSuffixesWithSameStringRepresentation_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        Dictionary<FixedSuffix, TestItem> items = new()
        {
            [new FixedSuffix("dup")] = new TestItem { Name = "Alice" },
            [new FixedSuffix("dup")] = new TestItem { Name = "Bob" },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetItemsAsync(items).AsTask());
    }

    [Test]
    public async Task GetAsync_InvalidTenantIdOverrideCharacters_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(tenantId: "invalid tenant!").AsTask());
    }

    [Test]
    public async Task GetAsync_TenantIdOverrideTooLong_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();
        string tenantId = new('a', 51);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(tenantId: tenantId).AsTask());
    }

    [Test]
    public async Task GetAsync_EmptyTenantIdOverride_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(tenantId: string.Empty).AsTask());
    }

    #endregion
}