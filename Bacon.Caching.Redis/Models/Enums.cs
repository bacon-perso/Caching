namespace Bacon.Caching.Redis.Models;

/// <summary>
/// The Redis topology. Standalone for a single server or a primary/replica pair, Cluster for Redis Cluster.
/// </summary>
public enum RedisServerTypes
{
    /// <summary>
    /// Single server or a primary/replica pair
    /// </summary>
    Standalone,

    /// <summary>
    /// Redis Cluster
    /// </summary>
    Cluster
}