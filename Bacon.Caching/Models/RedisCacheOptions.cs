using Bacon.Caching.Interfaces.Models;

namespace Bacon.Caching.Models;

/// <summary>
/// Defines the redis server settings
/// </summary>
public sealed class RedisCacheOptions : IDistributedCacheOptions
{
    /// <summary>
    /// Redis Server
    /// </summary>
    public required string Server { get; set; }

    /// <summary>
    /// Redis server port (default value 6379)
    /// </summary>
    public ushort Port { get; set; } = 6379;

    /// <summary>
    /// Redis server password
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Use ssl 
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Batch size for flush (default value 1000)
    /// </summary>
    public ushort FlushBatchSize { get; set; } = 1000;

    /// <summary>
    /// Batch size for insert cache (default value 100)
    /// </summary>
    public ushort InsertBatchSize { get; set; } = 100;

    /// <summary>
    /// Time (ms) to allow for synchronous operations
    /// </summary>
    public int SyncTimeout { get; set; } = 5000; //https://stackexchange.github.io/StackExchange.Redis/Configuration.html#configuration-options
}