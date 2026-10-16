using StackExchange.Redis;

namespace Bacon.Caching.Redis.Models;

internal sealed class RedisCacheConnection(ConnectionMultiplexer connectionMultiplexer, IDatabase database) : IDisposable
{
    public ConnectionMultiplexer Multiplexer { get; } = connectionMultiplexer;

    public IDatabase Database { get; } = database;

    public void Dispose() => Multiplexer.Dispose();
}