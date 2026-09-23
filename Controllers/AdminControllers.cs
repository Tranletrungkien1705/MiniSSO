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
public class GroupController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Objects = await db.PermissionObjects.OrderBy(o => o.Code).ToListAsync();
        ViewBag.Users = await db.Users.OrderBy(u => u.Email).ToListAsync();
        ViewBag.Members = await db.GroupMembers.ToListAsync();
        ViewBag.Access = await db.GroupAccesses.ToListAsync();
        return View(await db.Groups.OrderBy(g => g.Code).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Cần mã nhóm."; return RedirectToAction(nameof(Index)); }
        var c = code.Trim().ToUpperInvariant();
        if (await db.Groups.AnyAsync(g => g.Code == c)) { TempData["Error"] = "Mã nhóm đã tồn tại."; return RedirectToAction(nameof(Index)); }
        db.Groups.Add(new Group { Code = c, Name = string.IsNullOrWhiteSpace(name) ? c : name!.Trim(), Description = description });
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo nhóm.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var g = await db.Groups.FirstOrDefaultAsync(x => x.Id == id);
        if (g != null) { g.IsActive = !g.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    // Cấp/thu quyền đối tượng cho nhóm (Sys_Access).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAccess(Guid groupId, Guid objectId, bool grant)
    {
        var existing = await db.GroupAccesses.FirstOrDefaultAsync(a => a.GroupId == groupId && a.ObjectId == objectId);
        if (grant && existing == null) db.GroupAccesses.Add(new GroupAccess { GroupId = groupId, ObjectId = objectId });
        else if (!grant && existing != null) db.GroupAccesses.Remove(existing);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Thêm/bớt thành viên nhóm (Sys_UserInGroup).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMember(Guid groupId, Guid userId, bool add)
    {
        var existing = await db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);
        if (add && existing == null) db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId });
        else if (!add && existing != null) db.GroupMembers.Remove(existing);
        await db.SaveChangesAsync();
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

[Authorize]
public class OrgController(AppDbContext db, OrgService orgs) : Controller
{
    public async Task<IActionResult> Index()
    {
        var all = await db.Orgs.OrderBy(o => o.BuCode).ToListAsync();
        ViewBag.Children = all.Where(o => o.ParentId != null).GroupBy(o => o.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return View(all);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? name, Guid? parentId, string? remark)
    {
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Cần mã đơn vị."; return RedirectToAction(nameof(Index)); }
        var c = code.Trim();
        if (await db.Orgs.AnyAsync(o => o.Code == c)) { TempData["Error"] = "Mã đơn vị đã tồn tại."; return RedirectToAction(nameof(Index)); }
        if (parentId != null && !await db.Orgs.AnyAsync(o => o.Id == parentId)) { TempData["Error"] = "Đơn vị cha không tồn tại."; return RedirectToAction(nameof(Index)); }
        db.Orgs.Add(new Org { Code = c, Name = string.IsNullOrWhiteSpace(name) ? c : name!.Trim(), ParentId = parentId, Remark = remark });
        await db.SaveChangesAsync();
        await orgs.RebuildPathsAsync();   // cập nhật BuCode/BuPattern/Level cho cả cây
        TempData["Success"] = "Đã tạo đơn vị tổ chức.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var o = await db.Orgs.FirstOrDefaultAsync(x => x.Id == id);
        if (o != null) { o.IsActive = !o.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
