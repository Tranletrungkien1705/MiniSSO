using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;

namespace MiniSSO.Controllers;

[Authorize]
public class UserController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.Users.OrderBy(u => u.Email).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string email, string fullName, string password, string? roles, string? tenant)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        { TempData["Error"] = "Cần email và mật khẩu."; return RedirectToAction(nameof(Index)); }
        if (await db.Users.AnyAsync(u => u.Email == email)) { TempData["Error"] = "Email đã tồn tại."; return RedirectToAction(nameof(Index)); }
        db.Users.Add(new AppUser { Email = email.Trim(), FullName = fullName ?? "", Roles = roles ?? "", Tenant = tenant, PasswordHash = PasswordHasher.Hash(password) });
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo người dùng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u != null) { u.IsActive = !u.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class ClientController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.Clients.OrderBy(c => c.ClientId).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string clientId, string name, string? redirectUris, string grants, string scopes, string? secret, bool requirePkce)
    {
        if (string.IsNullOrWhiteSpace(clientId)) { TempData["Error"] = "Cần Client ID."; return RedirectToAction(nameof(Index)); }
        if (await db.Clients.AnyAsync(c => c.ClientId == clientId)) { TempData["Error"] = "Client ID đã tồn tại."; return RedirectToAction(nameof(Index)); }
        db.Clients.Add(new Client
        {
            ClientId = clientId.Trim(), Name = name ?? clientId, RedirectUris = redirectUris ?? "",
            AllowedGrants = string.IsNullOrWhiteSpace(grants) ? "authorization_code,refresh_token" : grants,
            AllowedScopes = string.IsNullOrWhiteSpace(scopes) ? "openid,profile,email" : scopes,
            RequirePkce = requirePkce, ClientSecretHash = string.IsNullOrWhiteSpace(secret) ? null : PasswordHasher.Hash(secret)
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã đăng ký client.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id);
        if (c != null) { c.IsActive = !c.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
