using Bacon.Caching.Interfaces.Models;
using Bacon.Caching.Models;
using Microsoft.Extensions.Logging;

namespace Bacon.Caching.Redis.Models;

/// <summary>
/// Defines the settings of the Redis distributed cache.
/// </summary>
public sealed class RedisCacheOptions : IDistributedCacheOptions
{
    /// <summary>
    /// Logger factory passed to the Redis connection to log its events, such as disconnects, reconnects and server errors. If null (default), no connection events are logged.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// The Redis topology. Standalone (default) for a single server or a primary/replica pair, Cluster for Redis Cluster.
    /// </summary>
    public RedisServerTypes ServerType { get; set; } = RedisServerTypes.Standalone;

    /// <summary>
    /// The Redis server host name or IP address, without the port. Required, cannot be empty or whitespace.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// The Redis server port (default value 6379). Cannot be 0.
    /// </summary>
    public ushort Port { get; set; } = 6379;

    /// <summary>
    /// The Redis server password, or null if none.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Whether to use TLS/SSL for the connection (default value false).
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Additional endpoints (seed nodes) of the cluster, next to <see cref="Server"/> and <see cref="Port"/>. If null (default) or empty, only the main server is used.
    /// They let the connection start when the main server is down; the other nodes of the cluster are discovered from any reachable seed. Only allowed when <see cref="ServerType"/> is <see cref="RedisServerTypes.Cluster"/>.
    /// An endpoint without a port uses <see cref="Port"/>.
    /// </summary>
    public ICollection<RedisEndpoint>? AdditionalEndpoints { get; set; }

    /// <summary>
    /// Maximum number of keys read per Redis command when getting several items (default value 100, valid range 1 to 5000). With <see cref="RedisServerTypes.Cluster"/> a command only holds keys of the same hash slot, so it can hold fewer keys. At most twice this number of keys are in flight at a time.
    /// </summary>
    public ushort GetBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of keys deleted per Redis command when removing several items (default value 100, valid range 1 to 5000). With <see cref="RedisServerTypes.Cluster"/> a command only holds keys of the same hash slot, so it can hold fewer keys. At most twice this number of keys are in flight at a time.
    /// </summary>
    public ushort RemoveBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of items written per batch when setting several items (default value 100, valid range 1 to 5000). Each item is written with its own command; at most twice this number of items are in flight at a time.
    /// </summary>
    public ushort InsertBatchSize { get; set; } = 100;

    /// <summary>
    /// Time (ms) to allow for Redis operations. Applied to both synchronous and asynchronous operations (default value 5000).
    /// </summary>
    public int SyncTimeout { get; set; } = 5000; //https://stackexchange.github.io/StackExchange.Redis/Configuration.html#configuration-options

    /// <summary>
    /// The health check options. If null (default), no health check is registered. When set, a Redis health check is registered; the values left unset default to the name "redis", the failure status <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>, the tags ["Cache"] and no timeout.
    /// </summary>
    public DistributedHealthCheckOptions? HealthCheckOptions { get; set; }
}