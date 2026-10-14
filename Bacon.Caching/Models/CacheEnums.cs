namespace Bacon.Caching.Models;

/// <summary>
/// Contains the distributed cache types.
/// </summary>
public enum DistributedCacheTypes
{
    /// <summary>
    /// No distributed cache. Only the memory cache is used.
    /// </summary>
    none,

    /// <summary>
    /// Redis distributed cache, in addition to the memory cache.
    /// </summary>
    redis
}
