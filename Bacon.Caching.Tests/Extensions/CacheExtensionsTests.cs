using Bacon.Caching.Extensions;
using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Tests.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.Caching.Tests.Extensions;

[TestFixture]
internal sealed class CacheExtensionsTests
{
    #region AddCache

    [Test]
    public void AddCache_MemoryOnly_RegistersCacheService()
    {
        ServiceCollection services = new();
        services.AddCache(o => o.TenantId = "tenant")
                .AddItem<TestItem>(o =>
                {
                    o.CacheKey = "test_";
                    o.MemoryCacheDuration = TimeSpan.FromMinutes(1);
                });

        // ICacheService<> is registered as an open-generic singleton (lazy) — check the descriptor directly rather than resolving, which would require ILogger<T>.
        Assert.That(services.Any(s => s.ServiceType == typeof(ICacheService<>)), Is.True);
    }

    [Test]
    public void AddCache_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CacheExtensions.AddCache(null!, o => o.TenantId = "tenant"));
    }

    [Test]
    public void AddCache_NullConfigureOptions_ThrowsArgumentNullException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentNullException>(() => services.AddCache(null!));
    }

    [Test]
    public void AddCache_InvalidTenantIdCharacters_ThrowsArgumentException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o => o.TenantId = "invalid tenant!")
        );
    }

    [Test]
    public void AddCache_TenantIdTooLong_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        string tenantId = new('a', 51);

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o => o.TenantId = tenantId)
        );
    }

    [Test]
    public void AddCache_NullTenantId_DoesNotThrow()
    {
        ServiceCollection services = new();

        Assert.DoesNotThrow(() => services.AddCache(o => o.TenantId = null));
    }

    #endregion

    #region AddItem

    [Test]
    public void AddItem_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CacheExtensions.AddItem<TestItem>(null!, o => o.CacheKey = "key_"));
    }

    [Test]
    public void AddItem_NullConfigureOptions_ThrowsArgumentNullException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentNullException>(() => builder.AddItem<TestItem>(null!));
    }

    [Test]
    public void AddItem_SameTypeTwice_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant")
            .AddItem<TestItem>(o => { o.CacheKey = "key_"; o.MemoryCacheDuration = TimeSpan.FromMinutes(1); });

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = "key2_"; o.MemoryCacheDuration = TimeSpan.FromMinutes(1); })
        );
    }

    [Test]
    public void AddItem_EmptyCacheKey_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => o.MemoryCacheDuration = TimeSpan.FromMinutes(1))
        );
    }

    [Test]
    public void AddItem_WhitespaceCacheKey_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = "   "; o.MemoryCacheDuration = TimeSpan.FromMinutes(1); })
        );
    }

    [Test]
    public void AddItem_CacheKeyTooLong_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");
        string cacheKey = new('a', 51);

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = cacheKey; o.MemoryCacheDuration = TimeSpan.FromMinutes(1); })
        );
    }

    [Test]
    public void AddItem_CacheKeyOfMaximumLength_DoesNotThrow()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");
        string cacheKey = new('a', 50);

        Assert.DoesNotThrow(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = cacheKey; o.MemoryCacheDuration = TimeSpan.FromMinutes(1); })
        );
    }

    [Test]
    public void AddItem_UndefinedScope_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = "key_"; o.Scope = (CacheScopes)99; })
        );
    }

    [Test]
    public void AddItem_GlobalScope_DoesNotThrow()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => { });

        Assert.DoesNotThrow(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = "key_"; o.Scope = CacheScopes.Global; })
        );
    }

    [Test]
    public void AddItem_NegativeMemoryCacheDuration_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = "key_"; o.MemoryCacheDuration = TimeSpan.FromMinutes(-1); })
        );
    }

    [Test]
    public void AddItem_NegativeDistributedCacheDuration_ThrowsArgumentException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentException>(() =>
            builder.AddItem<TestItem>(o => { o.CacheKey = "key_"; o.DistributedCacheDuration = TimeSpan.FromMinutes(-1); })
        );
    }

    #endregion
}