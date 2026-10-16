using Bacon.Caching.DataContracts;
using Bacon.Caching.Interfaces.Services;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bacon.Caching.Services;

/// <summary>
/// Default <see cref="ICacheSerializerService"/>, based on System.Text.Json and registered by <c>AddCache</c>. It writes every public readable instance property, including the ones marked with <c>[JsonIgnore]</c>.
/// </summary>
public sealed class CacheSerializerService : ICacheSerializerService
{
    #region CTOR

    private readonly JsonSerializerOptions _jsonSerializerOption;

    /// <summary>
    /// Creates the serializer and its JSON options.
    /// </summary>
    public CacheSerializerService()
    {
        _jsonSerializerOption = new()
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            {
                Modifiers =
            {
                JsonResolverExtensions.IgnoreJsonIgnoreAttributesModifier
            }
            },
        };
    }

    #endregion CTOR

    /// <inheritdoc />
    public string Serialize<TItem>(TItem value) => JsonSerializer.Serialize(value, _jsonSerializerOption);

    /// <inheritdoc />
    public TItem? Deserialize<TItem>(string json) => JsonSerializer.Deserialize<TItem>(json, _jsonSerializerOption);
}