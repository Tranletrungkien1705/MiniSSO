using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;

namespace MiniSSO.Controllers;

[Authorize]
public class UserController(AppDbContext db, AccountSecurityService security, UserProfileService profiles, UserDeleteService userDelete) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Orgs = await db.Orgs.OrderBy(o => o.BuCode).ToListAsync();
        return View(await db.Users.OrderBy(u => u.Email).ToListAsync());
    }

    // Cập nhật hồ sơ theo DANH SÁCH CỘT CHO PHÉP (port từ iNOS.InBrand: SysUserUpdateX / Ft_Cols_Upd).
    // Form gửi kèm các cột người dùng chọn sửa; chỉ những cột đó được ghi, cột khác giữ nguyên.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string? email, string? fullName, string? roles, string? tenant,
        bool isActive, bool isSysAdmin, Guid? orgId, string[]? columns)
    {
        var patch = new UserProfilePatch(email, fullName, roles, tenant, isActive, isSysAdmin, orgId);
        var res = await profiles.UpdateAsync(id, patch, columns);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã cập nhật hồ sơ người dùng.";
        return RedirectToAction(nameof(Index));
    }

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

    // Xoá người dùng kèm dọn liên kết nhóm (port từ iNOS.InBrand: SysUserManager.Remove →
    // SysUserDeleteX + SysUserInGroupProvider.RemoveByUser).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var res = await userDelete.DeleteAsync(id);
        if (!res.Ok) TempData["Error"] = res.Error;
        else TempData["Success"] = $"Đã xoá người dùng (gỡ khỏi {res.RemovedGroupLinks} nhóm).";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class GroupController(AppDbContext db, GroupService groups, GroupProfileService profiles) : Controller
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

    // Cập nhật hồ sơ nhóm theo DANH SÁCH CỘT CHO PHÉP (port từ iNOS.InBrand: SysGroupUpdateX / Ft_Cols_Upd).
    // Form gửi kèm các cột người dùng chọn sửa; chỉ những cột đó được ghi, cột khác giữ nguyên.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string? name, string? description, bool isActive, Guid? orgId, string[]? columns)
    {
        var patch = new GroupProfilePatch(name, description, isActive, orgId);
        var res = await profiles.UpdateAsync(id, patch, columns);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã cập nhật hồ sơ nhóm.";
        return RedirectToAction(nameof(Index));
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

// Kiểm tra tồn tại/trạng thái trước khi lưu (port từ iNOS.InBrand: mẫu "CheckDB").
// Màn hình cho phép chạy thử mẫu kiểm tra dùng chung của iNOS trên 4 loại thực thể.
[Authorize]
public class CheckDbController(AppDbContext db, CheckDbService checkDb) : Controller
{
    public async Task<IActionResult> Index(string? kind, string? code, string? exist, string? active)
    {
        ViewBag.Kinds = Enum.GetNames<CheckDbService.EntityKind>();
        ViewBag.Kind = kind ?? "User";
        ViewBag.Code = code ?? "";
        ViewBag.Exist = exist ?? "";
        ViewBag.Active = active ?? "";

        // Gợi ý mã để thử nhanh (mã thật đang có trong hệ thống).
        ViewBag.Samples = new Dictionary<string, List<string>>
        {
            ["User"] = await db.Users.OrderBy(u => u.Email).Select(u => u.Email).Take(10).ToListAsync(),
            ["Group"] = await db.Groups.OrderBy(g => g.Code).Select(g => g.Code).Take(10).ToListAsync(),
            ["Module"] = await db.Modules.OrderBy(m => m.Code).Select(m => m.Code).Take(10).ToListAsync(),
            ["Org"] = await db.Orgs.OrderBy(o => o.Code).Select(o => o.Code).Take(10).ToListAsync(),
        };

        if (!string.IsNullOrWhiteSpace(code) && Enum.TryParse<CheckDbService.EntityKind>(kind ?? "User", ignoreCase: true, out var k))
            ViewBag.Result = await checkDb.CheckAsync(k, code, exist ?? "", active ?? "");
        return View();
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

// Thành viên nhóm (port từ iNOS.InBrand: SysUserProvider.GetAllUserByGroupCode / GetAllUserNotInGroup
// + SysUserInGroupProvider.RemoveByUser). Màn hình "Gán người dùng vào nhóm" của iNOS (SysGroupController.GetSysUser)
// cho biết ai ĐANG trong nhóm và ai CHƯA thuộc nhóm nào; MiniSSO trước đây chỉ thêm/bớt/thay-thế mà không truy vấn được.
[Authorize]
public class GroupMemberController(AppDbContext db, GroupService groups) : Controller
{
    public async Task<IActionResult> Index(Guid? groupId)
    {
        var allGroups = await db.Groups.OrderBy(g => g.Code).ToListAsync();
        var selected = groupId != null ? allGroups.FirstOrDefault(g => g.Id == groupId) : allGroups.FirstOrDefault();

        ViewBag.Groups = allGroups;
        ViewBag.Selected = selected;
        ViewBag.Members = selected != null ? await groups.MembersOfGroupAsync(selected.Id) : new List<AppUser>();
        ViewBag.NotInAnyGroup = await groups.UsersNotInAnyGroupAsync();
        return View();
    }

    // Gỡ 1 người dùng khỏi mọi nhóm (↔ SysUserInGroupProvider.RemoveByUser).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFromAllGroups(Guid userId, Guid? groupId)
    {
        var n = await groups.RemoveUserFromAllGroupsAsync(userId);
        TempData["Success"] = $"Đã gỡ người dùng khỏi {n} nhóm.";
        return RedirectToAction(nameof(Index), new { groupId });
    }
}

// Nhập hàng loạt nhóm từ file (port từ iNOS.InBrand: SysGroupController.Import).
// iNOS đọc file Excel (từ ô A2) và kiểm tra toàn bộ trước khi ghi: đúng số cột, không rỗng,
// mô tả ≤ 400 ký tự, mã không lặp trong file. MiniSSO dùng CSV (không kèm thư viện Excel) —
// quy tắc nghiệp vụ giữ nguyên, chỉ khác định dạng đầu vào.
[Authorize]
public class GroupImportController(AppDbContext db, GroupImportService importer) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Groups = await db.Groups.OrderBy(g => g.Code).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string? content)
    {
        var res = await importer.ImportCsvAsync(content);
        if (!res.Ok) TempData["Error"] = res.Error;
        else TempData["Success"] = $"Đã nhập {res.Imported} nhóm từ file.";
        return RedirectToAction(nameof(Index));
    }
}

// Xuất danh sách nhóm ra file (port từ iNOS.InBrand: SysGroupController.Export + ExportTemplate).
// iNOS xuất Excel theo 2 chế độ: dữ liệu thật (Code, DLCode, Description, Enable) và file mẫu rỗng
// (Code, DLCode, Description). MiniSSO trả về CSV (không kèm thư viện Excel) — tập cột giữ nguyên.
[Authorize]
public class GroupExportController(AppDbContext db, GroupExportService exporter) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Groups = await db.Groups.OrderBy(g => g.Code).ToListAsync();
        ViewBag.DataHeaders = GroupExportService.DataHeaders;
        ViewBag.TemplateHeaders = GroupExportService.TemplateHeaders;
        return View();
    }

    // Xuất dữ liệu thật (↔ SysGroupController.Export).
    public async Task<IActionResult> Export()
    {
        var csv = await exporter.ExportCsvAsync();
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "SysGroup.csv");
    }

    // Xuất file mẫu rỗng (↔ SysGroupController.ExportTemplate).
    public IActionResult ExportTemplate()
    {
        var csv = exporter.ExportTemplateCsv();
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "SysGroup_Template.csv");
    }
}

