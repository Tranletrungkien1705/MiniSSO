using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Giữ khóa RSA ký JWT (nạp/tạo từ DB 1 lần) — token sống qua các lần restart.</summary>
public sealed class SigningKeyStore
{
    public RSA Rsa { get; private set; } = RSA.Create(2048);
    public string Kid { get; private set; } = "boot";

    public async Task EnsureLoadedAsync(AppDbContext db)
    {
        var rec = await db.SigningKeys.FirstOrDefaultAsync(k => k.IsActive);
        if (rec == null)
        {
            using var rsa = RSA.Create(2048);
            var kid = Guid.NewGuid().ToString("N")[..12];
            rec = new SigningKey { Kid = kid, PrivateKeyPkcs8 = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()) };
            db.SigningKeys.Add(rec);
            await db.SaveChangesAsync();
        }
        var loaded = RSA.Create();
        loaded.ImportPkcs8PrivateKey(Convert.FromBase64String(rec.PrivateKeyPkcs8), out _);
        Rsa = loaded;
        Kid = rec.Kid;
    }

    public object Jwk()
    {
        var p = Rsa.ExportParameters(false);
        return new
        {
            kty = "RSA", use = "sig", alg = "RS256", kid = Kid,
            n = Base64Url(p.Modulus!), e = Base64Url(p.Exponent!)
        };
    }

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class TokenService(SigningKeyStore keys)
{
    public const int AccessMinutes = 60, RefreshDays = 30;

    private SigningCredentials Creds() =>
        new(new RsaSecurityKey(keys.Rsa) { KeyId = keys.Kid }, SecurityAlgorithms.RsaSha256);

    public string IssueAccessToken(AppUser u, string issuer, string clientId, string scope)
    {
        var claims = new List<Claim>
        {
            new("sub", u.Id.ToString()), new("email", u.Email), new("name", u.FullName),
            new("scope", scope), new("client_id", clientId)
        };
        if (!string.IsNullOrEmpty(u.Tenant)) claims.Add(new Claim("tenant", u.Tenant));
        foreach (var r in u.RoleList) claims.Add(new Claim("role", r));
        return Write(issuer, clientId, claims, DateTime.UtcNow.AddMinutes(AccessMinutes));
    }

    public string IssueIdToken(AppUser u, string issuer, string clientId)
    {
        var claims = new List<Claim>
        {
            new("sub", u.Id.ToString()), new("email", u.Email), new("name", u.FullName), new("email_verified", "true")
        };
        return Write(issuer, clientId, claims, DateTime.UtcNow.AddMinutes(AccessMinutes));
    }

    private string Write(string issuer, string audience, List<Claim> claims, DateTime exp)
    {
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(issuer, audience, claims, now, exp, Creds());
        jwt.Payload["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds();
        jwt.Payload["jti"] = Guid.NewGuid().ToString("N");
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public static string NewOpaque() =>
        SigningKeyStore.Base64Url(RandomNumberGenerator.GetBytes(32));
}
