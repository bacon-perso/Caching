namespace Bacon.Caching.Interfaces.Services;

/// <summary>
/// Serializes cached items to and from JSON, for the distributed cache providers that store text.
/// </summary>
public interface ICacheSerializerService
{
    /// <summary>
    /// Serializes an item to JSON, without indentation. Every public readable instance property is written, including the ones marked with <c>[JsonIgnore]</c>.
    /// </summary>
    /// <typeparam name="TItem">The type of the item.</typeparam>
    /// <param name="value">The item to serialize.</param>
    /// <returns>The JSON representation of the item.</returns>
    string Serialize<TItem>(TItem value);

    /// <summary>
    /// Deserializes an item from JSON produced by <see cref="Serialize{TItem}(TItem)"/>.
    /// </summary>
    /// <typeparam name="TItem">The type of the item.</typeparam>
    /// <param name="json">The JSON representation of the item.</param>
    /// <returns>The item, or null/default if the JSON is <c>null</c>.</returns>
    TItem? Deserialize<TItem>(string json);
}