// Tìm kiếm có phân trang + lọc phạm vi dữ liệu (port từ iNOS.InBrand:
// SysUserManager.Search + SysGroupManager.Search). Màn hình cho chọn "người gọi" để thấy
// kết quả bị lọc theo phạm vi dữ liệu của người đó (SysAdmin/nút gốc → thấy tất cả).
[Authorize]
public class SearchController(AppDbContext db, SearchService search) : Controller
{
    public async Task<IActionResult> Index(Guid? callerId, string? q, bool? active, int page = 0, string? kind = "users")
    {
        var users = await db.Users.OrderBy(u => u.Email).ToListAsync();
        var caller = callerId != null ? users.FirstOrDefault(u => u.Id == callerId) : users.FirstOrDefault();

        ViewBag.Users = users;
        ViewBag.Caller = caller;
        ViewBag.Q = q ?? "";
        ViewBag.Active = active;
        ViewBag.Kind = kind == "groups" ? "groups" : "users";

        if (caller != null)
        {
            if (ViewBag.Kind == "groups")
                ViewBag.GroupResult = await search.SearchGroupsAsync(caller.Id, q, active, page);
            else
                ViewBag.UserResult = await search.SearchUsersAsync(caller.Id, q, active, page);
        }
        return View();
    }
}

// Gán module trực tiếp cho nhóm (port từ iNOS.InBrand: Sys_Access = GroupCode + ModuleCode).
// Màn hình "Gán module vào nhóm" của iNOS (SysGroupController.GetSysModule) liệt kê TẤT CẢ module
// kèm cờ "đã gán cho nhóm này chưa" (SysAccessService.GetAllAccessByGroupCode); SaveModuleInGroup →
// SysAccessSave_New20171101 lưu theo cơ chế thay-thế toàn bộ (clear-all → insert-all).
[Authorize]
public class GroupModuleController(AppDbContext db, ModuleService modules) : Controller
{
    public async Task<IActionResult> Index(Guid? groupId)
    {
        var allGroups = await db.Groups.OrderBy(g => g.Code).ToListAsync();
        var selected = groupId != null ? allGroups.FirstOrDefault(g => g.Id == groupId) : allGroups.FirstOrDefault();

        ViewBag.Groups = allGroups;
        ViewBag.Selected = selected;
        ViewBag.Modules = selected != null ? await modules.ModulesForGroupAsync(selected.Id) : new List<ModuleGrant>();
        return View();
    }

    // Lưu toàn bộ module gán cho nhóm theo cơ chế thay-thế (↔ SysAccessSave_New20171101).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveModules(Guid groupId, string[]? moduleCodes)
    {
        var res = await modules.SetGroupModulesAsync(groupId, moduleCodes ?? []);
        if (!res.Ok) TempData["Error"] = res.Error; else TempData["Success"] = "Đã lưu module cho nhóm.";
        return RedirectToAction(nameof(Index), new { groupId });
    }
}
