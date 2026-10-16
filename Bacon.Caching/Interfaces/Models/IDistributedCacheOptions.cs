using Bacon.Caching.Models;
using Microsoft.Extensions.Logging;

namespace Bacon.Caching.Interfaces.Models;

/// <summary>
/// Defines the distributed cache options.
/// </summary>
public interface IDistributedCacheOptions
{
    /// <summary>
    /// Logger factory that distributed cache providers can use to log the events of their connection, such as disconnects, reconnects and server errors. If null (default), no connection events are logged.
    /// </summary>
    ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// The server host name or IP address, without the port.
    /// </summary>
    string Server { get; set; }

    /// <summary>
    /// The server password, or null if none.
    /// </summary>
    string? Password { get; set; }

    /// <summary>
    /// The distributed health check options. If null (default), the provider registers no health check. The health check is only registered when a distributed cache is configured.
    /// </summary>
    DistributedHealthCheckOptions? HealthCheckOptions { get; set; }
}