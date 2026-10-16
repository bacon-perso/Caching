namespace Bacon.Caching.Redis.Models;

/// <summary>
/// Defines an additional Redis endpoint (seed node), used with <see cref="RedisServerTypes.Cluster"/>.
/// </summary>
public sealed class RedisEndpoint
{
    /// <summary>
    /// The Redis server host name or IP address, without the port. Required, cannot be empty or whitespace.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// The Redis server port. If null (default), <see cref="RedisCacheOptions.Port"/> is used. Cannot be 0.
    /// </summary>
    public ushort? Port { get; set; }
}