using Bacon.Caching.Interfaces.Models;
using Microsoft.Extensions.Logging;

namespace Bacon.Caching.Models;

/// <summary>
/// Options loading the caching middleware
/// </summary>
public sealed class CacheServerOptions
{
    /// <summary>
    /// Containes the environement instance ID. Can be passed here in a single tenant environment. Can also be overriden in each individual function in case of multi-tenancy
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The type of distrubuted cache. Defaults to none, or memory cache
    /// </summary>
    public DistributedCacheTypes DistributedCacheType { get; set; } = DistributedCacheTypes.none;

    /// <summary>
    /// Allows to log connection events, such as disconnects, reconnects, server errors. If not set (or null), then no connection events will be logged. (Default value is null)
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// The distributed health check options. If the option is null, no health will be done. Otherwise, the healthcheck will only work if distributed cache is used and the configurations are valid (default value : null)
    /// </summary>
    public DistributedHealthCheckOptions? HealthCheckOptions { get; set; }

    /// <summary>
    /// Defines the distributed cache options
    /// </summary>
    public IDistributedCacheOptions? DistributedCacheOptions { get; set; }
}