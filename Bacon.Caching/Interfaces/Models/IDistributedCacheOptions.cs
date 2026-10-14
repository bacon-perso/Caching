namespace Bacon.Caching.Interfaces.Models;

/// <summary>
/// Defines the distributed cache options.
/// </summary>
public interface IDistributedCacheOptions
{
    /// <summary>
    /// The server host name or IP address, without the port.
    /// </summary>
    string Server { get; set; }

    /// <summary>
    /// The server password, or null if none.
    /// </summary>
    string? Password { get; set; }
}
