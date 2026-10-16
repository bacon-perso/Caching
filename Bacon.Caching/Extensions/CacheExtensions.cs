using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Models;
using Bacon.Caching.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bacon.Caching.Extensions;

/// <summary>
/// Cache middleware extensions
/// </summary>
public static class CacheExtensions
{
    #region Extensions

    /// <summary>
    /// Registers the caching services: the memory cache, the serializer and <see cref="ICacheService{TItem}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The action configuring the cache server options.</param>
    /// <remarks>
    /// Only the memory cache is available after this call. To add a distributed cache, chain the extension of a provider on the returned builder (for example <c>AddRedis</c> from the Bacon.Caching.Redis package).
    /// A type with a <see cref="CacheOptions{TItem}.DistributedCacheDuration"/> greater than zero but no provider registered still works with the memory cache; the missing provider is logged.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The services or the configure options action is null.</exception>
    /// <exception cref="ArgumentException">The default tenant ID is invalid (1 to 50 characters of A-Z, a-z, 0-9, '.', '_', '-' or ':').</exception>
    /// <returns>The cache builder, used to add the cached types with <see cref="AddItem{TItem}(CacheBuilder, Action{CacheOptions{TItem}})"/>.</returns>
    public static CacheBuilder AddCache(this IServiceCollection services, Action<CacheServerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));

        CacheServerOptions cacheServerOptions = new();
        configureOptions(cacheServerOptions);

        ValidateCacheServerOptions(cacheServerOptions);

        services.AddMemoryCache();

        services.AddSingleton<ICacheSerializerService, CacheSerializerService>();

        services.AddSingleton(typeof(ICacheService<>), typeof(CacheService<>));

        services.AddSingleton(Options.Create(cacheServerOptions));

        return new(services);
    }

    /// <summary>
    /// Registers the cache configuration of a cached type.
    /// </summary>
    /// <typeparam name="TItem">The type of the items to cache.</typeparam>
    /// <param name="cacheBuilder">The cache builder returned by <see cref="AddCache(IServiceCollection, Action{CacheServerOptions})"/>.</param>
    /// <param name="configureOptions">The action configuring the cache options of <typeparamref name="TItem"/>.</param>
    /// <returns>The cache builder.</returns>
    /// <exception cref="ArgumentNullException">The configure options action is null.</exception>
    /// <exception cref="ArgumentException">The type is already configured and cannot be configured a second time.</exception>
    /// <exception cref="ArgumentException">The cache key is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentException">The cache key is longer than 50 characters.</exception>
    /// <exception cref="ArgumentException">The memory cache duration is negative.</exception>
    /// <exception cref="ArgumentException">The distributed cache duration is negative.</exception>
    /// <exception cref="ArgumentException">The scope is not a defined <see cref="CacheScopes"/> value.</exception>
    public static CacheBuilder AddItem<TItem>(this CacheBuilder cacheBuilder, Action<CacheOptions<TItem>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(cacheBuilder, nameof(cacheBuilder));
        ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));

        CacheOptions<TItem> options = new();
        configureOptions(options);

        ValidateCacheOptions(cacheBuilder, options);

        cacheBuilder.Services.Configure(configureOptions);

        cacheBuilder.CachedTypes.Add(typeof(TItem));

        return cacheBuilder;
    }

    #endregion Extensions

    #region Validations

    private static void ValidateCacheOptions<TItem>(CacheBuilder cacheBuilder, CacheOptions<TItem> cacheOptions)
    {
        Type type = typeof(TItem);

        if (cacheBuilder.CachedTypes.Contains(type))
        {
            throw new ArgumentException($"Caching - The type {type.Name} already exists, and cannot be configured a second time");
        }

        if (string.IsNullOrWhiteSpace(cacheOptions.CacheKey))
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.CacheKey)} cannot be null or empty.");
        }

        if (cacheOptions.CacheKey.Length > 50)
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.CacheKey)} of '{type.Name}' cannot be longer than 50 characters (current length: {cacheOptions.CacheKey.Length}).");
        }

        if (cacheOptions.MemoryCacheDuration.Ticks < 0)
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.MemoryCacheDuration)} cannot contain negative values.");
        }

        if (cacheOptions.DistributedCacheDuration.Ticks < 0)
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.DistributedCacheDuration)} cannot contain negative values.");
        }

        if (!Enum.IsDefined(cacheOptions.Scope))
        {
            throw new ArgumentException($"Caching - The {nameof(cacheOptions.Scope)} of '{type.Name}' is not a valid value ({cacheOptions.Scope}).");
        }
    }

    private static void ValidateCacheServerOptions(CacheServerOptions cacheServerOptions)
    {
        if (!string.IsNullOrEmpty(cacheServerOptions.TenantId))
        {
            TenantValidationService.ValidateTenantId(cacheServerOptions.TenantId);
        }
    }

    #endregion Validations
}