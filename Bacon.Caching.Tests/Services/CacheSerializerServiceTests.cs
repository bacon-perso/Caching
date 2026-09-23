using Bacon.Caching.Services;
using Bacon.Caching.Tests.Models;

namespace Bacon.Caching.Tests.Services;

[TestFixture]
internal sealed class CacheSerializerServiceTests
{
    private CacheSerializerService _sut = null!;

    [SetUp]
    public void SetUp() => _sut = new CacheSerializerService();

    [Test]
    public void Roundtrip_PreservesStandardProperties()
    {
        TestItem original = new() { Name = "Alice", Value = 42 };

        TestItem? result = _sut.Deserialize<TestItem>(_sut.Serialize(original));

        Assert.Multiple(() =>
        {
            Assert.That(result!.Name, Is.EqualTo("Alice"));
            Assert.That(result.Value, Is.EqualTo(42));
        });
    }

    [Test]
    public void Roundtrip_JsonIgnoreProperty_IsIncluded()
    {
        Guid guid = Guid.NewGuid();
        TestItem original = new() { RandomGuid = guid };

        TestItem? result = _sut.Deserialize<TestItem>(_sut.Serialize(original));

        Assert.That(result!.RandomGuid, Is.EqualTo(guid));
    }

    [Test]
    public void Serialize_JsonPropertyNameAttribute_UsesClrNameNotJsonName()
    {
        string json = _sut.Serialize(new TestItem { CustomName = "hello" });

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"CustomName\""));
            Assert.That(json, Does.Not.Contain("\"custom_name\""));
        });
    }

    [Test]
    public void Roundtrip_JsonPropertyNameAttribute_DeserializesCorrectly()
    {
        TestItem original = new() { CustomName = "hello" };

        TestItem? result = _sut.Deserialize<TestItem>(_sut.Serialize(original));

        Assert.That(result!.CustomName, Is.EqualTo("hello"));
    }

    [Test]
    public void Deserialize_NullJson_ReturnsNull()
    {
        Assert.That(_sut.Deserialize<TestItem>("null"), Is.Null);
    }

    [Test]
    public void Roundtrip_NullableProperty_PreservesNull()
    {
        TestItem original = new() { CustomName = null };

        TestItem? result = _sut.Deserialize<TestItem>(_sut.Serialize(original));

        Assert.That(result!.CustomName, Is.Null);
    }
}
