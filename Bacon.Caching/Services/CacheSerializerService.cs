using Bacon.Caching.DataContracts;
using Bacon.Caching.Interfaces.Services;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bacon.Caching.Services;

internal sealed class CacheSerializerService : ICacheSerializerService
{
    #region CTOR

    private readonly JsonSerializerOptions _jsonSerializerOption;

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

    public string Serialize<TItem>(TItem value) => JsonSerializer.Serialize(value, _jsonSerializerOption);

    public TItem? Deserialize<TItem>(string json) => JsonSerializer.Deserialize<TItem>(json, _jsonSerializerOption);
}