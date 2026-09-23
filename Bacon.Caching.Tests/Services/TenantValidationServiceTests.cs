using Bacon.Caching.Services;

namespace Bacon.Caching.Tests.Services;

[TestFixture]
internal sealed class TenantValidationServiceTests
{
    [TestCase("a")]
    [TestCase("tenant")]
    [TestCase("tenant.abc_123-xyz:1")]
    [TestCase("ABCXYZ")]
    public void ValidateTenantId_Valid(string tenantId)
    {
        Assert.DoesNotThrow(() => TenantValidationService.ValidateTenantId(tenantId));
    }

    [Test]
    public void ValidateTenantId_WithExactlyFiftyCharacters_Valid()
    {
        string tenantId = new('a', 50);

        Assert.DoesNotThrow(() => TenantValidationService.ValidateTenantId(tenantId));
    }

    [Test]
    public void ValidateTenantId_WithFiftyOneCharacters_ShouldThrowArgumentException()
    {
        string tenantId = new('a', 51);

        Assert.Throws<ArgumentException>(() => TenantValidationService.ValidateTenantId(tenantId));
    }

    [Test]
    public void ValidateTenantId_WithEmptyString_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TenantValidationService.ValidateTenantId(string.Empty));
    }

    [TestCase("tenant id")]
    [TestCase("tenant@id")]
    [TestCase("tenant/id")]
    [TestCase("tenant#id")]
    public void ValidateTenantId_WithDisallowedCharacters_ShouldThrowArgumentException(string tenantId)
    {
        Assert.Throws<ArgumentException>(() => TenantValidationService.ValidateTenantId(tenantId));
    }

    [Test]
    public void ValidateTenantId_WithNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TenantValidationService.ValidateTenantId(null!));
    }
}
