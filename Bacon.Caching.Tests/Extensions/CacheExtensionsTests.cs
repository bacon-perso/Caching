using Bacon.Caching.Extensions;
using Bacon.Caching.Interfaces.Models;
using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Tests.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.Caching.Tests.Extensions;

[TestFixture]
internal sealed class CacheExtensionsTests
{
    private sealed class OtherDistributedOptions : IDistributedCacheOptions
    {
        public string Server { get; set; } = "localhost";

        public string? Password { get; set; }
    }

    private static void ConfigureRedis(CacheServerOptions options, RedisCacheOptions redisOptions)
    {
        options.DistributedCacheType = DistributedCacheTypes.redis;
        options.DistributedCacheOptions = redisOptions;
    }

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

    #region Redis options

    [Test]
    public void AddCache_RedisWithoutDistributedCacheOptions_ThrowsArgumentNullException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddCache(o => o.DistributedCacheType = DistributedCacheTypes.redis)
        );
    }

    [Test]
    public void AddCache_RedisWithWrongOptionsType_ThrowsArgumentNullException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddCache(o =>
            {
                o.DistributedCacheType = DistributedCacheTypes.redis;
                o.DistributedCacheOptions = new OtherDistributedOptions();
            })
        );
    }

    [Test]
    public void AddCache_RedisServerWhitespace_ThrowsArgumentException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "  " }))
        );
    }

    [Test]
    public void AddCache_RedisServerContainsPort_ThrowsArgumentException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost:6379" }))
        );
    }

    [Test]
    public void AddCache_RedisPortZero_ThrowsArgumentException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 0 }))
        );
    }

    [Test]
    public void AddCache_RedisGetBatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 6379, GetBatchSize = 0 }))
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(RedisCacheOptions.GetBatchSize)));
            Assert.That(exception.Message, Does.Contain(nameof(RedisCacheOptions.GetBatchSize)));
        };
    }

    [Test]
    public void AddCache_RedisGetBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 6379, GetBatchSize = 5001 }))
        );
    }

    [Test]
    public void AddCache_RedisRemoveBatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 6379, RemoveBatchSize = 0 }))
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(RedisCacheOptions.RemoveBatchSize)));
            Assert.That(exception.Message, Does.Contain(nameof(RedisCacheOptions.RemoveBatchSize)));
        };
    }

    [Test]
    public void AddCache_RedisRemoveBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 6379, RemoveBatchSize = 5001 }))
        );
    }

    [Test]
    public void AddCache_RedisInsertBatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 6379, InsertBatchSize = 0 }))
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(RedisCacheOptions.InsertBatchSize)));
            Assert.That(exception.Message, Does.Contain(nameof(RedisCacheOptions.InsertBatchSize)));
        };
    }

    [Test]
    public void AddCache_RedisInsertBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddCache(o => ConfigureRedis(o, new RedisCacheOptions { Server = "localhost", Port = 6379, InsertBatchSize = 5001 }))
        );
    }

    #endregion
}