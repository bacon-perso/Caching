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
    #region Extensions

    /// <summary>
    /// Add cache middleware
    /// </summary>
    /// <param name="services">The service collections</param>
    /// <param name="options">The cache server options</param>
    /// <exception cref="ArgumentNullException">CacheKeyPrefix cannot be null or empty.</exception>
    /// <exception cref="ArgumentException">MemoryCacheDuration cannot contain negative values.</exception>
    /// <exception cref="ArgumentException">RedisCacheDuration cannot contain negative values.</exception>
    /// <exception cref="ArgumentNullException">The server cannot be empty.</exception>
    /// <exception cref="ArgumentException">The server cannot contain a port number.</exception>
    /// <exception cref="ArgumentException">The port number cannot be 0.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The flush batch size cannot be bigger than 8000.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The insert batch size cannot be bigger than 5000.</exception>
    /// <returns>The cache builder. Needed to add individual items</returns>
    public static CacheBuilder AddCache(this IServiceCollection services, Action<CacheServerOptions> options)
    {
        CacheServerOptions cacheServerOptions = new();

        options(cacheServerOptions);

        ValidateCacheServerOptions(cacheServerOptions);

        services.AddMemoryCache();

        services.AddSingleton<ICacheSerializerService, CacheSerializerService>();

        if (!cacheServerOptions.DistributedCacheType.Equals(DistributedCacheTypes.none))
        {
            services.AddDistributedCache(cacheServerOptions);
        }

        services.AddSingleton(typeof(ICacheService<>), typeof(CacheService<>));

        services.Configure(options);

        return new(services);
    }

    /// <summary>
    /// Add Items to cache
    /// </summary>
    /// <typeparam name="TItem">Class to cache</typeparam>
    /// <param name="builder">Cache Builder</param>
    /// <param name="setupAction">CacheOptions of TItem setup action</param>
    /// <returns>CacheBuilder</returns>
    /// <exception cref="ArgumentException">The type name already exists, and cannot be configured a second time</exception>
    /// <exception cref="ArgumentNullException">The cache key cannot be null or empty.</exception>
    /// <exception cref="ArgumentException">The memory cache duration cannot contain negative values.</exception>
    /// <exception cref="ArgumentException">The redis cache duration cannot contain negative values.</exception>
    public static CacheBuilder AddItem<TItem>(this CacheBuilder builder, Action<CacheOptions<TItem>> setupAction)
    {
        //Validate options
        CacheOptions<TItem> options = new();
        setupAction(options);

        ValidateCacheOptions(builder, options);

        builder.Services.Configure(setupAction);

        builder.CachedTypes.Add(typeof(TItem));

        return builder;
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
            AllowAdmin = true,
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

    private static void ValidateCacheOptions<TItem>(CacheBuilder builder, CacheOptions<TItem> cacheOptions)
    {
        Type type = typeof(TItem);

        if (builder.CachedTypes.Contains(type))
        {
            throw new ArgumentException($"Caching - The type {type.Name} already exists, and cannot be configured a second time");
        }

        if (string.IsNullOrWhiteSpace(cacheOptions.CacheKey))
        {
            throw new ArgumentNullException(null, $"Caching - The {nameof(cacheOptions.CacheKey)} cannot be null or empty.");
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

        if (redisCacheOptions.FlushBatchSize > 8000)
        {
            throw new ArgumentOutOfRangeException(nameof(distributedCacheOptions), $"Caching - The {nameof(redisCacheOptions.FlushBatchSize)} cannot be bigger than 8000.");
        }

        if (redisCacheOptions.InsertBatchSize > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(distributedCacheOptions), $"Caching - The {nameof(redisCacheOptions.InsertBatchSize)} cannot be bigger than 5000.");
        }
    }

    #endregion Redis

    #endregion Distributed Cache

    #endregion Validations
}