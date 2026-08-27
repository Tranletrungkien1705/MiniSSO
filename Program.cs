using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MiniSSO.Data;
using MiniSSO.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minisso.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddSingleton<SigningKeyStore>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => { o.LoginPath = "/Account/Login"; o.Cookie.Name = "minisso.sid"; });
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
// Sau reverse-proxy (Render terminate TLS): tin X-Forwarded-Proto/Host để Request.Scheme=https → issuer đúng.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear(); o.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await Seeder.SeedAsync(db);
    await scope.ServiceProvider.GetRequiredService<SigningKeyStore>().EnsureLoadedAsync(db);
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

string Issuer(HttpRequest r) => $"{r.Scheme}://{r.Host}";

app.MapGet("/healthz", () => "ok");

// ── OIDC discovery ───────────────────────────────────────────────────
app.MapGet("/.well-known/openid-configuration", (HttpRequest r) =>
{
    var iss = Issuer(r);
    return Results.Ok(new
    {
        issuer = iss,
        authorization_endpoint = $"{iss}/oauth/authorize",
        token_endpoint = $"{iss}/oauth/token",
        userinfo_endpoint = $"{iss}/oauth/userinfo",
        jwks_uri = $"{iss}/.well-known/jwks.json",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token", "password", "client_credentials" },
        scopes_supported = new[] { "openid", "profile", "email", "roles", "api" },
        token_endpoint_auth_methods_supported = new[] { "client_secret_post", "none" },
        code_challenge_methods_supported = new[] { "S256", "plain" },
        subject_types_supported = new[] { "public" },
        id_token_signing_alg_values_supported = new[] { "RS256" }
    });
});

app.MapGet("/.well-known/jwks.json", (SigningKeyStore keys) => Results.Ok(new { keys = new[] { keys.Jwk() } }));

// ── Token endpoint ───────────────────────────────────────────────────
app.MapPost("/oauth/token", async (HttpRequest r, AuthService auth) =>
{
    var res = await auth.TokenAsync(r.Form, Issuer(r));
    if (!res.Ok) return Results.BadRequest(new { error = res.Error });
    return Results.Ok(new
    {
        access_token = res.AccessToken,
        id_token = res.IdToken,
        refresh_token = res.RefreshToken,
        token_type = "Bearer",
        expires_in = res.ExpiresIn,
        scope = res.Scope
    });
});

// ── UserInfo (Bearer JWT) ────────────────────────────────────────────
app.MapGet("/oauth/userinfo", async (HttpRequest r, AppDbContext db, SigningKeyStore keys) =>
{
    var auth = r.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
    var token = auth["Bearer ".Length..].Trim();
    try
    {
        var handler = new JwtSecurityTokenHandler();
        var result = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = Issuer(r),
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new RsaSecurityKey(keys.Rsa) { KeyId = keys.Kid },
            ValidateIssuerSigningKey = true
        }, out _);
        var sub = result.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var uid)) return Results.Ok(new { sub, name = result.FindFirst("name")?.Value });
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return Results.Unauthorized();
        return Results.Ok(new { sub = u.Id, email = u.Email, name = u.FullName, roles = u.RoleList, tenant = u.Tenant });
    }
    catch { return Results.Unauthorized(); }
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
