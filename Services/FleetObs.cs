using System.Text.Json;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Sinks.OpenSearch;
using StackExchange.Redis;

namespace MiniSSO.Services;

// ===== Quan sát hạ tầng dùng chung fleet: Serilog + Correlation-Id + OpenSearch(Bonsai) + Redis + Swagger =====

/// <summary>Gán/đọc X-Correlation-Id, đưa vào LogContext → trace xuyên các app.</summary>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";
    public async Task Invoke(HttpContext ctx)
    {
        var cid = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(cid)) cid = Guid.NewGuid().ToString("N")[..16];
        ctx.Response.Headers[Header] = cid; ctx.Items[Header] = cid;
        using (LogContext.PushProperty("CorrelationId", cid)) await next(ctx);
    }
}

public interface ICache
{
    bool Enabled { get; }
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan ttl);
    Task RemoveByPrefixAsync(string prefix);
}

public sealed class RedisCache : ICache
{
    private readonly IConnectionMultiplexer? _mux;
    public bool Enabled => _mux is { IsConnected: true };
    public RedisCache(IConfiguration cfg, ILogger<RedisCache> log)
    {
        var url = Environment.GetEnvironmentVariable("REDIS_URL") ?? cfg["REDIS_URL"];
        if (string.IsNullOrWhiteSpace(url)) return;
        try { var o = ConfigurationOptions.Parse(To(url)); o.AbortOnConnectFail = false; o.ConnectTimeout = 5000; _mux = ConnectionMultiplexer.Connect(o); }
        catch (Exception ex) { log.LogWarning("Redis fail: {E}", ex.Message); }
    }
    private static string To(string url)
    {
        if (!url.Contains("://")) return url;
        var u = new Uri(url); var pass = u.UserInfo.Contains(':') ? u.UserInfo.Split(':', 2)[1] : u.UserInfo;
        return $"{u.Host}:{(u.Port > 0 ? u.Port : 6379)},password={Uri.UnescapeDataString(pass)},ssl={url.StartsWith("rediss://").ToString().ToLower()},abortConnect=false";
    }
    public async Task<T?> GetAsync<T>(string key) { if (!Enabled) return default; try { var v = await _mux!.GetDatabase().StringGetAsync(key); return v.HasValue ? JsonSerializer.Deserialize<T>(v!) : default; } catch { return default; } }
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl) { if (!Enabled) return; try { await _mux!.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), ttl); } catch { } }
    public async Task RemoveByPrefixAsync(string prefix) { if (!Enabled) return; try { foreach (var ep in _mux!.GetEndPoints()) foreach (var k in _mux.GetServer(ep).Keys(pattern: prefix + "*")) await _mux.GetDatabase().KeyDeleteAsync(k); } catch { } }
}

public static class FleetObs
{
    /// <summary>Cấu hình Serilog: console + OpenSearch(Bonsai) khi có ELASTIC_URL. Index fleet-events-* (khớp whitelist Bonsai).</summary>
    public static void ConfigureLogger(string app)
    {
        var url = Environment.GetEnvironmentVariable("ELASTIC_URL");
        var cfg = new LoggerConfiguration().MinimumLevel.Information().MinimumLevel.Override("Microsoft", LogEventLevel.Warning).MinimumLevel.Override("System", LogEventLevel.Warning).Enrich.FromLogContext().Enrich.WithProperty("app", app)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({CorrelationId}) {Message:lj}{NewLine}{Exception}");
        if (!string.IsNullOrWhiteSpace(url))
            cfg = cfg.WriteTo.OpenSearch(new OpenSearchSinkOptions(new Uri(url))
            {
                AutoRegisterTemplate = false, IndexFormat = "fleet-events-{0:yyyy.MM.dd}", BatchPostingLimit = 20, Period = TimeSpan.FromSeconds(3),
                ModifyConnectionSettings = c => { var u = Environment.GetEnvironmentVariable("ELASTIC_USER"); var p = Environment.GetEnvironmentVariable("ELASTIC_PASS"); return string.IsNullOrEmpty(u) ? c : c.BasicAuthentication(u, p); }
            });
        Log.Logger = cfg.CreateLogger();
    }
    public static void AddFleetObs(this IServiceCollection s)
    { s.AddSingleton<ICache, RedisCache>(); s.AddEndpointsApiExplorer(); s.AddSwaggerGen(); }
    public static void UseFleetObs(this WebApplication app)
    { app.UseMiddleware<CorrelationMiddleware>(); app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) => ex != null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error : ctx.Request.Path.StartsWithSegments("/healthz") ? LogEventLevel.Verbose : LogEventLevel.Information); app.UseSwagger(); app.UseSwaggerUI(c => c.RoutePrefix = "swagger"); }
}
