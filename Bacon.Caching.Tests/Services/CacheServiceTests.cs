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

    private CacheService<TestItem> CreateService(string tenantId = TenantId, TimeSpan? memDuration = null, TimeSpan? distDuration = null, bool includeDistributedCache = true)
    {
        IOptions<CacheServerOptions> serverOptions = Options.Create(new CacheServerOptions { TenantId = tenantId });
        IOptions<CacheOptions<TestItem>> cacheOptions = Options.Create(new CacheOptions<TestItem>
        {
            CacheKey = CacheKey,
            MemoryCacheDuration = memDuration ?? TimeSpan.FromMinutes(5),
            DistributedCacheDuration = distDuration ?? TimeSpan.FromMinutes(10),
        });

        return new CacheService<TestItem>(serverOptions, cacheOptions, _memoryCache, NullLogger<TestItem>.Instance, includeDistributedCache ? _fakeDistCache : null);
    }

    #region Get / Set

    [Test]
    public async Task SetItemAsync_GetItemAsync_ReturnsItem()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "Alice", Value = 1 });

        TestItem? result = await sut.GetItemAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Name, Is.EqualTo("Alice"));
        };
    }

    [Test]
    public async Task SetItemAsync_GetItemAsync_WithSuffix_IsolatesByKey()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "Alice" }, cacheKeySuffix: "id1");
        await sut.SetItemAsync(new TestItem { Name = "Bob" }, cacheKeySuffix: "id2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await sut.GetItemAsync(cacheKeySuffix: "id1"))!.Name, Is.EqualTo("Alice"));
            Assert.That((await sut.GetItemAsync(cacheKeySuffix: "id2"))!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task GetItemAsync_WhenNotCached_ReturnsNull()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.That(await sut.GetItemAsync(cacheKeySuffix: "missing"), Is.Null);
    }

    [Test]
    public async Task SetItemAsync_StoresInDistributedCache()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "Alice" }, cacheKeySuffix:"id1");

        Assert.That(_fakeDistCache.Contains($"{TenantId}_{CacheKey}id1"), Is.True);
    }

    #endregion

    #region Memory-first / fallthrough

    [Test]
    public async Task GetItemAsync_MemoryMiss_FallsThroughToDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.Seed($"{TenantId}_{CacheKey}myid", new TestItem { Name = "FromRedis" });

        TestItem? result = await sut.GetItemAsync(cacheKeySuffix: "myid");

        Assert.That(result!.Name, Is.EqualTo("FromRedis"));
    }

    [Test]
    public async Task GetItemAsync_DistributedHit_BackfillsMemoryCache()
    {
        CacheService<TestItem> sut = CreateService();

        _fakeDistCache.Seed($"{TenantId}_{CacheKey}myid", new TestItem { Name = "FromRedis" });

        await sut.GetItemAsync(cacheKeySuffix: "myid"); // populates memory cache

        _fakeDistCache.Clear(); // wipe distributed

        TestItem? result = await sut.GetItemAsync(cacheKeySuffix: "myid"); // should hit memory

        Assert.That(result!.Name, Is.EqualTo("FromRedis"));
    }

    [Test]
    public async Task GetItemAsync_MemoryHit_DoesNotRequireDistributed()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "InMemory" }, cacheKeySuffix: "id1");

        _fakeDistCache.Clear(); // remove from distributed after set

        TestItem? result = await sut.GetItemAsync(cacheKeySuffix: "id1");

        Assert.That(result!.Name, Is.EqualTo("InMemory"));
    }

    #endregion

    #region Memory-only operations

    [Test]
    public void SetMemoryItem_GetMemoryItem_ReturnsItem()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        sut.SetMemoryItem(new TestItem { Name = "MemOnly" }, cacheKeySuffix: "key1");

        Assert.That(sut.GetMemoryItem(cacheKeySuffix: "key1")!.Name, Is.EqualTo("MemOnly"));
    }

    [Test]
    public void GetMemoryItem_WhenNotCached_ReturnsNull()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        Assert.That(sut.GetMemoryItem(cacheKeySuffix: "missing"), Is.Null);
    }

    [Test]
    public void RemoveMemoryItem_RemovesFromMemory()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        sut.SetMemoryItem(new TestItem { Name = "ToRemove" }, cacheKeySuffix: "key1");

        sut.RemoveMemoryItem(cacheKeySuffix: "key1");

        Assert.That(sut.GetMemoryItem(cacheKeySuffix: "key1"), Is.Null);
    }

    [Test]
    public void SetMemoryItems_GetMemoryItems_ReturnsAllItems()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        sut.SetMemoryItems(new Dictionary<string, TestItem>
        {
            ["a"] = new TestItem { Name = "Alice" },
            ["b"] = new TestItem { Name = "Bob" },
        });

        IDictionary<string, TestItem?> result = sut.GetMemoryItems(cacheKeySuffixes: ["a", "b"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["a"]!.Name, Is.EqualTo("Alice"));
            Assert.That(result["b"]!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public void FlushMemoryItems_RemovesAllMatchingKeys()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        sut.SetMemoryItem(new TestItem { Name = "A" }, cacheKeySuffix: "key1");
        sut.SetMemoryItem(new TestItem { Name = "B" }, cacheKeySuffix: "key2");

        sut.FlushMemoryItems();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.GetMemoryItem(cacheKeySuffix: "key1"), Is.Null);
            Assert.That(sut.GetMemoryItem(cacheKeySuffix: "key2"), Is.Null);
        };
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

        IDictionary<string, TestItem?> result = await sut.GetItemsAsync(cacheKeySuffixes: ["id1", "id2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id1"]!.Name, Is.EqualTo("Alice"));
            Assert.That(result["id2"]!.Name, Is.EqualTo("Bob"));
        };
    }

    [Test]
    public async Task GetItemsAsync_MixedHitsAndMisses_NullForMissing()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "Alice" }, cacheKeySuffix: "id1");

        IDictionary<string, TestItem?> result = await sut.GetItemsAsync(cacheKeySuffixes: ["id1", "missing"]);

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

        _fakeDistCache.Seed($"{TenantId}_{CacheKey}id1", new TestItem { Name = "FromRedis" });

        IDictionary<string, TestItem?> result = await sut.GetItemsAsync(cacheKeySuffixes: ["id1"]);

        Assert.That(result["id1"]!.Name, Is.EqualTo("FromRedis"));
    }

    #endregion

    #region Remove

    [Test]
    public async Task RemoveItemAsync_RemovesFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "ToRemove" }, cacheKeySuffix: "key1");

        await sut.RemoveItemAsync(cacheKeySuffix: "key1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetItemAsync(cacheKeySuffix: "key1"), Is.Null);
            Assert.That(_fakeDistCache.Contains($"{TenantId}_{CacheKey}key1"), Is.False);
        };
    }

    [Test]
    public async Task RemoveItemsAsync_RemovesAllSpecifiedKeys()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "A" }, cacheKeySuffix: "key1");
        await sut.SetItemAsync(new TestItem { Name = "B" }, cacheKeySuffix: "key2");

        await sut.RemoveItemsAsync(["key1", "key2"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetItemAsync(cacheKeySuffix: "key1"), Is.Null);
            Assert.That(await sut.GetItemAsync(cacheKeySuffix: "key2"), Is.Null);
        };
    }

    #endregion

    #region Flush

    [Test]
    public async Task FlushItemsAsync_ClearsAllMatchingFromBothCaches()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "A" }, cacheKeySuffix: "key1");
        await sut.SetItemAsync(new TestItem { Name = "B" }, cacheKeySuffix: "key2");

        await sut.FlushItemsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await sut.GetItemAsync(cacheKeySuffix: "key1"), Is.Null);
            Assert.That(await sut.GetItemAsync(cacheKeySuffix: "key2"), Is.Null);
        };
    }

    #endregion

    #region Tenant isolation

    [Test]
    public async Task GetItemAsync_WithTenantOverride_IsolatesPerTenant()
    {
        CacheService<TestItem> sut = CreateService();

        await sut.SetItemAsync(new TestItem { Name = "Alice" }, cacheKeySuffix: "key1", tenantId: "tenant-a");
        await sut.SetItemAsync(new TestItem { Name = "Bob" }, cacheKeySuffix: "key1", tenantId: "tenant-b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await sut.GetItemAsync(cacheKeySuffix: "key1", tenantId: "tenant-a"))!.Name, Is.EqualTo("Alice"));
            Assert.That((await sut.GetItemAsync(cacheKeySuffix: "key1", tenantId: "tenant-b"))!.Name, Is.EqualTo("Bob"));
        };
    }

    #endregion

    #region Duration zero

    [Test]
    public async Task GetItemAsync_BothDurationsZero_AlwaysReturnsNull()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero, distDuration: TimeSpan.Zero);

        await sut.SetItemAsync(new TestItem { Name = "ShouldNotBeCached" });

        Assert.That(await sut.GetItemAsync(), Is.Null);
    }

    [Test]
    public void SetMemoryItem_WhenMemoryDurationIsZero_DoesNotStore()
    {
        CacheService<TestItem> sut = CreateService(memDuration: TimeSpan.Zero, includeDistributedCache: false);

        sut.SetMemoryItem(new TestItem { Name = "ShouldNotStore" }, cacheKeySuffix: "key1");

        Assert.That(sut.GetMemoryItem(cacheKeySuffix: "key1"), Is.Null);
    }

    [Test]
    public async Task SetItemAsync_WhenDistributedDurationIsZero_DoesNotStoreInDistributed()
    {
        CacheService<TestItem> sut = CreateService(distDuration: TimeSpan.Zero);

        await sut.SetItemAsync(new TestItem { Name = "MemOnly" }, cacheKeySuffix: "key1");

        Assert.That(_fakeDistCache.Contains($"{TenantId}_{CacheKey}key1"), Is.False);
    }

    #endregion

    #region Validations

    [Test]
    public void GetItemAsync_WithoutDistributedCache_ThrowsInvalidOperationException()
    {
        CacheService<TestItem> sut = CreateService(includeDistributedCache: false);

        Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetItemAsync());
    }

    [Test]
    public void GetItemsAsync_NullSuffixes_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetItemsAsync<string>(cacheKeySuffixes: null!));
    }

    [Test]
    public void GetItemsAsync_EmptySuffixes_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemsAsync(cacheKeySuffixes: Array.Empty<string>()));
    }

    [Test]
    public void SetItemsAsync_ItemWithNullValue_ThrowsArgumentNullException()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetItemsAsync(items: new Dictionary<string, TestItem> { ["key"] = null! }));
    }

    [Test]
    public void GetItemAsync_NullTenantIdWithNoDefaultTenant_ThrowsArgumentNullException()
    {
        IOptions<CacheServerOptions> serverOptions = Options.Create(new CacheServerOptions { TenantId = null });
        IOptions<CacheOptions<TestItem>> cacheOptions = Options.Create(new CacheOptions<TestItem>
        {
            CacheKey = CacheKey,
            MemoryCacheDuration = TimeSpan.FromMinutes(5),
            DistributedCacheDuration = TimeSpan.FromMinutes(10),
        });

        CacheService<TestItem> sut = new(serverOptions, cacheOptions, _memoryCache, NullLogger<TestItem>.Instance, _fakeDistCache);

        Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetItemAsync());
    }

    [Test]
    public void GetItemAsync_InvalidTenantIdOverrideCharacters_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemAsync(tenantId: "invalid tenant!"));
    }

    [Test]
    public void GetItemAsync_TenantIdOverrideTooLong_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();
        string tenantId = new('a', 51);

        Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemAsync(tenantId: tenantId));
    }

    [Test]
    public void GetItemAsync_EmptyTenantIdOverride_ThrowsArgumentException()
    {
        CacheService<TestItem> sut = CreateService();

        Assert.ThrowsAsync<ArgumentException>(() => sut.GetItemAsync(tenantId: string.Empty));
    }

    #endregion
}
