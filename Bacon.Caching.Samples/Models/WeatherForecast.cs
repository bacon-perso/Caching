using System.Text.Json.Serialization;

namespace Bacon.Caching.Samples.Models;

public class WeatherForecast
{
    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("temperature_celcius")]
    public int TemperatureC { get; set; }

    [JsonPropertyName("temperature_fahrenheit")]
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonIgnore]
    public Guid RandomGuid { get; set; }
}
