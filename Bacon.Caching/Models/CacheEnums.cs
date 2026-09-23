namespace Bacon.Caching.Models;

/// <summary>
/// Contains the distrubuted cache types
/// </summary>
public enum DistributedCacheTypes
{
    /// <summary>
    /// No distrubted cache. Only memory cache
    /// </summary>
    none,

    /// <summary>
    /// Redis + memory cache
    /// </summary>
    redis
}