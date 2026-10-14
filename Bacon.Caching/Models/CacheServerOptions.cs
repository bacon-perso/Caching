using Bacon.Caching.Interfaces.Models;
using Microsoft.Extensions.Logging;

namespace Bacon.Caching.Models;

/// <summary>
/// Global options of the caching library.
/// </summary>
public sealed class CacheServerOptions
{
    /// <summary>
    /// The global tenant ID, used when no tenant ID is passed to a call. Typically set in a single-tenant environment; in a multi-tenant environment it can be overridden on each call.
    /// Must be 1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The type of distributed cache. Defaults to none (memory cache only).
    /// </summary>
    public DistributedCacheTypes DistributedCacheType { get; set; } = DistributedCacheTypes.none;

    /// <summary>
    /// Logger factory passed to the distributed cache connection to log connection events, such as disconnects, reconnects and server errors. If null (default), no connection events are logged.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// The distributed health check options. If null (default), no health check is registered. The health check is only registered when a distributed cache is configured.
    /// </summary>
    public DistributedHealthCheckOptions? HealthCheckOptions { get; set; }

    /// <summary>
    /// The options of the distributed cache selected by <see cref="DistributedCacheType"/> (<see cref="RedisCacheOptions"/> for redis). Required when a distributed cache type is selected.
    /// </summary>
    public IDistributedCacheOptions? DistributedCacheOptions { get; set; }
}
