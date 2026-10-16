namespace Bacon.Caching.Models;

/// <summary>
/// Cache options of a single cached type.
/// </summary>
/// <typeparam name="TItem">The type of the cached items.</typeparam>
public sealed class CacheOptions<TItem>
{
    /// <summary>
    /// Key prefix of the cached type (maximum 50 characters). The final cache key is <c>{tenantId}_{CacheKey}{suffix}</c>, or <c>|{CacheKey}{suffix}</c> when <see cref="Scope"/> is <see cref="CacheScopes.Global"/>. Required, cannot be empty or whitespace.
    /// </summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>
    /// How long items stay in the memory cache. <see cref="TimeSpan.Zero"/> (default) disables the memory cache for this type. Cannot be negative.
    /// </summary>
    public TimeSpan MemoryCacheDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// How long items stay in the distributed cache. <see cref="TimeSpan.Zero"/> (default) disables the distributed cache for this type. Cannot be negative.
    /// </summary>
    public TimeSpan DistributedCacheDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Whether the items of this type are isolated per tenant (<see cref="CacheScopes.Tenant"/>, default) or shared by all tenants (<see cref="CacheScopes.Global"/>). Must be a defined <see cref="CacheScopes"/> value.
    /// </summary>
    public CacheScopes Scope { get; set; } = CacheScopes.Tenant;
}