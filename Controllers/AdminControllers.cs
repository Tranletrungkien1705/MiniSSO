using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;

namespace MiniSSO.Controllers;

[Authorize]
public class UserController(AppDbContext db, AccountSecurityService security) : Controller
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

    // Khoá/mở khoá tài khoản (port từ iNOS.InBrand SysUser.Lockout).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(Guid id)
    {
        await security.LockAsync(id);
        TempData["Success"] = "Đã khoá tài khoản.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(Guid id)
    {
        await security.UnlockAsync(id);
        TempData["Success"] = "Đã mở khoá tài khoản.";
        return RedirectToAction(nameof(Index));
    }

    // Đặt lại mật khẩu (port từ iNOS.InBrand SysUser.ResetPass).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(Guid id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        { TempData["Error"] = "Mật khẩu mới phải từ 6 ký tự."; return RedirectToAction(nameof(Index)); }
        await security.ResetPasswordAsync(id, newPassword);
        TempData["Success"] = "Đã đặt lại mật khẩu.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class GroupController(AppDbContext db, GroupService groups) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Objects = await db.PermissionObjects.OrderBy(o => o.Code).ToListAsync();
        ViewBag.Users = await db.Users.OrderBy(u => u.Email).ToListAsync();
        ViewBag.Members = await db.GroupMembers.ToListAsync();
        ViewBag.Access = await db.GroupAccesses.ToListAsync();
        ViewBag.Orgs = await db.Orgs.OrderBy(o => o.BuCode).ToListAsync();
        return View(await db.Groups.OrderBy(g => g.Code).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? name, string? description, Guid? orgId)
    {
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Cần mã nhóm."; return RedirectToAction(nameof(Index)); }
        var c = code.Trim().ToUpperInvariant();
        if (await db.Groups.AnyAsync(g => g.Code == c)) { TempData["Error"] = "Mã nhóm đã tồn tại."; return RedirectToAction(nameof(Index)); }
        if (orgId != null && !await db.Orgs.AnyAsync(o => o.Id == orgId)) { TempData["Error"] = "Đơn vị không tồn tại."; return RedirectToAction(nameof(Index)); }
        db.Groups.Add(new Group { Code = c, Name = string.IsNullOrWhiteSpace(name) ? c : name!.Trim(), Description = description, OrgId = orgId });
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

    // Lưu toàn bộ thành viên nhóm theo cơ chế thay-thế (port từ iNOS SysUserInGroupSave).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMembers(Guid groupId, Guid[]? userIds)
    {
        var res = await groups.SetMembersAsync(groupId, userIds ?? []);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã lưu thành viên nhóm.";
        return RedirectToAction(nameof(Index));
    }

    // Lưu toàn bộ quyền của nhóm theo cơ chế thay-thế (port từ iNOS SysAccessSave).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAccess(Guid groupId, string[]? objectCodes)
    {
        var res = await groups.SetAccessAsync(groupId, objectCodes ?? []);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã lưu quyền nhóm.";
        return RedirectToAction(nameof(Index));
    }

    // Xoá nhóm kèm dọn thành viên + cấp quyền (port từ iNOS SysGroupManager.Remove).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await groups.DeleteGroupAsync(id)) TempData["Success"] = "Đã xoá nhóm.";
        else TempData["Error"] = "Không tìm thấy nhóm.";
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

// Phạm vi dữ liệu (port từ iNOS.InBrand: SysUserProvider "ViewAbility").
[Authorize]
public class DataScopeController(AppDbContext db, DataScopeService scope) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = await db.Users.OrderBy(u => u.Email).ToListAsync();
        var orgs = await db.Orgs.OrderBy(o => o.BuCode).ToListAsync();
        var orgById = orgs.ToDictionary(o => o.Id);

        // Phạm vi hiệu lực của từng người dùng (danh sách đơn vị được thấy).
        var visible = new Dictionary<Guid, List<Org>>();
        foreach (var u in users)
            visible[u.Id] = await scope.VisibleOrgsAsync(u.Id);

        ViewBag.Orgs = orgs;
        ViewBag.OrgById = orgById;
        ViewBag.Visible = visible;
        return View(users);
    }

    // Gắn người dùng vào 1 đơn vị tổ chức + cờ SysAdmin (tương ứng SysUser.DLCode / SysUser.SysAdmin).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetScope(Guid userId, Guid? orgId, bool isSysAdmin)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null) { TempData["Error"] = "Không tìm thấy người dùng."; return RedirectToAction(nameof(Index)); }
        if (orgId != null && !await db.Orgs.AnyAsync(o => o.Id == orgId))
        { TempData["Error"] = "Đơn vị tổ chức không tồn tại."; return RedirectToAction(nameof(Index)); }
        u.IsSysAdmin = isSysAdmin;
        u.OrgId = orgId;
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật phạm vi dữ liệu.";
        return RedirectToAction(nameof(Index));
    }
}

