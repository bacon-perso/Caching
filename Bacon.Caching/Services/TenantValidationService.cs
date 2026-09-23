using System.Text.RegularExpressions;

namespace Bacon.Caching.Services;

internal static partial class TenantValidationService
{
    public static void ValidateTenantId(string tenantId)
    {
        if (!TenantValidationRegex().IsMatch(tenantId))
        {
            throw new ArgumentException("Caching - The tenant ID cannot be longer than 50 characters and only supports the following characters ('A-Z', 'a-z', '0-9', '.', '_', '-', ':'", nameof(tenantId));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,50}$")]
    private static partial Regex TenantValidationRegex();
}