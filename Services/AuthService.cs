using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

public sealed record TokenResult(bool Ok, string? Error, string? AccessToken = null, string? IdToken = null,
    string? RefreshToken = null, int ExpiresIn = 0, string? Scope = null);

public sealed class AuthService(AppDbContext db, TokenService tokens)
{
    public async Task<AppUser?> ValidateUserAsync(string email, string password)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Email == email && x.IsActive);
        return u != null && PasswordHasher.Verify(password, u.PasswordHash) ? u : null;
    }

    public Task<Client?> GetClientAsync(string clientId) =>
        db.Clients.FirstOrDefaultAsync(c => c.ClientId == clientId && c.IsActive);

    public static bool VerifyClientSecret(Client c, string? secret) =>
        string.IsNullOrEmpty(c.ClientSecretHash) || (secret != null && PasswordHasher.Verify(secret, c.ClientSecretHash));

    public async Task<string> CreateAuthCodeAsync(string clientId, Guid userId, string redirectUri, string scope, string? challenge, string? method)
    {
        var code = TokenService.NewOpaque();
        db.AuthCodes.Add(new AuthCode { Code = code, ClientId = clientId, UserId = userId, RedirectUri = redirectUri,
            Scope = scope, CodeChallenge = challenge, CodeChallengeMethod = method, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync();
        return code;
    }

    /// <summary>Xử lý /oauth/token theo grant_type.</summary>
    public async Task<TokenResult> TokenAsync(IFormCollection f, string issuer)
    {
        var grant = f["grant_type"].ToString();
        var clientId = f["client_id"].ToString();
        var secret = f["client_secret"].ToString();
        var client = await GetClientAsync(clientId);
        if (client == null) return new(false, "invalid_client");
        if (!VerifyClientSecret(client, string.IsNullOrEmpty(secret) ? null : secret)) return new(false, "invalid_client");
        if (!client.Grants.Contains(grant)) return new(false, "unauthorized_client");

        return grant switch
        {
            "authorization_code" => await AuthCodeGrant(f, client, issuer),
            "refresh_token" => await RefreshGrant(f, client, issuer),
            "password" => await PasswordGrant(f, client, issuer),
            "client_credentials" => ClientCredsGrant(client, issuer),
            _ => new(false, "unsupported_grant_type")
        };
    }

    private async Task<TokenResult> AuthCodeGrant(IFormCollection f, Client client, string issuer)
    {
        var code = f["code"].ToString();
        var ac = await db.AuthCodes.FirstOrDefaultAsync(x => x.Code == code && !x.Consumed);
        if (ac == null || ac.ExpiresAt < DateTime.UtcNow || ac.ClientId != client.ClientId) return new(false, "invalid_grant");
        if (ac.RedirectUri != f["redirect_uri"].ToString()) return new(false, "invalid_grant");
        if (ac.CodeChallenge != null)
        {
            var verifier = f["code_verifier"].ToString();
            if (string.IsNullOrEmpty(verifier) || !VerifyPkce(verifier, ac.CodeChallenge, ac.CodeChallengeMethod)) return new(false, "invalid_grant");
        }
        ac.Consumed = true;
        var user = await db.Users.FirstAsync(u => u.Id == ac.UserId);
        await db.SaveChangesAsync();
        return await IssueAsync(user, client, ac.Scope, issuer, includeId: ac.Scope.Contains("openid"));
    }

    private async Task<TokenResult> RefreshGrant(IFormCollection f, Client client, string issuer)
    {
        var rt = f["refresh_token"].ToString();
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.Token == rt && !x.Revoked);
        if (row == null || row.ExpiresAt < DateTime.UtcNow || row.ClientId != client.ClientId) return new(false, "invalid_grant");
        row.Revoked = true;   // rotate
        var user = await db.Users.FirstAsync(u => u.Id == row.UserId);
        await db.SaveChangesAsync();
        return await IssueAsync(user, client, row.Scope, issuer, includeId: row.Scope.Contains("openid"));
    }

    private async Task<TokenResult> PasswordGrant(IFormCollection f, Client client, string issuer)
    {
        var user = await ValidateUserAsync(f["username"].ToString(), f["password"].ToString());
        if (user == null) return new(false, "invalid_grant");
        var scope = string.IsNullOrWhiteSpace(f["scope"]) ? string.Join(' ', client.Scopes) : f["scope"].ToString();
        return await IssueAsync(user, client, scope, issuer, includeId: scope.Contains("openid"));
    }

    private TokenResult ClientCredsGrant(Client client, string issuer)
    {
        var scope = string.Join(' ', client.Scopes);
        var svcUser = new AppUser { Id = Guid.Empty, Email = client.ClientId, FullName = client.Name, Roles = "service" };
        var access = tokens.IssueAccessToken(svcUser, issuer, client.ClientId, scope);
        return new(true, null, access, null, null, TokenService.AccessMinutes * 60, scope);
    }

    private async Task<TokenResult> IssueAsync(AppUser user, Client client, string scope, string issuer, bool includeId)
    {
        var access = tokens.IssueAccessToken(user, issuer, client.ClientId, scope);
        var id = includeId ? tokens.IssueIdToken(user, issuer, client.ClientId) : null;
        string? refresh = null;
        if (client.Grants.Contains("refresh_token"))
        {
            refresh = TokenService.NewOpaque();
            db.RefreshTokens.Add(new RefreshToken { Token = refresh, UserId = user.Id, ClientId = client.ClientId,
                Scope = scope, ExpiresAt = DateTime.UtcNow.AddDays(TokenService.RefreshDays) });
            await db.SaveChangesAsync();
        }
        return new(true, null, access, id, refresh, TokenService.AccessMinutes * 60, scope);
    }

    private static bool VerifyPkce(string verifier, string challenge, string? method)
    {
        if (method == "S256" || method == null)
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
            return SigningKeyStore.Base64Url(hash) == challenge;
        }
        return verifier == challenge;   // plain
    }
}