// Phân hệ chức năng: cây Module → Function (port từ iNOS.InBrand: SysModule / SysFunction / SysFunctionInModule).
[Authorize]
public class ModuleController(AppDbContext db, ModuleService modules) : Controller
{
    public async Task<IActionResult> Index()
    {
        var all = await db.Modules.OrderBy(m => m.SortOrder).ThenBy(m => m.Code).ToListAsync();
        var links = await db.FunctionInModules.ToListAsync();
        var functions = await db.Functions.OrderBy(f => f.Code).ToListAsync();
        var fnById = functions.ToDictionary(f => f.Id);

        ViewBag.Modules = all;
        ViewBag.Functions = functions;
        ViewBag.ModuleFunctions = links
            .Where(l => fnById.ContainsKey(l.FunctionId))
            .GroupBy(l => l.ModuleId)
            .ToDictionary(g => g.Key, g => g.Select(l => fnById[l.FunctionId].Code).ToHashSet());
        return View(all);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? title, string? description, string? moduleType, Guid? parentId, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Cần mã module."; return RedirectToAction(nameof(Index)); }
        var c = code.Trim();
        if (await db.Modules.AnyAsync(m => m.Code == c)) { TempData["Error"] = "Mã module đã tồn tại."; return RedirectToAction(nameof(Index)); }
        if (parentId != null && !await db.Modules.AnyAsync(m => m.Id == parentId)) { TempData["Error"] = "Module cha không tồn tại."; return RedirectToAction(nameof(Index)); }
        db.Modules.Add(new Module { Code = c, Title = string.IsNullOrWhiteSpace(title) ? c : title!.Trim(), Description = description, ModuleType = moduleType, ParentId = parentId, SortOrder = sortOrder });
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo module.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var m = await db.Modules.FirstOrDefaultAsync(x => x.Id == id);
        if (m != null) { m.IsActive = !m.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    // Lưu toàn bộ chức năng của module theo cơ chế thay-thế (port từ iNOS SysFunctionInModule save).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFunctions(Guid moduleId, string[]? functionCodes)
    {
        var res = await modules.SetFunctionsAsync(moduleId, functionCodes ?? []);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã lưu chức năng module.";
        return RedirectToAction(nameof(Index));
    }
}

// Nhóm cột hiển thị: ViewGroupView → ViewColumnInGroup → ViewColumnView (port từ iNOS.InBrand).
[Authorize]
public class ViewGroupController(AppDbContext db, ViewGroupService viewGroups) : Controller
{
    public async Task<IActionResult> Index()
    {
        var groups = await db.ViewGroupViews.OrderBy(g => g.Code).ToListAsync();
        var columns = await db.ViewColumnViews.OrderBy(c => c.Code).ToListAsync();
        var links = await db.ViewColumnInGroups.ToListAsync();
        var colById = columns.ToDictionary(c => c.Id);

        ViewBag.Columns = columns;
        ViewBag.GroupColumns = links
            .Where(l => colById.ContainsKey(l.ColumnViewId))
            .GroupBy(l => l.GroupViewId)
            .ToDictionary(g => g.Key, g => g.Select(l => colById[l.ColumnViewId].Code).ToHashSet());
        return View(groups);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? name, string? remark)
    {
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Cần mã nhóm cột hiển thị."; return RedirectToAction(nameof(Index)); }
        var c = code.Trim();
        if (await db.ViewGroupViews.AnyAsync(g => g.Code == c)) { TempData["Error"] = "Mã nhóm cột hiển thị đã tồn tại."; return RedirectToAction(nameof(Index)); }
        db.ViewGroupViews.Add(new ViewGroupView { Code = c, Name = string.IsNullOrWhiteSpace(name) ? c : name!.Trim(), Remark = remark });
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo nhóm cột hiển thị.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateColumn(string code, string? name, string? remark)
    {
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Cần mã cột hiển thị."; return RedirectToAction(nameof(Index)); }
        var c = code.Trim();
        if (await db.ViewColumnViews.AnyAsync(x => x.Code == c)) { TempData["Error"] = "Mã cột hiển thị đã tồn tại."; return RedirectToAction(nameof(Index)); }
        db.ViewColumnViews.Add(new ViewColumnView { Code = c, Name = string.IsNullOrWhiteSpace(name) ? c : name!.Trim(), Remark = remark });
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo cột hiển thị.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var g = await db.ViewGroupViews.FirstOrDefaultAsync(x => x.Id == id);
        if (g != null) { g.IsActive = !g.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleColumn(Guid id)
    {
        var c = await db.ViewColumnViews.FirstOrDefaultAsync(x => x.Id == id);
        if (c != null) { c.IsActive = !c.IsActive; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    // Lưu toàn bộ cột của nhóm theo cơ chế thay-thế (port từ iNOS ViewColumnInGroupSaveX).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveColumns(Guid groupViewId, string[]? columnCodes)
    {
        var res = await viewGroups.SetColumnsAsync(groupViewId, columnCodes ?? []);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã lưu cột hiển thị của nhóm.";
        return RedirectToAction(nameof(Index));
    }

    // Xoá nhóm cột hiển thị kèm dọn liên kết cột (port từ iNOS ViewColumnInGroupSaveX nhánh FlagIsDelete).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await viewGroups.DeleteGroupAsync(id)) TempData["Success"] = "Đã xoá nhóm cột hiển thị.";
        else TempData["Error"] = "Không tìm thấy nhóm cột hiển thị.";
        return RedirectToAction(nameof(Index));
    }
}

// Đội người dùng: Sys_UserTeam (port từ iNOS.InBrand) — đội thuộc 1 đơn vị tổ chức.
[Authorize]
public class UserTeamController(AppDbContext db, UserTeamService teams) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Orgs = await db.Orgs.OrderBy(o => o.BuCode).ToListAsync();
        return View(await teams.AllAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? name, Guid? orgId)
    {
        var res = await teams.CreateAsync(code, name, orgId);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã tạo đội.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string? name, Guid? orgId)
    {
        var res = await teams.UpdateAsync(id, name, orgId);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã cập nhật đội.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        await teams.ToggleAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await teams.DeleteAsync(id)) TempData["Success"] = "Đã xoá đội.";
        else TempData["Error"] = "Không tìm thấy đội.";
        return RedirectToAction(nameof(Index));
    }
}

// Phiên làm việc hiệu lực: SysSession / GlobSession (port từ iNOS.InBrand).
[Authorize]
public class SessionController(AppDbContext db, SessionService sessions) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = await db.Users.OrderBy(u => u.Email).ToListAsync();
        var recent = await sessions.RecentAsync(50);
        var userById = users.ToDictionary(u => u.Id);

        // Ảnh chụp phiên hiệu lực của từng người dùng (module/chức năng + bối cảnh đơn vị).
        var snapshots = new Dictionary<Guid, SessionSnapshot>();
        foreach (var u in users)
        {
            var snap = await sessions.BuildAsync(u.Id);
            if (snap != null) snapshots[u.Id] = snap;
        }

        ViewBag.Users = users;
        ViewBag.UserById = userById;
        ViewBag.Snapshots = snapshots;
        return View(recent);
    }

    // Tạo 1 phiên cho người dùng (↔ GlobSessionManager.Add).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid userId)
    {
        var s = await sessions.CreateAsync(userId);
        if (s == null) TempData["Error"] = "Không tìm thấy người dùng.";
        else TempData["Success"] = $"Đã tạo phiên {s.SessionId[..8]}…";
        return RedirectToAction(nameof(Index));
    }

    // Kết thúc 1 phiên (↔ đăng xuất).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> End(string sessionId)
    {
        if (await sessions.EndAsync(sessionId)) TempData["Success"] = "Đã kết thúc phiên.";
        else TempData["Error"] = "Không tìm thấy phiên.";
        return RedirectToAction(nameof(Index));
    }

    // Kết thúc mọi phiên đang hoạt động của 1 người dùng (↔ SysUserLogOutX).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EndAll(Guid userId)
    {
        var n = await sessions.EndAllForUserAsync(userId);
        TempData["Success"] = $"Đã kết thúc {n} phiên.";
        return RedirectToAction(nameof(Index));
    }
}
