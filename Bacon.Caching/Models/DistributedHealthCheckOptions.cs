using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bacon.Caching.Models;

/// <summary>
/// Defines the health check configurations
/// </summary>
public sealed class DistributedHealthCheckOptions
{
    /// <summary>
    /// Defines the unique name of the health check (default value : "redis")
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Defines the status used if the check fails. (default value : "Degraded")
    /// </summary>
    public HealthStatus? FailureStatus { get; set; }

    /// <summary>
    /// Defines the time before the check is considered timed out (default value : null)
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Defines the tags. (default value : ["Cache"])
    /// </summary>
    public IEnumerable<string>? Tags { get; set; }
}