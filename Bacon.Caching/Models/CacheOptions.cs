namespace Bacon.Caching.Models;

/// <summary>
/// Per cache entity options
/// </summary>
/// <typeparam name="TItem"></typeparam>
public sealed class CacheOptions<TItem>
{
    /// <summary>
    /// Prefix used for the typed object
    /// </summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>
    /// Memory cache duration
    /// </summary>
    public TimeSpan MemoryCacheDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Distributed cache duration
    /// </summary>
    public TimeSpan DistributedCacheDuration { get; set; } = TimeSpan.Zero;
}