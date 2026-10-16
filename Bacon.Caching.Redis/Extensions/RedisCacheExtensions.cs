using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Redis.Models;
using Bacon.Caching.Redis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Bacon.Caching.Redis.Extensions;

/// <summary>
/// Extensions registering Redis as the distributed cache.
/// </summary>
public static partial class RedisCacheExtensions
{
    private static readonly ushort _maxBatchSize = 5_000;

    /// <summary>
    /// Registers Redis as the distributed cache of the library.
    /// </summary>
    /// <param name="cacheBuilder">The cache builder returned by <c>AddCache</c>.</param>
    /// <param name="configureOptions">The action configuring the Redis options.</param>
    /// <returns>The cache builder.</returns>
    /// <remarks>
    /// The connection is created during the registration. If Redis is unreachable the application still starts, but the connection retries can make the startup slightly longer.
    /// The failure is logged when a <see cref="RedisCacheOptions.LoggerFactory"/> is configured.
    /// Only one distributed cache provider can be registered.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The cache builder or the configure options action is null.</exception>
    /// <exception cref="InvalidOperationException">A distributed cache provider is already registered.</exception>
    /// <exception cref="ArgumentException">The server is empty or whitespace, or it contains a port number.</exception>
    /// <exception cref="ArgumentException">The port number is 0.</exception>
    /// <exception cref="ArgumentException">The additional endpoints are set with a server type other than Cluster, or one of them is null, has an empty or whitespace server, a server containing a port number, or a port number of 0.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The get, remove or insert batch size is 0 or bigger than 5000.</exception>
    public static CacheBuilder AddRedis(this CacheBuilder cacheBuilder, Action<RedisCacheOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(cacheBuilder, nameof(cacheBuilder));
        ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));

        if (cacheBuilder.Services.Any(s => s.ServiceType == typeof(IDistributedCacheService<>)))
        {
            throw new InvalidOperationException("Caching - A distributed cache provider is already registered.");
        }

        RedisCacheOptions redisCacheOptions = new();
        configureOptions(redisCacheOptions);

        ValidateRedisCacheOptions(redisCacheOptions);

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
            LoggerFactory = redisCacheOptions.LoggerFactory
        };

        if (redisCacheOptions.AdditionalEndpoints is not null)
        {
            foreach (RedisEndpoint redisEndpoint in redisCacheOptions.AdditionalEndpoints)
            {
                configurationOptions.EndPoints.Add(redisEndpoint.Server, redisEndpoint.Port ?? redisCacheOptions.Port);
            }
        }

        //creating the connection multiplexer
        ConnectionMultiplexer connectionMultiplexer = ConnectionMultiplexer.Connect(configurationOptions);

        CheckServerType(connectionMultiplexer, redisCacheOptions);

        // the wrapper is registered with a factory so the container disposes it (and the connection) when the host stops
        RedisCacheConnection redisCacheConnection = new(connectionMultiplexer, connectionMultiplexer.GetDatabase());
        cacheBuilder.Services.AddSingleton(_ => redisCacheConnection);

        if (redisCacheOptions.HealthCheckOptions != null)
        {
            cacheBuilder.Services.AddHealthChecks()
                .AddRedis(
                    connectionMultiplexer,
                    string.IsNullOrWhiteSpace(redisCacheOptions.HealthCheckOptions.Name) ? "redis" : redisCacheOptions.HealthCheckOptions.Name,
                    failureStatus: redisCacheOptions.HealthCheckOptions.FailureStatus ?? HealthStatus.Degraded,
                    redisCacheOptions.HealthCheckOptions.Tags ?? ["Cache"],
                    redisCacheOptions.HealthCheckOptions.Timeout
                );
        }

        Type distributedCacheServiceType = redisCacheOptions.ServerType == RedisServerTypes.Cluster ? typeof(ClusterCacheService<>) : typeof(StandaloneCacheService<>);

        cacheBuilder.Services.AddSingleton(typeof(IDistributedCacheService<>), distributedCacheServiceType);

        cacheBuilder.Services.AddSingleton(Options.Create(redisCacheOptions));

        return cacheBuilder;
    }

    #region Diagnostics

    /// <summary>
    /// Compares the configured <see cref="RedisCacheOptions.ServerType"/> with the type reported by the connected servers and logs a warning when they differ.
    /// It only logs: nothing is changed, and nothing is checked when no logger factory is configured or no server is connected yet.
    /// </summary>
    private static void CheckServerType(ConnectionMultiplexer connectionMultiplexer, RedisCacheOptions redisCacheOptions)
    {
        if (redisCacheOptions.LoggerFactory is null)
        {
            return;
        }

        IServer[] connectedServers = [.. connectionMultiplexer.GetServers().Where(s => s.IsConnected)];
        if (connectedServers.Length == 0)
        {
            return;
        }

        bool isCluster = connectedServers.Any(s => s.ServerType == StackExchange.Redis.ServerType.Cluster);
        ILogger logger = redisCacheOptions.LoggerFactory.CreateLogger(typeof(RedisCacheExtensions).FullName!);

        if (isCluster && redisCacheOptions.ServerType == RedisServerTypes.Standalone)
        {
            LogClusterConfiguredAsStandalone(logger);
        }
        else if (!isCluster && redisCacheOptions.ServerType == RedisServerTypes.Cluster)
        {
            LogStandaloneConfiguredAsCluster(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Caching - The Redis server is a cluster but the ServerType is Standalone. Reading or removing several items fails when their keys are in different hash slots. Set the ServerType to Cluster.")]
    private static partial void LogClusterConfiguredAsStandalone(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Caching - The ServerType is Cluster but the Redis server is not a cluster. It works, but adds a small overhead when several items are read or removed. Set the ServerType to Standalone.")]
    private static partial void LogStandaloneConfiguredAsCluster(ILogger logger);

    #endregion Diagnostics

    #region Validations

    private static void ValidateRedisCacheOptions(RedisCacheOptions redisCacheOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redisCacheOptions.Server, nameof(redisCacheOptions.Server));

        if (!Enum.IsDefined(redisCacheOptions.ServerType))
        {
            throw new ArgumentException($"Caching - The {nameof(redisCacheOptions.ServerType)} is not a valid value ({redisCacheOptions.ServerType}).");
        }

        if (redisCacheOptions.Server.Contains(':'))
        {
            throw new ArgumentException("Caching - The server cannot contain a port number.", nameof(redisCacheOptions));
        }

        if (redisCacheOptions.Port == 0)
        {
            throw new ArgumentException("Caching - The port number cannot be 0.", nameof(redisCacheOptions));
        }

        if (redisCacheOptions.AdditionalEndpoints is { Count: > 0 })
        {
            if (redisCacheOptions.ServerType != RedisServerTypes.Cluster)
            {
                throw new ArgumentException($"Caching - The additional endpoints require the {nameof(redisCacheOptions.ServerType)} '{RedisServerTypes.Cluster}'.", nameof(redisCacheOptions));
            }

            foreach (RedisEndpoint redisEndpoint in redisCacheOptions.AdditionalEndpoints)
            {
                if (redisEndpoint is null)
                {
                    throw new ArgumentException("Caching - The additional endpoints cannot contain a null endpoint.", nameof(redisCacheOptions));
                }

                if (string.IsNullOrWhiteSpace(redisEndpoint.Server))
                {
                    throw new ArgumentException("Caching - The server of an additional endpoint cannot be null, empty or whitespace.", nameof(redisCacheOptions));
                }

                if (redisEndpoint.Server.Contains(':'))
                {
                    throw new ArgumentException("Caching - The server of an additional endpoint cannot contain a port number.", nameof(redisCacheOptions));
                }

                if (redisEndpoint.Port == 0)
                {
                    throw new ArgumentException("Caching - The port number of an additional endpoint cannot be 0.", nameof(redisCacheOptions));
                }
            }
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

    #endregion Validations
}