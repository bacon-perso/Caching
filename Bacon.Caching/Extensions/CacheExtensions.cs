using Bacon.Caching.Interfaces.Models;
using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bacon.Caching.Extensions;

/// <summary>
/// Cache middleware extensions
/// </summary>
public static class CacheExtensions
{
    private static readonly ushort _maxBatchSize = 5_000;

    #region Extensions

    /// <summary>
    /// Registers the caching services: the memory cache and, when configured, the distributed cache.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The action configuring the cache server options.</param>
    /// <remarks>
    /// When a distributed cache is configured, the connection is created during the registration. If it is unreachable the application still starts, but the connection retries can make the startup slightly longer.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The configure options action is null.</exception>
    /// <exception cref="ArgumentException">The global tenant ID is invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <exception cref="ArgumentNullException">A distributed cache type is selected but the distributed cache options are missing or are not of the expected type (RedisCacheOptions for redis).</exception>
    /// <exception cref="ArgumentException">The server is empty or whitespace, or it contains a port number.</exception>
    /// <exception cref="ArgumentException">The port number is 0.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The get, remove or insert batch size is 0 or bigger than 5000.</exception>
    /// <returns>The cache builder, used to add the cached types with <see cref="AddItem{TItem}(CacheBuilder, Action{CacheOptions{TItem}})"/>.</returns>
    public static CacheBuilder AddCache(this IServiceCollection services, Action<CacheServerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));

        CacheServerOptions cacheServerOptions = new();
        configureOptions(cacheServerOptions);

        ValidateCacheServerOptions(cacheServerOptions);

        services.AddMemoryCache();

        services.AddSingleton<ICacheSerializerService, CacheSerializerService>();

        if (!cacheServerOptions.DistributedCacheType.Equals(DistributedCacheTypes.none))
        {
            services.AddDistributedCache(cacheServerOptions);
        }

        services.AddSingleton(typeof(ICacheService<>), typeof(CacheService<>));

        services.Configure(configureOptions);

        return new(services);
    }

    /// <summary>
    /// Registers the cache configuration of a cached type.
    /// </summary>
    /// <typeparam name="TItem">The type of the items to cache.</typeparam>
    /// <param name="cacheBuilder">The cache builder returned by <see cref="AddCache(IServiceCollection, Action{CacheServerOptions})"/>.</param>
    /// <param name="configureOptions">The action configuring the cache options of <typeparamref name="TItem"/>.</param>
    /// <returns>The cache builder.</returns>
    /// <exception cref="ArgumentNullException">The configure options action is null.</exception>
    /// <exception cref="ArgumentException">The type is already configured and cannot be configured a second time.</exception>
    /// <exception cref="ArgumentException">The cache key is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentException">The cache key is longer than 50 characters.</exception>
    /// <exception cref="ArgumentException">The memory cache duration is negative.</exception>
    /// <exception cref="ArgumentException">The distributed cache duration is negative.</exception>
    public static CacheBuilder AddItem<TItem>(this CacheBuilder cacheBuilder, Action<CacheOptions<TItem>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(cacheBuilder, nameof(cacheBuilder));
        ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));

        CacheOptions<TItem> options = new();
        configureOptions(options);

        ValidateCacheOptions(cacheBuilder, options);

        cacheBuilder.Services.Configure(configureOptions);

        cacheBuilder.CachedTypes.Add(typeof(TItem));

        return cacheBuilder;
    }

    #region Distributed Cache

    private static void AddDistributedCache(this IServiceCollection services, CacheServerOptions cacheServerOptions)
    {
        switch(cacheServerOptions.DistributedCacheType)
        {
            case DistributedCacheTypes.redis :
                RedisCacheOptions redisCacheOptions = (cacheServerOptions.DistributedCacheOptions as RedisCacheOptions)!;

                services.AddRedisCache(redisCacheOptions, cacheServerOptions.HealthCheckOptions, cacheServerOptions.LoggerFactory);
                break;

            default: return;
        }
    }

    #region Redis

    private static void AddRedisCache(this IServiceCollection services, RedisCacheOptions redisCacheOptions, DistributedHealthCheckOptions? distributedHealthCheckOptions, ILoggerFactory? loggerFactory)
    {
        ConfigurationOptions configurationOptions = new()
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            ConnectRetry = 5,
            KeepAlive = 60,
            EndPoints = {
                    {
                        redisCacheOptions.Server!,
                        redisCacheOptions.Port
                    }
                },
            ReconnectRetryPolicy = new LinearRetry(5000),
            Password = redisCacheOptions.Password,
            Ssl = redisCacheOptions.UseSsl,
            SyncTimeout = redisCacheOptions.SyncTimeout,
            AsyncTimeout = redisCacheOptions.SyncTimeout,
            LoggerFactory = loggerFactory
        };

        //creating the connection multiplexer
        ConnectionMultiplexer connectionMultiplexer = ConnectionMultiplexer.Connect(configurationOptions);
        services.AddSingleton(connectionMultiplexer);

        //creating the redis database
        IDatabase database = connectionMultiplexer.GetDatabase();
        services.AddSingleton(database);

        if (distributedHealthCheckOptions != null)
        {
            services.AddHealthChecks()
                .AddRedis(
                    connectionMultiplexer,
                    string.IsNullOrWhiteSpace(distributedHealthCheckOptions.Name) ? "redis" : distributedHealthCheckOptions.Name,
                    failureStatus: distributedHealthCheckOptions.FailureStatus ?? HealthStatus.Degraded,
                    distributedHealthCheckOptions.Tags ?? ["Cache"],
                    distributedHealthCheckOptions.Timeout
                );
        }

        services.AddSingleton(typeof(IDistributedCacheService<>), typeof(RedisCacheService<>));
    }

    #endregion Redis

    #endregion Distributed Cache

    #endregion Extensions

    #region Validations

    private static void ValidateCacheOptions<TItem>(CacheBuilder cacheBuilder, CacheOptions<TItem> cacheOptions)
    {
        Type type = typeof(TItem);

        if (cacheBuilder.CachedTypes.Contains(type))
        {
            throw new ArgumentException($"Caching - The type {type.Name} already exists, and cannot be configured a second time");
        }

        if (string.IsNullOrWhiteSpace(cacheOptions.CacheKey))
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.CacheKey)} cannot be null or empty.");
        }

        if (cacheOptions.CacheKey.Length > 50)
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.CacheKey)} of '{type.Name}' cannot be longer than 50 characters (current length: {cacheOptions.CacheKey.Length}).");
        }

        if (cacheOptions.MemoryCacheDuration.Ticks < 0)
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.MemoryCacheDuration)} cannot contain negative values.");
        }

        if (cacheOptions.DistributedCacheDuration.Ticks < 0)
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.DistributedCacheDuration)} cannot contain negative values.");
        }
    }

    #region Distributed Cache

    private static void ValidateCacheServerOptions(CacheServerOptions cacheServerOptions)
    {
        if (!string.IsNullOrEmpty(cacheServerOptions.TenantId))
        {
            TenantValidationService.ValidateTenantId(cacheServerOptions.TenantId);
        }

        if (cacheServerOptions.DistributedCacheType.Equals(DistributedCacheTypes.none))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(cacheServerOptions.DistributedCacheOptions, nameof(cacheServerOptions));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheServerOptions.DistributedCacheOptions.Server, nameof(cacheServerOptions));

        switch (cacheServerOptions.DistributedCacheType)
        {
            case DistributedCacheTypes.redis:
                ValidateRedisCacheOptions(cacheServerOptions.DistributedCacheOptions);
                break;

            default: return;
        };
    }

    #region Redis

    private static void ValidateRedisCacheOptions(IDistributedCacheOptions distributedCacheOptions)
    {
        RedisCacheOptions redisCacheOptions = distributedCacheOptions as RedisCacheOptions ?? throw new ArgumentNullException(nameof(distributedCacheOptions), "Caching - The redis cache options cannot be null when the distributed cache type is redis");

        if (redisCacheOptions.Server.Contains(':'))
        {
            throw new ArgumentException("Caching - The server cannot contain a port number.", nameof(distributedCacheOptions));
        }

        if (redisCacheOptions.Port == 0)
        {
            throw new ArgumentException("Caching - The port number cannot be 0.", nameof(distributedCacheOptions));
        }

        ValidateBatchSize(nameof(redisCacheOptions.GetBatchSize), redisCacheOptions.GetBatchSize);
        ValidateBatchSize(nameof(redisCacheOptions.InsertBatchSize), redisCacheOptions.InsertBatchSize);
        ValidateBatchSize(nameof(redisCacheOptions.RemoveBatchSize), redisCacheOptions.RemoveBatchSize);
    }

    private static void ValidateBatchSize(string propertyName, ushort batchSize)
    {
        if (batchSize == 0 || batchSize > _maxBatchSize)
        {
            throw new ArgumentOutOfRangeException(propertyName, $"Caching - The {propertyName} cannot be 0 or bigger than {_maxBatchSize}.");
        }
    }

    #endregion Redis

    #endregion Distributed Cache

    #endregion Validations
}