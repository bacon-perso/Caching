namespace Bacon.Caching.Interfaces.Services;

internal interface ICacheSerializerService
{
    string Serialize<TItem>(TItem value);

    TItem? Deserialize<TItem>(string json);
}