using Bacon.Caching.Extensions;
using Bacon.Caching.Models;
using Bacon.Caching.Redis.Extensions;
using Bacon.Caching.Samples.Models;
using Serilog;
using System.Globalization;


WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .MinimumLevel.Warning()
                    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                    .CreateLogger();

ILoggerFactory loggerFactory = new LoggerFactory();
loggerFactory.AddSerilog();

// Add services to the container.

webApplicationBuilder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
webApplicationBuilder.Services.AddOpenApi();

string tenantId = Constants.TenantId;
string redisServer = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Server"]!;
ushort redisPort = Convert.ToUInt16(webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Port"]!, CultureInfo.InvariantCulture);
string redisPassword = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Password"]!;
bool redisUseSsl = Convert.ToBoolean(webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:UseSsl"]!);

webApplicationBuilder.Services.AddCache(o =>
{
    o.TenantId = tenantId;
})
    .AddRedis(o =>
    {
        o.LoggerFactory = loggerFactory;
        o.Server = redisServer;
        o.Port = redisPort;
        o.Password = redisPassword;
        o.UseSsl = redisUseSsl;
        o.GetBatchSize = 1000;
        o.InsertBatchSize = 500;
        //o.HealthCheckOptions = new();
    })
    .AddItem<WeatherForecast>(o =>
    {
        o.CacheKey = "weather_";
        //o.MemoryCacheDuration = TimeSpan.FromSeconds(60);
        o.MemoryCacheDuration = TimeSpan.Zero;
        o.DistributedCacheDuration = TimeSpan.FromSeconds(60);
        o.Scope = CacheScopes.Tenant;
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