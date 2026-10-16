namespace Bacon.Caching.Models;

/// <summary>
/// Global options of the caching library.
/// </summary>
public sealed class CacheServerOptions
{
    /// <summary>
    /// The default tenant ID, used when no tenant ID is passed to a call. Typically set in a single-tenant environment; in a multi-tenant environment it can be overridden on each call.
    /// Must be 1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':'. Not used by types configured with <see cref="CacheScopes.Global"/>.
    /// </summary>
    public string? TenantId { get; set; }
}