namespace Bacon.Caching.Interfaces.Models;

/// <summary>
/// Defines the distributed cache options
/// </summary>
public interface IDistributedCacheOptions
{
    /// <summary>
    /// The server
    /// </summary>
    string Server { get; set; }

    /// <summary>
    /// Server password
    /// </summary>
    string? Password { get; set; }
}