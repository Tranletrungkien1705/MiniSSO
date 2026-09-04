using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;

namespace MiniSSO.Controllers;

/// <summary>
/// API JSON cho SPA React (admin IdP). Quản lý người dùng + client OAuth. Không multi-tenant (IdP toàn cục).
/// OAuth code flow (login/authorize/consent) + endpoint /.well-known, /oauth/token, /oauth/userinfo giữ nguyên.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(AppDbContext db, ICache cache) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        const string key = "sso:dash";
        var hit = await cache.GetAsync<DashDto>(key);
        if (hit != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(hit); }
        var dto = new DashDto(
            await db.Users.CountAsync(), await db.Users.CountAsync(u => u.IsActive),
            await db.Clients.CountAsync(), await db.RefreshTokens.CountAsync(t => !t.Revoked),
            $"{Request.Scheme}://{Request.Host}");
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(20));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users()
        => Ok((await db.Users.OrderBy(u => u.Email).ToListAsync()).Select(u => new { u.Id, u.Email, u.FullName, roles = u.RoleList, u.Tenant, u.IsActive, u.CreatedAt }));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] UserReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrWhiteSpace(r.Password))
            return BadRequest(new { error = "Cần email và mật khẩu." });
        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email)) return BadRequest(new { error = "Email đã tồn tại." });
        var u = new AppUser { Email = email, FullName = r.FullName ?? "", Roles = r.Roles ?? "", Tenant = r.Tenant, PasswordHash = PasswordHasher.Hash(r.Password) };
        db.Users.Add(u); await db.SaveChangesAsync();
        return Ok(new { id = u.Id });
    }

    [HttpPost("users/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleUser(Guid id)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound(new { error = "Không tìm thấy." });
        u.IsActive = !u.IsActive; await db.SaveChangesAsync();
        return Ok(new { ok = true, isActive = u.IsActive });
    }

    [HttpGet("clients")]
    public async Task<IActionResult> Clients()
        => Ok((await db.Clients.OrderBy(c => c.ClientId).ToListAsync()).Select(c => new
        {
            c.Id, c.ClientId, c.Name, redirects = c.Redirects, grants = c.Grants, scopes = c.Scopes,
            c.RequirePkce, isPublic = c.ClientSecretHash == null, c.IsActive
        }));

    [HttpPost("clients")]
    public async Task<IActionResult> CreateClient([FromBody] ClientReq r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientId)) return BadRequest(new { error = "Cần Client ID." });
        if (await db.Clients.AnyAsync(c => c.ClientId == r.ClientId)) return BadRequest(new { error = "Client ID đã tồn tại." });
        var c = new Client
        {
            ClientId = r.ClientId.Trim(), Name = r.Name ?? r.ClientId, RedirectUris = r.RedirectUris ?? "",
            AllowedGrants = string.IsNullOrWhiteSpace(r.Grants) ? "authorization_code,refresh_token" : r.Grants!,
            AllowedScopes = string.IsNullOrWhiteSpace(r.Scopes) ? "openid,profile,email" : r.Scopes!,
            RequirePkce = r.RequirePkce, ClientSecretHash = string.IsNullOrWhiteSpace(r.Secret) ? null : PasswordHasher.Hash(r.Secret!)
        };
        db.Clients.Add(c); await db.SaveChangesAsync();
        return Ok(new { id = c.Id });
    }

    [HttpPost("clients/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleClient(Guid id)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { error = "Không tìm thấy." });
        c.IsActive = !c.IsActive; await db.SaveChangesAsync();
        return Ok(new { ok = true, isActive = c.IsActive });
    }

    // Bản quyền: mỗi app fleet tự gọi endpoint này khi khởi động (công khai trong Program.cs) để xác thực license
    // key + tự ghi log lại "ai/ở đâu" đang chạy source code. Không xác thực JWT (app còn chưa có user đăng nhập lúc startup).
    [HttpPost("license/check")]
    public async Task<IActionResult> LicenseCheck([FromBody] LicenseCheckReq r)
    {
        var key = (r.LicenseKey ?? "").Trim();
        var appSlug = (r.AppSlug ?? "").Trim().ToLowerInvariant();
        var lic = string.IsNullOrWhiteSpace(key) ? null : await db.Licenses.FirstOrDefaultAsync(x => x.LicenseKey == key);
        bool ok = lic != null && lic.IsActive && (lic.ExpiresAt == null || lic.ExpiresAt > DateTime.UtcNow);
        var msg = lic == null ? "License key không tồn tại." : !lic.IsActive ? "License đã bị khoá." : lic.ExpiresAt <= DateTime.UtcNow ? "License đã hết hạn." : "OK.";
        db.LicenseCheckLogs.Add(new LicenseCheckLog
        {
            LicenseKey = key, AppSlug = appSlug, InstanceHost = r.InstanceHost,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(), Result = ok, Message = msg
        });
        await db.SaveChangesAsync();
        return Ok(new { valid = ok, message = msg, owner = ok ? lic!.OwnerName : null });
    }

    [HttpGet("license/logs")]
    public async Task<IActionResult> LicenseLogs([FromQuery] string? appSlug, [FromQuery] int take = 100)
    {
        var q = db.LicenseCheckLogs.OrderByDescending(x => x.CheckedAt).AsQueryable();
        if (!string.IsNullOrWhiteSpace(appSlug)) q = q.Where(x => x.AppSlug == appSlug.Trim().ToLowerInvariant());
        var rows = await q.Take(Math.Clamp(take, 1, 500)).ToListAsync();
        return Ok(rows.Select(x => new { x.AppSlug, x.InstanceHost, x.RemoteIp, x.Result, x.Message, x.CheckedAt }));
    }

    // Thông tin OIDC discovery (để SPA hiển thị hướng dẫn tích hợp).
    [HttpGet("oidc-info")]
    public IActionResult OidcInfo()
    {
        var iss = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            issuer = iss, discovery = $"{iss}/.well-known/openid-configuration", jwks = $"{iss}/.well-known/jwks.json",
            token = $"{iss}/oauth/token", authorize = $"{iss}/oauth/authorize", userinfo = $"{iss}/oauth/userinfo"
        });
    }
}

public record DashDto(int Users, int ActiveUsers, int Clients, int ActiveTokens, string Issuer);

public class UserReq { public string Email { get; set; } = ""; public string Password { get; set; } = ""; public string? FullName { get; set; } public string? Roles { get; set; } public string? Tenant { get; set; } }
public class ClientReq { public string ClientId { get; set; } = ""; public string? Name { get; set; } public string? RedirectUris { get; set; } public string? Grants { get; set; } public string? Scopes { get; set; } public string? Secret { get; set; } public bool RequirePkce { get; set; } = true; }
public class LicenseCheckReq { public string? LicenseKey { get; set; } public string? AppSlug { get; set; } public string? InstanceHost { get; set; } }
