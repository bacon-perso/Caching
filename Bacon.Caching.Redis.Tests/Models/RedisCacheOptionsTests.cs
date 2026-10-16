using Bacon.Caching.Redis.Models;

namespace Bacon.Caching.Redis.Tests.Models;

[TestFixture]
internal sealed class RedisCacheOptionsTests
{
    [Test]
    public void NewOptions_HaveTheDocumentedDefaults()
    {
        RedisCacheOptions options = new();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Server, Is.Empty);
            Assert.That(options.Port, Is.EqualTo(6379));
            Assert.That(options.Password, Is.Null);
            Assert.That(options.UseSsl, Is.False);
            Assert.That(options.GetBatchSize, Is.EqualTo(100));
            Assert.That(options.RemoveBatchSize, Is.EqualTo(100));
            Assert.That(options.InsertBatchSize, Is.EqualTo(100));
            Assert.That(options.SyncTimeout, Is.EqualTo(5000));
            Assert.That(options.ServerType, Is.EqualTo(RedisServerTypes.Standalone));
            Assert.That(options.LoggerFactory, Is.Null);
            Assert.That(options.HealthCheckOptions, Is.Null);
            Assert.That(options.AdditionalEndpoints, Is.Null);
        };
    }
}