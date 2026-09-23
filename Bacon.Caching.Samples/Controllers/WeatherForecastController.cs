using Bacon.Caching.Interfaces.Services;
using Bacon.Caching.Samples.Models;
using Microsoft.AspNetCore.Mvc;

namespace Bacon.Caching.Samples.Controllers;

[ApiController]
[Route("api/weather")]
public class WeatherForecastController(ICacheService<WeatherForecast> weatherForecastCacheService) : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    [HttpGet(Name = "GetWeatherForecast")]
    public async Task<IEnumerable<WeatherForecast>> GetAsync()
    {
        WeatherForecast[] weatherForecasts = [.. Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)],
            RandomGuid = Guid.NewGuid()
        })];

        Dictionary<int, WeatherForecast> weatherDic = [];
        for (int idx = 1; idx <= weatherForecasts.Length; ++idx)
        {
            weatherDic.Add(idx, weatherForecasts[idx -1]);
        }

        weatherForecastCacheService.SetMemoryItems(weatherDic);

        IDictionary<int, WeatherForecast?> weatherCacheDic = weatherForecastCacheService.GetMemoryItems(weatherDic.Select(s => s.Key));

        await weatherForecastCacheService.SetDistributedItemsAsync(weatherDic);

        IDictionary<int, WeatherForecast?> weatherCacheDicDistributed = await weatherForecastCacheService.GetDistributedItemsAsync(weatherDic.Select(s => s.Key));

        return weatherForecasts;
    }
}
