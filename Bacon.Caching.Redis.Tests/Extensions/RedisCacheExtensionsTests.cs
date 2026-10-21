using Bacon.Caching.Extensions;
using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Redis.Extensions;
using Bacon.Caching.Redis.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace Bacon.Caching.Redis.Tests.Extensions;

/// <summary>
/// None of these tests uses valid Redis options: a valid configuration would try to connect to a server.
/// </summary>
[TestFixture]
internal sealed class RedisCacheExtensionsTests
{
    private sealed class ProviderStub<TItem> : IDistributedCacheService<TItem>
    {
        public Task<TItem?> GetAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Dictionary<string, TItem?>> GetItemsAsync(ReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetAsync(string key, TItem value, TimeSpan cacheDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetItemsAsync(IReadOnlyDictionary<string, TItem> items, TimeSpan cacheDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveItemsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static CacheBuilder CreateBuilder()
    {
        ServiceCollection services = new();

        return services.AddCache(o => { });
    }

    #region Arguments

    [Test]
    public void AddRedis_NullBuilder_ThrowsArgumentNullException()
    {
        CacheBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.AddRedis(o => o.Server = "localhost"));
    }

    [Test]
    public void AddRedis_NullConfigureOptions_ThrowsArgumentNullException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.AddRedis(null!));
    }

    #endregion

    #region Provider

    [Test]
    public void AddRedis_ProviderAlreadyRegistered_ThrowsInvalidOperationExceptionBeforeConfiguring()
    {
        ServiceCollection services = new();
        CacheBuilder builder = services.AddCache(o => { });
        services.AddSingleton(typeof(IDistributedCacheService<>), typeof(ProviderStub<>));

        bool configured = false;

        Assert.Throws<InvalidOperationException>(() => builder.AddRedis(o =>
        {
            configured = true;
            o.Server = "localhost";
        }));

        Assert.That(configured, Is.False);
    }

    #endregion

    #region Options

    [Test]
    public void AddRedis_ServerEmpty_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o => { }));
    }

    [Test]
    public void AddRedis_ServerWhitespace_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o => o.Server = "  "));
    }

    [Test]
    public void AddRedis_ServerContainsPort_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o => o.Server = "localhost:6379"));
    }

    [Test]
    public void AddRedis_PortZero_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.Port = 0;
        }));
    }

    [Test]
    public void AddRedis_UndefinedServerType_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = (RedisServerTypes)99;
        }));
    }

    [Test]
    public void AddRedis_GetBatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        CacheBuilder builder = CreateBuilder();

        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.GetBatchSize = 0;
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(RedisCacheOptions.GetBatchSize)));
            Assert.That(exception.Message, Does.Contain(nameof(RedisCacheOptions.GetBatchSize)));
        };
    }

    [Test]
    public void AddRedis_GetBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.GetBatchSize = 5001;
        }));
    }

    [Test]
    public void AddRedis_RemoveBatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        CacheBuilder builder = CreateBuilder();

        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.RemoveBatchSize = 0;
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(RedisCacheOptions.RemoveBatchSize)));
            Assert.That(exception.Message, Does.Contain(nameof(RedisCacheOptions.RemoveBatchSize)));
        };
    }

    [Test]
    public void AddRedis_RemoveBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.RemoveBatchSize = 5001;
        }));
    }

    [Test]
    public void AddRedis_InsertBatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        CacheBuilder builder = CreateBuilder();

        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.InsertBatchSize = 0;
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(RedisCacheOptions.InsertBatchSize)));
            Assert.That(exception.Message, Does.Contain(nameof(RedisCacheOptions.InsertBatchSize)));
        };
    }

    [Test]
    public void AddRedis_InsertBatchSizeTooLarge_ThrowsArgumentOutOfRangeException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.InsertBatchSize = 5001;
        }));
    }

    [Test]
    public void AddRedis_AdditionalEndpointsWithStandalone_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = RedisServerTypes.Standalone;
            o.AdditionalEndpoints = [new RedisEndpoint { Server = "localhost2" }];
        }));
    }

    [Test]
    public void AddRedis_AdditionalEndpointNull_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = RedisServerTypes.Cluster;
            o.AdditionalEndpoints = [null!];
        }));
    }

    [Test]
    public void AddRedis_AdditionalEndpointServerEmpty_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = RedisServerTypes.Cluster;
            o.AdditionalEndpoints = [new RedisEndpoint()];
        }));
    }

    [Test]
    public void AddRedis_AdditionalEndpointServerWhitespace_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = RedisServerTypes.Cluster;
            o.AdditionalEndpoints = [new RedisEndpoint { Server = "  " }];
        }));
    }

    [Test]
    public void AddRedis_AdditionalEndpointServerContainsPort_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = RedisServerTypes.Cluster;
            o.AdditionalEndpoints = [new RedisEndpoint { Server = "localhost2:6379" }];
        }));
    }

    [Test]
    public void AddRedis_AdditionalEndpointPortZero_ThrowsArgumentException()
    {
        CacheBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddRedis(o =>
        {
            o.Server = "localhost";
            o.ServerType = RedisServerTypes.Cluster;
            o.AdditionalEndpoints = [new RedisEndpoint { Server = "localhost2", Port = 0 }];
        }));
    }

    #endregion
}