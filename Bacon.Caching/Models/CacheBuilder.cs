using Microsoft.Extensions.DependencyInjection;

namespace Bacon.Caching.Models;

/// <summary>
/// Cache builder
/// </summary>
public sealed class CacheBuilder(IServiceCollection services)
{
    /// <summary>
    /// Service collection
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Contains the unique list of cached types
    /// </summary>
    internal HashSet<Type> CachedTypes { get; set; } = [];
}