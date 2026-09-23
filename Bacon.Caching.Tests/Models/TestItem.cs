using System.Text.Json.Serialization;

namespace Bacon.Caching.Tests.Models;

internal sealed class TestItem
{
    public string Name { get; set; } = string.Empty;

    public int Value { get; set; }

    [JsonIgnore]
    public Guid RandomGuid { get; set; }

    [JsonPropertyName("custom_name")]
    public string? CustomName { get; set; }
}
