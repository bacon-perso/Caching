using Microsoft.Extensions.DependencyInjection;

namespace Bacon.Caching.Models;

/// <summary>
/// Builder used to register the cached types.
/// </summary>
public sealed class CacheBuilder(IServiceCollection services)
{
    /// <summary>
    /// The service collection the cache is registered in.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Contains the unique list of cached types
    /// </summary>
    internal HashSet<Type> CachedTypes { get; set; } = [];
}
