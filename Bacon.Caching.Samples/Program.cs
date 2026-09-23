using Bacon.Caching.Extensions;
using Bacon.Caching.Models;
using Bacon.Caching.Samples.Models;
using Serilog;


WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .MinimumLevel.Warning()
                    .WriteTo.Console()
                    .CreateLogger();

ILoggerFactory loggerFactory = new LoggerFactory();
loggerFactory.AddSerilog();

// Add services to the container.

webApplicationBuilder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
webApplicationBuilder.Services.AddOpenApi();

string tenantId = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Server"]!;
string redisServer = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Server"]!;
ushort redisPort = Convert.ToUInt16(webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Port"]!);
string redisPassword = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Password"]!;
bool redisUseSsl = Convert.ToBoolean(webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:UseSsl"]!);

webApplicationBuilder.Services.AddCache(o =>
{
    o.TenantId = tenantId;
    o.DistributedCacheType = DistributedCacheTypes.redis;
    o.LoggerFactory = loggerFactory;
    //o.HealthCheckOptions = new();
    o.DistributedCacheOptions = new RedisCacheOptions()
    {
        Server = redisServer,
        Port = redisPort,
        Password = redisPassword,
        UseSsl = redisUseSsl,
    };
})
    .AddItem<WeatherForecast>(o =>
    {
        o.CacheKey = "weather_";
        o.MemoryCacheDuration = TimeSpan.FromSeconds(60);
        o.DistributedCacheDuration = TimeSpan.FromSeconds(60);
    });

WebApplication webApplication = webApplicationBuilder.Build();

// Configure the HTTP request pipeline.
if (webApplication.Environment.IsDevelopment())
{
    webApplication.MapOpenApi();
}

webApplication.UseHttpsRedirection();

webApplication.UseAuthorization();

webApplication.MapControllers();

webApplication.Run();
