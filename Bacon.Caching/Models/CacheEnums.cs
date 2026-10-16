namespace Bacon.Caching.Models;

/// <summary>
/// Contains the distributed cache types.
/// </summary>
public enum DistributedCacheTypes
{
    /// <summary>
    /// No distributed cache. Only the memory cache is used.
    /// </summary>
    none,

    /// <summary>
    /// Redis distributed cache, in addition to the memory cache.
    /// </summary>
    redis
}

/// <summary>
/// Defines whether the items of a cached type are isolated per tenant or shared by all tenants.
/// </summary>
public enum CacheScopes
{
    /// <summary>
    /// Items are stored per tenant, with the cache key <c>{tenantId}_{CacheKey}{suffix}</c>. A tenant ID is required, passed on each call or taken from <see cref="CacheServerOptions.TenantId"/>.
    /// </summary>
    Tenant,

    /// <summary>
    /// Items are shared by all tenants, with the cache key <c>|{CacheKey}{suffix}</c> (the leading '|' cannot appear in a tenant key, so the two never collide). No tenant ID is used, and passing one to the cache service for this type throws an <see cref="ArgumentException"/>.
    /// </summary>
    Global
}