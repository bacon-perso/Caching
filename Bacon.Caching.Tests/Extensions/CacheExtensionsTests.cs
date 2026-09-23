using Bacon.Caching.Extensions;
using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Tests.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.Caching.Tests.Extensions;

[TestFixture]
internal sealed class CacheExtensionsTests
{
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

        ServiceProvider provider = services.BuildServiceProvider();

        // ICacheService<> is registered as an open-generic singleton (lazy) — check the descriptor directly rather than resolving, which would require ILogger<T>.
        Assert.That(services.Any(s => s.ServiceType == typeof(ICacheService<>)), Is.True);
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
    public void AddItem_EmptyCacheKey_ThrowsArgumentNullException()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => o.TenantId = "tenant");

        Assert.Throws<ArgumentNullException>(() =>
            builder.AddItem<TestItem>(o => o.MemoryCacheDuration = TimeSpan.FromMinutes(1))
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

    [Test]
    public void AddCache_RedisServerContainsPort_ThrowsArgumentException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o =>
            {
                o.DistributedCacheType = DistributedCacheTypes.redis;
                o.DistributedCacheOptions = new RedisCacheOptions { Server = "localhost:6379" };
            })
        );
    }

    [Test]
    public void AddCache_RedisPortZero_ThrowsArgumentException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o =>
            {
                o.DistributedCacheType = DistributedCacheTypes.redis;
                o.DistributedCacheOptions = new RedisCacheOptions { Server = "localhost", Port = 0 };
            })
        );
    }

    [Test]
    public void AddCache_RedisFlushBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o =>
            {
                o.DistributedCacheType = DistributedCacheTypes.redis;
                o.DistributedCacheOptions = new RedisCacheOptions { Server = "localhost", Port = 6379, FlushBatchSize = 8001 };
            })
        );
    }

    [Test]
    public void AddCache_RedisInsertBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o =>
            {
                o.DistributedCacheType = DistributedCacheTypes.redis;
                o.DistributedCacheOptions = new RedisCacheOptions { Server = "localhost", Port = 6379, InsertBatchSize = 5001 };
            })
        );
    }
}
