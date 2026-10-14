using Bacon.Caching.Interfaces.Models;

namespace Bacon.Caching.Models;

/// <summary>
/// Defines the Redis server settings.
/// </summary>
public sealed class RedisCacheOptions : IDistributedCacheOptions
{
    /// <summary>
    /// The Redis server host name or IP address, without the port. Required.
    /// </summary>
    public required string Server { get; set; }

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
    /// Maximum number of keys read per Redis command when getting several items (default value 100, valid range 1 to 5000).
    /// </summary>
    public ushort GetBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of keys deleted per Redis command when removing several items (default value 100, valid range 1 to 5000).
    /// </summary>
    public ushort RemoveBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of items written per batch when setting several items (default value 100, valid range 1 to 5000).
    /// </summary>
    public ushort InsertBatchSize { get; set; } = 100;

    /// <summary>
    /// Time (ms) to allow for Redis operations. Applied to both synchronous and asynchronous operations (default value 5000).
    /// </summary>
    public int SyncTimeout { get; set; } = 5000; //https://stackexchange.github.io/StackExchange.Redis/Configuration.html#configuration-options
}