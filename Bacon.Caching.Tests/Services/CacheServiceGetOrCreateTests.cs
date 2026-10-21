using Bacon.Caching.Models;
using Bacon.Caching.Services;
using Bacon.Caching.Tests.Fakes;
using Bacon.Caching.Tests.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bacon.Caching.Tests.Services;

[TestFixture]
internal sealed class CacheServiceGetOrCreateTests
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

    private CacheService<TestItem> CreateMemoryOnlyService() => CreateService(distDuration: TimeSpan.Zero, includeDistributedCache: false);

    private CacheService<TestItem> CreateBothDisabledService() => CreateService(memDuration: TimeSpan.Zero, distDuration: TimeSpan.Zero);

    private static string Key(string suffix = "", string tenantId = TenantId) => $"{tenantId}_{CacheKey}{suffix}";

    private static string GlobalKey(string suffix = "") => $"|{CacheKey}{suffix}";

    private bool IsInMemory(string key) => _memoryCache.TryGetValue(key, out TestItem? _);

    private static TestItem Item(string name) => new() { Name = name };

    private static ValueTask<TestItem?> Created(TestItem item) => ValueTask.FromResult<TestItem?>(item);

    private static ValueTask<IReadOnlyDictionary<string, TestItem>> Found(Dictionary<string, TestItem> items) => ValueTask.FromResult<IReadOnlyDictionary<string, TestItem>>(items);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for the condition");
            }

            await Task.Delay(10);
        }
    }

    private static async Task<TResult> WithinAsync<TResult>(Task<TResult> task) => await task.WaitAsync(TimeSpan.FromSeconds(5));

    private sealed class FixedSuffix(string value)
    {
        public override string ToString() => value;
    }

    #region Gated factories

    private sealed class GatedCall<TResult>
    {
        public TaskCompletionSource<TResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool IsCanceled;
    }

    /// <summary>
    /// A single item factory that waits until the test releases (or fails) each call. Every call is recorded.
    /// </summary>
    private sealed class GatedFactory(bool observeToken = false)
    {
        private readonly List<GatedCall<TestItem?>> _calls = [];
        private readonly Lock _gate = new();

        public int Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.Count;
                }
            }
        }

        public bool WasCanceled(int call) => Get(call).IsCanceled;

        public void Release(int call, TestItem? item) => Get(call).Completion.TrySetResult(item);

        public void Fail(int call, Exception exception) => Get(call).Completion.TrySetException(exception);

        public async ValueTask<TestItem?> InvokeAsync(CancellationToken token)
        {
            GatedCall<TestItem?> call = new();

            lock (_gate)
            {
                _calls.Add(call);
            }

            using CancellationTokenRegistration registration = token.Register(() => call.IsCanceled = true);

            try
            {
                return observeToken ? await call.Completion.Task.WaitAsync(token) : await call.Completion.Task;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The wait can complete, and dispose the registration above, before the registered callback runs
                call.IsCanceled = true;

                throw;
            }
        }

        private GatedCall<TestItem?> Get(int call)
        {
            lock (_gate)
            {
                return _calls[call];
            }
        }
    }

    /// <summary>
    /// A batch factory that waits until the test releases (or fails) each call. Every call and the suffixes it received are recorded.
    /// </summary>
    private sealed class GatedBatchFactory(bool observeToken = false)
    {
        private readonly List<GatedCall<IReadOnlyDictionary<string, TestItem>>> _calls = [];
        private readonly List<string[]> _requests = [];
        private readonly Lock _gate = new();

        public int Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.Count;
                }
            }
        }

        public string[] RequestOf(int call)
        {
            lock (_gate)
            {
                return _requests[call];
            }
        }

        public bool WasCanceled(int call) => Get(call).IsCanceled;

        public void Release(int call, Dictionary<string, TestItem> items) => Get(call).Completion.TrySetResult(items);

        public void Fail(int call, Exception exception) => Get(call).Completion.TrySetException(exception);

        public async ValueTask<IReadOnlyDictionary<string, TestItem>> InvokeAsync(IReadOnlyCollection<string> suffixes, CancellationToken token)
        {
            GatedCall<IReadOnlyDictionary<string, TestItem>> call = new();

            lock (_gate)
            {
                _calls.Add(call);
                _requests.Add([.. suffixes]);
            }

            using CancellationTokenRegistration registration = token.Register(() => call.IsCanceled = true);

            try
            {
                return observeToken ? await call.Completion.Task.WaitAsync(token) : await call.Completion.Task;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The wait can complete, and dispose the registration above, before the registered callback runs
                call.IsCanceled = true;

                throw;
            }
        }

        private GatedCall<IReadOnlyDictionary<string, TestItem>> Get(int call)
        {
            lock (_gate)
            {
                return _calls[call];
            }
        }
    }

    #endregion Gated factories

    #region GetOrCreateAsync

    [Test]
    public async Task GetOrCreateAsync_Miss_LoadsAndStoresInBothLayers()
    {
        CacheService<TestItem> sut = CreateService();
        TestItem created = Item("Alice");
        int calls = 0;

        TestItem? result = await sut.GetOrCreateAsync("id1", _ =>
        {
            calls++;

            return Created(created);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(created));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True);
            Assert.That(IsInMemory(Key("id1")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_SecondCall_IsServedFromTheCache()
    {
        CacheService<TestItem> sut = CreateService();
        TestItem created = Item("Alice");
        int calls = 0;

        Func<CancellationToken, ValueTask<TestItem?>> factory = _ =>
        {
            calls++;

            return Created(created);
        };

        await sut.GetOrCreateAsync("id1", factory);
        TestItem? second = await sut.GetOrCreateAsync("id1", factory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(created));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_MemoryHit_DoesNotCallTheFactory()
    {
        CacheService<TestItem> sut = CreateService();
        TestItem existing = Item("Existing");
        await sut.SetAsync(existing, "id1");
        int calls = 0;

        TestItem? result = await sut.GetOrCreateAsync("id1", _ =>
        {
            calls++;

            return Created(Item("Fresh"));
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(existing));
            Assert.That(calls, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_DistributedHit_DoesNotCallTheFactory_AndBackfillsMemory()
    {
        CacheService<TestItem> sut = CreateService();
        TestItem existing = Item("Existing");
        _fakeDistCache.Seed(Key("id1"), existing);
        int calls = 0;

        TestItem? result = await sut.GetOrCreateAsync("id1", _ =>
        {
            calls++;

            return Created(Item("Fresh"));
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(existing));
            Assert.That(calls, Is.EqualTo(0));
            Assert.That(IsInMemory(Key("id1")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_FactoryReturnsNull_ReturnsNull_AndDoesNotCacheIt()
    {
        CacheService<TestItem> sut = CreateService();
        int calls = 0;

        Func<CancellationToken, ValueTask<TestItem?>> factory = _ =>
        {
            calls++;

            return ValueTask.FromResult<TestItem?>(null);
        };

        TestItem? first = await sut.GetOrCreateAsync("id1", factory);
        TestItem? second = await sut.GetOrCreateAsync("id1", factory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
            Assert.That(IsInMemory(Key("id1")), Is.False);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_WithoutSuffix_UsesTheKeyWithoutSuffix_AndTheTenantOverride()
    {
        CacheService<TestItem> sut = CreateService();

        TestItem? defaultTenant = await sut.GetOrCreateAsync(_ => Created(Item("Config")));
        TestItem? otherTenant = await sut.GetOrCreateAsync(_ => Created(Item("OtherConfig")), tenantId: "tenant-a");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(defaultTenant!.Name, Is.EqualTo("Config"));
            Assert.That(otherTenant!.Name, Is.EqualTo("OtherConfig"));
            Assert.That(_fakeDistCache.Contains(Key()), Is.True);
            Assert.That(_fakeDistCache.Contains(Key(tenantId: "tenant-a")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_GlobalScope_StoresAtTheGlobalKey_WithoutTenant()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null, scope: CacheScopes.Global);

        TestItem? result = await sut.GetOrCreateAsync("id1", _ => Created(Item("Shared")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Name, Is.EqualTo("Shared"));
            Assert.That(_fakeDistCache.Contains(GlobalKey("id1")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_ConcurrentMisses_CallTheFactoryOnce()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new();
        TestItem created = Item("Alice");

        Task<TestItem?>[] callers = [.. Enumerable.Range(0, 20).Select(_ => sut.GetOrCreateAsync("id1", factory.InvokeAsync).AsTask())];

        await WaitUntilAsync(() => factory.Calls == 1);

        factory.Release(0, created);

        TestItem?[] results = await WithinAsync(Task.WhenAll(callers));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Calls, Is.EqualTo(1));
            Assert.That(results, Has.All.SameAs(created));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_ConcurrentMissesOnDifferentKeys_LoadIndependently()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new();
        TestItem first = Item("First");
        TestItem second = Item("Second");

        Task<TestItem?> callerOne = sut.GetOrCreateAsync("id1", factory.InvokeAsync).AsTask();
        Task<TestItem?> callerTwo = sut.GetOrCreateAsync("id2", factory.InvokeAsync).AsTask();

        await WaitUntilAsync(() => factory.Calls == 2);

        factory.Release(0, first);
        factory.Release(1, second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await WithinAsync(callerOne), Is.SameAs(first));
            Assert.That(await WithinAsync(callerTwo), Is.SameAs(second));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_FactoryThrows_EveryWaitingCallerGetsTheException_NothingIsCached_AndTheNextCallRetries()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new();

        Task<TestItem?>[] callers = [.. Enumerable.Range(0, 5).Select(_ => sut.GetOrCreateAsync("id1", factory.InvokeAsync).AsTask())];

        await WaitUntilAsync(() => factory.Calls == 1);

        factory.Fail(0, new InvalidOperationException("boom"));

        foreach (Task<TestItem?> caller in callers)
        {
            InvalidOperationException exception = (await Assert.ThrowsAsync<InvalidOperationException>(() => caller))!;

            Assert.That(exception.Message, Is.EqualTo("boom"));
        }

        Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);

        TestItem created = Item("Alice");
        TestItem? retry = await sut.GetOrCreateAsync("id1", _ => Created(created));

        Assert.That(retry, Is.SameAs(created));
    }

    [Test]
    public async Task GetOrCreateAsync_OneCallerCancels_TheOthersStillGetTheResult()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new(observeToken: true);
        TestItem created = Item("Alice");
        using CancellationTokenSource cts = new();

        Task<TestItem?> leaving = sut.GetOrCreateAsync("id1", factory.InvokeAsync, cancellationToken: cts.Token).AsTask();
        Task<TestItem?> staying = sut.GetOrCreateAsync("id1", factory.InvokeAsync).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        await cts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => leaving);

        Assert.That(factory.WasCanceled(0), Is.False);

        factory.Release(0, created);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await WithinAsync(staying), Is.SameAs(created));
            Assert.That(factory.Calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_EveryCallerCancels_CancelsTheFactory_AndTheNextCallStartsANewLoad()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new(observeToken: true);
        using CancellationTokenSource firstCts = new();
        using CancellationTokenSource secondCts = new();

        Task<TestItem?> first = sut.GetOrCreateAsync("id1", factory.InvokeAsync, cancellationToken: firstCts.Token).AsTask();
        Task<TestItem?> second = sut.GetOrCreateAsync("id1", factory.InvokeAsync, cancellationToken: secondCts.Token).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        await firstCts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => first);

        Assert.That(factory.WasCanceled(0), Is.False);

        await secondCts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => second);
        await WaitUntilAsync(() => factory.WasCanceled(0));

        TestItem created = Item("Alice");
        TestItem? retry = await sut.GetOrCreateAsync("id1", _ => Created(created));

        Assert.That(retry, Is.SameAs(created));
    }

    [Test]
    public async Task GetOrCreateAsync_AbandonedLoad_IsNotJoined_ANewCallerStartsItsOwnLoad()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new();
        TestItem abandoned = Item("Abandoned");
        TestItem fresh = Item("Fresh");
        using CancellationTokenSource cts = new();

        Task<TestItem?> leaving = sut.GetOrCreateAsync("id1", factory.InvokeAsync, cancellationToken: cts.Token).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        await cts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => leaving);
        await WaitUntilAsync(() => factory.WasCanceled(0));

        Task<TestItem?> newCaller = sut.GetOrCreateAsync("id1", factory.InvokeAsync).AsTask();

        await WaitUntilAsync(() => factory.Calls == 2);

        factory.Release(1, fresh);

        Assert.That(await WithinAsync(newCaller), Is.SameAs(fresh));

        factory.Release(0, abandoned);
    }

    [Test]
    public async Task GetOrCreateAsync_EveryCallerLeft_TheFactoryResultIsStillStored()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory factory = new();
        TestItem created = Item("Alice");
        using CancellationTokenSource cts = new();

        Task<TestItem?> leaving = sut.GetOrCreateAsync("id1", factory.InvokeAsync, cancellationToken: cts.Token).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        await cts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => leaving);
        await WaitUntilAsync(() => factory.WasCanceled(0));

        factory.Release(0, created);

        await WaitUntilAsync(() => _fakeDistCache.Contains(Key("id1")));

        Assert.That(IsInMemory(Key("id1")), Is.True);
    }

    [Test]
    public async Task GetOrCreateAsync_BothLayersDisabled_CallsTheFactoryEveryTime()
    {
        CacheService<TestItem> sut = CreateBothDisabledService();
        int calls = 0;

        Func<CancellationToken, ValueTask<TestItem?>> factory = _ =>
        {
            calls++;

            return Created(Item("Alice"));
        };

        await sut.GetOrCreateAsync("id1", factory);
        await sut.GetOrCreateAsync("id1", factory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
            Assert.That(IsInMemory(Key("id1")), Is.False);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_DistributedCacheFailing_StillLoadsAndCachesInMemory()
    {
        CacheService<TestItem> sut = CreateService();
        _fakeDistCache.FailWith = new InvalidOperationException("redis down");
        TestItem created = Item("Alice");

        TestItem? result = await sut.GetOrCreateAsync("id1", _ => Created(created));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(created));
            Assert.That(IsInMemory(Key("id1")), Is.True);
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_MemoryOnly_Hit_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();
        await sut.SetAsync(Item("Alice"), "id1");

        ValueTask<TestItem?> task = sut.GetOrCreateAsync("id1", _ => Created(Item("Fresh")));
        bool isCompleted = task.IsCompletedSuccessfully;

        TestItem? result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result!.Name, Is.EqualTo("Alice"));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_NullFactory_Throws()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetOrCreateAsync("id1", null!).AsTask());
    }

    [Test]
    public async Task GetOrCreateAsync_NullSuffix_Throws()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetOrCreateAsync<string>(null!, _ => Created(Item("Alice"))).AsTask());
    }

    [Test]
    public async Task GetOrCreateAsync_NoTenant_Throws_AndDoesNotCallTheFactory()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null);
        int calls = 0;

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetOrCreateAsync("id1", _ =>
        {
            calls++;

            return Created(Item("Alice"));
        }).AsTask());

        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public async Task GetOrCreateAsync_GlobalScope_WithTenantId_Throws()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null, scope: CacheScopes.Global);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetOrCreateAsync("id1", _ => Created(Item("Alice")), tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task GetOrCreateAsync_CanceledToken_Throws_AndDoesNotCallTheFactory()
    {
        CacheService<TestItem> sut = CreateService();
        int calls = 0;
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrCreateAsync("id1", _ =>
        {
            calls++;

            return Created(Item("Alice"));
        }, cancellationToken: cts.Token).AsTask());

        Assert.That(calls, Is.EqualTo(0));
    }

    #endregion GetOrCreateAsync

    #region GetOrCreateItemsAsync

    [Test]
    public async Task GetOrCreateItemsAsync_PartialHit_LoadsOnlyTheMissingSuffixes_InOneCall()
    {
        CacheService<TestItem> sut = CreateService();
        TestItem existing = Item("Existing");
        await sut.SetAsync(existing, "id1");
        List<string[]> requests = [];
        string[] suffixes = ["id1", "id2", "id3"];

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) =>
        {
            requests.Add([.. requested]);

            return Found(requested.ToDictionary(s => s, s => Item(s)));
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0], Is.EquivalentTo(["id2", "id3"]));
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result["id1"], Is.SameAs(existing));
            Assert.That(result["id2"]!.Name, Is.EqualTo("id2"));
            Assert.That(result["id3"]!.Name, Is.EqualTo("id3"));
            Assert.That(_fakeDistCache.Contains(Key("id2")), Is.True);
            Assert.That(_fakeDistCache.Contains(Key("id3")), Is.True);
            Assert.That(IsInMemory(Key("id2")), Is.True);
            Assert.That(IsInMemory(Key("id3")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_OneMissing_LoadsAndStoresIt()
    {
        CacheService<TestItem> sut = CreateService();
        await sut.SetAsync(Item("Existing"), "id1");
        string[] suffixes = ["id1", "id2"];

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => Found(requested.ToDictionary(s => s, s => Item(s))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id2"]!.Name, Is.EqualTo("id2"));
            Assert.That(_fakeDistCache.Contains(Key("id2")), Is.True);
            Assert.That(IsInMemory(Key("id2")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_AllCached_DoesNotCallTheFactory()
    {
        CacheService<TestItem> sut = CreateService();
        await sut.SetAsync(Item("One"), "id1");
        await sut.SetAsync(Item("Two"), "id2");
        int calls = 0;
        string[] suffixes = ["id1", "id2"];

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) =>
        {
            calls++;

            return Found([]);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(0));
            Assert.That(result["id1"]!.Name, Is.EqualTo("One"));
            Assert.That(result["id2"]!.Name, Is.EqualTo("Two"));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_EmptySuffixes_ReturnsEmpty_WithoutCallingTheFactory()
    {
        CacheService<TestItem> sut = CreateService();
        int calls = 0;

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(Array.Empty<string>(), (IReadOnlyCollection<string> requested, CancellationToken _) =>
        {
            calls++;

            return Found([]);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Empty);
            Assert.That(calls, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_DuplicateSuffixes_AreLoadedOnce()
    {
        CacheService<TestItem> sut = CreateService();
        List<string[]> requests = [];
        string[] suffixes = ["id1", "id1", "id2"];

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) =>
        {
            requests.Add([.. requested]);

            return Found(requested.ToDictionary(s => s, s => Item(s)));
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0], Is.EquivalentTo(["id1", "id2"]));
            Assert.That(result, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_SuffixTheFactoryDoesNotReturn_IsNullAndNotCached_AndIsLoadedAgainNextTime()
    {
        CacheService<TestItem> sut = CreateService();
        List<string[]> requests = [];
        string[] suffixes = ["id1", "id2"];

        Func<IReadOnlyCollection<string>, CancellationToken, ValueTask<IReadOnlyDictionary<string, TestItem>>> factory = (requested, _) =>
        {
            requests.Add([.. requested]);

            return Found(requested.Where(s => s == "id1").ToDictionary(s => s, s => Item(s)));
        };

        IReadOnlyDictionary<string, TestItem?> first = await sut.GetOrCreateItemsAsync(suffixes, factory);
        IReadOnlyDictionary<string, TestItem?> second = await sut.GetOrCreateItemsAsync(suffixes, factory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Count.EqualTo(2));
            Assert.That(first["id1"], Is.Not.Null);
            Assert.That(first["id2"], Is.Null);
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.True);
            Assert.That(_fakeDistCache.Contains(Key("id2")), Is.False);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1], Is.EquivalentTo(["id2"]));
            Assert.That(second["id2"], Is.Null);
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_FactoryReturnsNull_Throws_AndNothingIsCached()
    {
        CacheService<TestItem> sut = CreateService();
        string[] suffixes = ["id1", "id2"];

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => ValueTask.FromResult<IReadOnlyDictionary<string, TestItem>>(null!)).AsTask());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
            Assert.That(_fakeDistCache.Contains(Key("id2")), Is.False);
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_FactoryThrows_Throws_NothingIsCached_AndTheNextCallRetries()
    {
        CacheService<TestItem> sut = CreateService();
        string[] suffixes = ["id1", "id2"];

        InvalidOperationException exception = (await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => throw new InvalidOperationException("boom")).AsTask()))!;

        Assert.That(exception.Message, Is.EqualTo("boom"));
        Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);

        IReadOnlyDictionary<string, TestItem?> retry = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => Found(requested.ToDictionary(s => s, s => Item(s))));

        Assert.That(retry, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetOrCreateItemsAsync_OverlappingCallers_LoadEachSuffixOnce()
    {
        CacheService<TestItem> sut = CreateService();
        GatedBatchFactory factory = new();
        TestItem itemA = Item("A");
        TestItem itemB = Item("B");
        TestItem itemC = Item("C");
        string[] first = ["a", "b"];
        string[] second = ["b", "c"];

        Task<IReadOnlyDictionary<string, TestItem?>> callerOne = sut.GetOrCreateItemsAsync(first, factory.InvokeAsync).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        Task<IReadOnlyDictionary<string, TestItem?>> callerTwo = sut.GetOrCreateItemsAsync(second, factory.InvokeAsync).AsTask();

        await WaitUntilAsync(() => factory.Calls == 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.RequestOf(0), Is.EquivalentTo(["a", "b"]));
            Assert.That(factory.RequestOf(1), Is.EquivalentTo(["c"]));
        }

        factory.Release(0, new Dictionary<string, TestItem> { ["a"] = itemA, ["b"] = itemB });
        factory.Release(1, new Dictionary<string, TestItem> { ["c"] = itemC });

        IReadOnlyDictionary<string, TestItem?> resultOne = await WithinAsync(callerOne);
        IReadOnlyDictionary<string, TestItem?> resultTwo = await WithinAsync(callerTwo);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultOne["a"], Is.SameAs(itemA));
            Assert.That(resultOne["b"], Is.SameAs(itemB));
            Assert.That(resultTwo["b"], Is.SameAs(itemB));
            Assert.That(resultTwo["c"], Is.SameAs(itemC));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_CallersWithTheSameSuffixes_CallTheFactoryOnce()
    {
        CacheService<TestItem> sut = CreateService();
        GatedBatchFactory factory = new();
        TestItem itemA = Item("A");
        TestItem itemB = Item("B");
        string[] suffixes = ["a", "b"];

        Task<IReadOnlyDictionary<string, TestItem?>>[] callers = [.. Enumerable.Range(0, 10).Select(_ => sut.GetOrCreateItemsAsync(suffixes, factory.InvokeAsync).AsTask())];

        await WaitUntilAsync(() => factory.Calls == 1);

        factory.Release(0, new Dictionary<string, TestItem> { ["a"] = itemA, ["b"] = itemB });

        IReadOnlyDictionary<string, TestItem?>[] results = await WithinAsync(Task.WhenAll(callers));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Calls, Is.EqualTo(1));
            Assert.That(results.Select(s => s["a"]), Has.All.SameAs(itemA));
            Assert.That(results.Select(s => s["b"]), Has.All.SameAs(itemB));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_SharesAnInFlightSingleLoad()
    {
        CacheService<TestItem> sut = CreateService();
        GatedFactory single = new();
        GatedBatchFactory batch = new();
        TestItem itemA = Item("A");
        TestItem itemB = Item("B");
        string[] suffixes = ["a", "b"];

        Task<TestItem?> singleCaller = sut.GetOrCreateAsync("a", single.InvokeAsync).AsTask();

        await WaitUntilAsync(() => single.Calls == 1);

        Task<IReadOnlyDictionary<string, TestItem?>> batchCaller = sut.GetOrCreateItemsAsync(suffixes, batch.InvokeAsync).AsTask();

        await WaitUntilAsync(() => batch.Calls == 1);

        Assert.That(batch.RequestOf(0), Is.EquivalentTo(["b"]));

        single.Release(0, itemA);
        batch.Release(0, new Dictionary<string, TestItem> { ["b"] = itemB });

        IReadOnlyDictionary<string, TestItem?> result = await WithinAsync(batchCaller);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await WithinAsync(singleCaller), Is.SameAs(itemA));
            Assert.That(result["a"], Is.SameAs(itemA));
            Assert.That(result["b"], Is.SameAs(itemB));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_OneCallerCancels_DoesNotCancelTheFactory_TheLastOneDoes()
    {
        CacheService<TestItem> sut = CreateService();
        GatedBatchFactory factory = new(observeToken: true);
        using CancellationTokenSource firstCts = new();
        using CancellationTokenSource secondCts = new();
        string[] suffixes = ["a"];

        Task<IReadOnlyDictionary<string, TestItem?>> first = sut.GetOrCreateItemsAsync(suffixes, factory.InvokeAsync, cancellationToken: firstCts.Token).AsTask();
        Task<IReadOnlyDictionary<string, TestItem?>> second = sut.GetOrCreateItemsAsync(suffixes, factory.InvokeAsync, cancellationToken: secondCts.Token).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        await firstCts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => first);

        Assert.That(factory.WasCanceled(0), Is.False);

        await secondCts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => second);
        await WaitUntilAsync(() => factory.WasCanceled(0));
    }

    [Test]
    public async Task GetOrCreateItemsAsync_AbandonedLoad_IsNotJoined_ANewCallerStartsItsOwnLoad()
    {
        CacheService<TestItem> sut = CreateService();
        GatedBatchFactory factory = new();
        TestItem abandoned = Item("Abandoned");
        TestItem fresh = Item("Fresh");
        using CancellationTokenSource cts = new();
        string[] suffixes = ["a"];

        Task<IReadOnlyDictionary<string, TestItem?>> leaving = sut.GetOrCreateItemsAsync(suffixes, factory.InvokeAsync, cancellationToken: cts.Token).AsTask();

        await WaitUntilAsync(() => factory.Calls == 1);

        await cts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => leaving);
        await WaitUntilAsync(() => factory.WasCanceled(0));

        Task<IReadOnlyDictionary<string, TestItem?>> newCaller = sut.GetOrCreateItemsAsync(suffixes, factory.InvokeAsync).AsTask();

        await WaitUntilAsync(() => factory.Calls == 2);

        factory.Release(1, new Dictionary<string, TestItem> { ["a"] = fresh });

        IReadOnlyDictionary<string, TestItem?> result = await WithinAsync(newCaller);

        Assert.That(result["a"], Is.SameAs(fresh));

        factory.Release(0, new Dictionary<string, TestItem> { ["a"] = abandoned });
    }

    [Test]
    public async Task GetOrCreateItemsAsync_BothLayersDisabled_CallsTheFactoryWithEverySuffix()
    {
        CacheService<TestItem> sut = CreateBothDisabledService();
        List<string[]> requests = [];
        string[] suffixes = ["id1", "id2"];

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) =>
        {
            requests.Add([.. requested]);

            return Found(requested.Where(s => s == "id1").ToDictionary(s => s, s => Item(s)));
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0], Is.EquivalentTo(["id1", "id2"]));
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result["id1"], Is.Not.Null);
            Assert.That(result["id2"], Is.Null);
            Assert.That(_fakeDistCache.Contains(Key("id1")), Is.False);
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_DistributedCacheFailing_StillLoadsAndCachesInMemory()
    {
        CacheService<TestItem> sut = CreateService();
        _fakeDistCache.FailWith = new InvalidOperationException("redis down");
        string[] suffixes = ["id1", "id2"];

        IReadOnlyDictionary<string, TestItem?> result = await sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => Found(requested.ToDictionary(s => s, s => Item(s))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["id1"]!.Name, Is.EqualTo("id1"));
            Assert.That(result["id2"]!.Name, Is.EqualTo("id2"));
            Assert.That(IsInMemory(Key("id1")), Is.True);
            Assert.That(IsInMemory(Key("id2")), Is.True);
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_MemoryOnly_AllCached_CompletesSynchronously()
    {
        CacheService<TestItem> sut = CreateMemoryOnlyService();
        await sut.SetAsync(Item("One"), "id1");
        await sut.SetAsync(Item("Two"), "id2");
        string[] suffixes = ["id1", "id2"];

        ValueTask<IReadOnlyDictionary<string, TestItem?>> task = sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => Found([]));
        bool isCompleted = task.IsCompletedSuccessfully;

        IReadOnlyDictionary<string, TestItem?> result = await task;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isCompleted, Is.True);
            Assert.That(result, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetOrCreateItemsAsync_NullSuffixes_Throws()
    {
        CacheService<TestItem> sut = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetOrCreateItemsAsync<string>(null!, (IReadOnlyCollection<string> requested, CancellationToken _) => Found([])).AsTask());
    }

    [Test]
    public async Task GetOrCreateItemsAsync_NullFactory_Throws()
    {
        CacheService<TestItem> sut = CreateService();
        string[] suffixes = ["id1"];

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetOrCreateItemsAsync(suffixes, null!).AsTask());
    }

    [Test]
    public async Task GetOrCreateItemsAsync_BlankSuffix_Throws_AndDoesNotCallTheFactory()
    {
        CacheService<TestItem> sut = CreateService();
        int calls = 0;
        string[] suffixes = ["id1", " "];

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) =>
        {
            calls++;

            return Found([]);
        }).AsTask());

        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public async Task GetOrCreateItemsAsync_TwoSuffixesWithTheSameStringRepresentation_Throws()
    {
        CacheService<TestItem> sut = CreateService();
        FixedSuffix[] suffixes = [new FixedSuffix("x"), new FixedSuffix("x")];

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<FixedSuffix> requested, CancellationToken _) => ValueTask.FromResult<IReadOnlyDictionary<FixedSuffix, TestItem>>(new Dictionary<FixedSuffix, TestItem>())).AsTask());
    }

    [Test]
    public async Task GetOrCreateItemsAsync_GlobalScope_WithTenantId_Throws()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null, scope: CacheScopes.Global);
        string[] suffixes = ["id1"];

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => Found([]), tenantId: "tenant-a").AsTask());
    }

    [Test]
    public async Task GetOrCreateItemsAsync_NoTenant_Throws()
    {
        CacheService<TestItem> sut = CreateService(tenantId: null);
        string[] suffixes = ["id1"];

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetOrCreateItemsAsync(suffixes, (IReadOnlyCollection<string> requested, CancellationToken _) => Found([])).AsTask());
    }

    #endregion GetOrCreateItemsAsync
}