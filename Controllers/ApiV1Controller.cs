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
public class ApiV1Controller(AppDbContext db, ICache cache, RbacService rbac, AccountSecurityService security, DataScopeService scope, ModuleService modules, ViewGroupService viewGroups, UserTeamService teams, SessionService sessions, SelfServiceService selfService, CheckDbService checkDb) : ControllerBase
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
        => Ok((await db.Users.OrderBy(u => u.Email).ToListAsync()).Select(u => new { u.Id, u.Email, u.FullName, roles = u.RoleList, u.Tenant, u.IsActive, u.CreatedAt,
            u.FailedLoginCount, u.IsLockedOut, u.LockoutDate, u.LockoutUntil, u.LastLoginAt, u.IsSysAdmin, u.OrgId }));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] UserReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrWhiteSpace(r.Password))
            return BadRequest(new { error = "Cần email và mật khẩu." });
        var email = r.Email.Trim().ToLowerInvariant();
        // Kiểm tra theo mẫu CheckDB (↔ SysUserCheckDB): người dùng PHẢI CHƯA tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.User, email, CheckDbService.FlagInactive);
        if (!chk.Ok) return BadRequest(new { error = chk.Error });
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

    // Cập nhật hồ sơ người dùng theo DANH SÁCH CỘT CHO PHÉP (port từ iNOS.InBrand:
    // SysUserManager.SysUserUpdateX — mẫu "Ft_Cols_Upd"). Chỉ các cột có tên trong `columns`
    // mới được ghi; các cột khác giữ nguyên. `columns` rỗng/null = cập nhật tất cả cột cho phép.
    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UserUpdateReq r, UserProfileService profiles)
    {
        var patch = new UserProfilePatch(r.Email, r.FullName, r.Roles, r.Tenant, r.IsActive, r.IsSysAdmin, r.OrgId);
        var res = await profiles.UpdateAsync(id, patch, r.Columns);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true });
    }

    // ── Bảo mật tài khoản (port từ iNOS.InBrand SysUser: Lockout / LockoutDate / ResetPass) ──
    // Khoá/mở khoá tài khoản thủ công (admin) — tương ứng SysUser.Lockout.
    [HttpPost("users/{id:guid}/lock")]
    public async Task<IActionResult> LockUser(Guid id)
    {
        if (!await security.LockAsync(id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true, isLockedOut = true });
    }

    [HttpPost("users/{id:guid}/unlock")]
    public async Task<IActionResult> UnlockUser(Guid id)
    {
        if (!await security.UnlockAsync(id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true, isLockedOut = false });
    }

    // Đặt lại mật khẩu (admin) — tương ứng SysUser.ResetPass: băm mật khẩu mới + mở khoá.
    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordReq r)
    {
        if (string.IsNullOrWhiteSpace(r.NewPassword) || r.NewPassword.Length < 6)
            return BadRequest(new { error = "Mật khẩu mới phải từ 6 ký tự." });
        if (!await security.ResetPasswordAsync(id, r.NewPassword)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true });
    }

    // Tự đổi mật khẩu (port từ iNOS.InBrand: AccountController.ChangePassword + SysUserManager.ResetPassword).
    // Người dùng tự đổi mật khẩu của chính mình: phải khớp mật khẩu hiện tại + xác nhận mật khẩu mới.
    [HttpPost("users/{id:guid}/change-password")]
    public async Task<IActionResult> ChangeOwnPassword(Guid id, [FromBody] ChangePasswordReq r)
    {
        var res = await selfService.ChangeOwnPasswordAsync(id, r.CurrentPassword, r.NewPassword, r.ConfirmPassword);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true });
    }

    // Nhật ký đăng nhập gần đây (thành công/thất bại) — phục vụ truy vết bảo mật.
    [HttpGet("login-attempts")]
    public async Task<IActionResult> LoginAttempts([FromQuery] int take = 100)
        => Ok((await security.RecentAttemptsAsync(take)).Select(a => new { a.Email, a.UserId, a.Success, a.Reason, a.RemoteIp, a.AttemptedAt }));

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
    public async Task<IActionResult> LicenseCheck([FromBody] LicenseCheckReq request)
    {
        var licenseKey = (request.LicenseKey ?? "").Trim();
        var appSlug = (request.AppSlug ?? "").Trim().ToLowerInvariant();
        var license = string.IsNullOrWhiteSpace(licenseKey) ? null : await db.Licenses.FirstOrDefaultAsync(lic => lic.LicenseKey == licenseKey);
        bool isValid = license != null && license.IsActive && (license.ExpiresAt == null || license.ExpiresAt > DateTime.UtcNow);
        var resultMessage = license == null ? "License key không tồn tại."
            : !license.IsActive ? "License đã bị khoá."
            : license.ExpiresAt <= DateTime.UtcNow ? "License đã hết hạn."
            : "OK.";
        db.LicenseCheckLogs.Add(new LicenseCheckLog
        {
            LicenseKey = licenseKey, AppSlug = appSlug, InstanceHost = request.InstanceHost,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(), Result = isValid, Message = resultMessage
        });
        await db.SaveChangesAsync();
        return Ok(new { valid = isValid, message = resultMessage, owner = isValid ? license!.OwnerName : null });
    }

    [HttpGet("license/logs")]
    public async Task<IActionResult> LicenseLogs([FromQuery] string? appSlug, [FromQuery] int take = 100)
    {
        var logsQuery = db.LicenseCheckLogs.OrderByDescending(log => log.CheckedAt).AsQueryable();
        if (!string.IsNullOrWhiteSpace(appSlug)) logsQuery = logsQuery.Where(log => log.AppSlug == appSlug.Trim().ToLowerInvariant());
        var logs = await logsQuery.Take(Math.Clamp(take, 1, 500)).ToListAsync();
        return Ok(logs.Select(log => new { log.AppSlug, log.InstanceHost, log.RemoteIp, log.Result, log.Message, log.CheckedAt }));
    }

    // ── RBAC theo nhóm (port từ iNOS.InBrand: Sys_Group / Sys_UserInGroup / Sys_Access / Sys_Object) ──
    [HttpGet("groups")]
    public async Task<IActionResult> Groups()
    {
        var groups = await db.Groups.OrderBy(g => g.Code).ToListAsync();
        var memberCounts = await db.GroupMembers.GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var accessCounts = await db.GroupAccesses.GroupBy(a => a.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return Ok(groups.Select(g => new
        {
            g.Id, g.Code, g.Name, g.Description, g.IsActive,
            members = memberCounts.GetValueOrDefault(g.Id), permissions = accessCounts.GetValueOrDefault(g.Id)
        }));
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] GroupReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return BadRequest(new { error = "Cần mã nhóm." });
        var code = r.Code.Trim().ToUpperInvariant();
        // Kiểm tra theo mẫu CheckDB (↔ SysGroupCheckDB): nhóm PHẢI CHƯA tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.Group, code, CheckDbService.FlagInactive);
        if (!chk.Ok) return BadRequest(new { error = chk.Error });
        var g = new Group { Code = code, Name = string.IsNullOrWhiteSpace(r.Name) ? code : r.Name!.Trim(), Description = r.Description };
        db.Groups.Add(g); await db.SaveChangesAsync();
        return Ok(new { id = g.Id });
    }

    [HttpPost("groups/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleGroup(Guid id)
    {
        var g = await db.Groups.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound(new { error = "Không tìm thấy." });
        g.IsActive = !g.IsActive; await db.SaveChangesAsync();
        return Ok(new { ok = true, isActive = g.IsActive });
    }

    // Cập nhật hồ sơ nhóm theo DANH SÁCH CỘT CHO PHÉP (port từ iNOS.InBrand:
    // SysGroupManager.SysGroupUpdateX — mẫu "Ft_Cols_Upd"). Chỉ các cột có tên trong `columns`
    // mới được ghi; các cột khác giữ nguyên. `columns` rỗng/null = cập nhật tất cả cột cho phép.
    [HttpPut("groups/{id:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] GroupUpdateReq r, GroupProfileService profiles)
    {
        var patch = new GroupProfilePatch(r.Name, r.Description, r.IsActive, r.OrgId);
        var res = await profiles.UpdateAsync(id, patch, r.Columns);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true });
    }

    // Danh sách nhóm của 1 người dùng (↔ SysGroupProvider.GetAllByUser).
    [HttpGet("users/{id:guid}/groups")]
    public async Task<IActionResult> UserGroups(Guid id, GroupProfileService profiles)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        var list = await profiles.GroupsOfUserAsync(id);
        return Ok(list.Select(g => new { g.Id, g.Code, g.Name, g.Description, g.IsActive, g.OrgId }));
    }

    [HttpGet("objects")]
    public async Task<IActionResult> Objects()
        => Ok((await db.PermissionObjects.OrderBy(o => o.Code).ToListAsync()).Select(o => new { o.Id, o.Code, o.Name, o.Module, o.IsActive }));

    // Cấp/thu quyền đối tượng cho nhóm (Sys_Access).
    [HttpPost("groups/{id:guid}/access")]
    public async Task<IActionResult> SetGroupAccess(Guid id, [FromBody] GroupAccessReq r)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound(new { error = "Không tìm thấy nhóm." });
        var obj = await db.PermissionObjects.FirstOrDefaultAsync(o => o.Code == r.ObjectCode);
        if (obj == null) return BadRequest(new { error = "Đối tượng quyền không tồn tại." });
        var existing = await db.GroupAccesses.FirstOrDefaultAsync(a => a.GroupId == id && a.ObjectId == obj.Id);
        if (r.Grant && existing == null) db.GroupAccesses.Add(new GroupAccess { GroupId = id, ObjectId = obj.Id });
        else if (!r.Grant && existing != null) db.GroupAccesses.Remove(existing);
        await db.SaveChangesAsync();
        return Ok(new { ok = true, granted = r.Grant });
    }

    // Thêm/bớt thành viên nhóm (Sys_UserInGroup).
    [HttpPost("groups/{id:guid}/members")]
    public async Task<IActionResult> SetGroupMember(Guid id, [FromBody] GroupMemberReq r)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound(new { error = "Không tìm thấy nhóm." });
        if (!await db.Users.AnyAsync(u => u.Id == r.UserId)) return BadRequest(new { error = "Người dùng không tồn tại." });
        var existing = await db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == id && m.UserId == r.UserId);
        if (r.Add && existing == null) db.GroupMembers.Add(new GroupMember { GroupId = id, UserId = r.UserId });
        else if (!r.Add && existing != null) db.GroupMembers.Remove(existing);
        await db.SaveChangesAsync();
        return Ok(new { ok = true, added = r.Add });
    }

    // Quyền hiệu lực của 1 người dùng (hợp các đối tượng quyền qua mọi nhóm đang hoạt động).
    [HttpGet("users/{id:guid}/permissions")]
    public async Task<IActionResult> UserPermissions(Guid id)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { userId = id, groups = await rbac.GroupCodesAsync(id), permissions = await rbac.EffectivePermissionsAsync(id) });
    }

    // ── Gán thành viên/quyền theo nhóm — thay thế toàn bộ (port từ iNOS.InBrand) ──
    // iNOS lưu thành viên/quyền của nhóm theo cơ chế "xoá sạch rồi ghi lại" (SysUserInGroupSave /
    // SysAccessSave), kèm ràng buộc thành viên phải cùng đơn vị với nhóm (InvalidDLCode).
    [HttpPut("groups/{id:guid}/members")]
    public async Task<IActionResult> ReplaceGroupMembers(Guid id, [FromBody] GroupMembersReq r, GroupService groups)
    {
        var res = await groups.SetMembersAsync(id, r.UserIds ?? []);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true, count = (r.UserIds ?? []).Distinct().Count() });
    }

    [HttpPut("groups/{id:guid}/access")]
    public async Task<IActionResult> ReplaceGroupAccess(Guid id, [FromBody] GroupAccessSetReq r, GroupService groups)
    {
        var res = await groups.SetAccessAsync(id, r.ObjectCodes ?? []);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true, count = (r.ObjectCodes ?? []).Distinct().Count() });
    }

    // Xoá nhóm kèm dọn thành viên + cấp quyền (↔ SysGroupManager.Remove).
    [HttpDelete("groups/{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id, GroupService groups)
    {
        if (!await groups.DeleteGroupAsync(id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true });
    }

    // ── Truy vấn thành viên nhóm (port từ iNOS.InBrand: SysUserProvider.GetAllUserByGroupCode /
    //    GetAllUserNotInGroup + SysUserInGroupProvider.RemoveByUser) ──
    // Danh sách người dùng đang thuộc 1 nhóm (↔ GetAllUserByGroupCode).
    [HttpGet("groups/{id:guid}/members")]
    public async Task<IActionResult> GroupMembers(Guid id, GroupService groups)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound(new { error = "Không tìm thấy nhóm." });
        var members = await groups.MembersOfGroupAsync(id);
        return Ok(members.Select(u => new { u.Id, u.Email, u.FullName, u.IsActive }));
    }

    // Danh sách người dùng CHƯA thuộc bất kỳ nhóm nào (↔ GetAllUserNotInGroup).
    [HttpGet("users-not-in-group")]
    public async Task<IActionResult> UsersNotInGroup(GroupService groups)
    {
        var users = await groups.UsersNotInAnyGroupAsync();
        return Ok(users.Select(u => new { u.Id, u.Email, u.FullName, u.IsActive }));
    }

    // Gỡ 1 người dùng khỏi mọi nhóm (↔ SysUserInGroupProvider.RemoveByUser).
    [HttpDelete("users/{id:guid}/groups")]
    public async Task<IActionResult> RemoveUserFromGroups(Guid id, GroupService groups)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { error = "Không tìm thấy người dùng." });
        var removed = await groups.RemoveUserFromAllGroupsAsync(id);
        return Ok(new { ok = true, removed });
    }

    // ── Cây tổ chức (port từ iNOS.InBrand: Mst_Org) ──
    [HttpGet("orgs")]
    public async Task<IActionResult> Orgs()
        => Ok((await db.Orgs.OrderBy(o => o.BuCode).ToListAsync())
            .Select(o => new { o.Id, o.Code, o.Name, o.ParentId, o.BuCode, o.BuPattern, o.Level, o.Remark, o.IsActive }));

    [HttpPost("orgs")]
    public async Task<IActionResult> CreateOrg([FromBody] OrgReq r, OrgService orgs)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return BadRequest(new { error = "Cần mã đơn vị." });
        var code = r.Code.Trim();
        // Kiểm tra theo mẫu CheckDB (↔ MstDealerCheckDB): đơn vị PHẢI CHƯA tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.Org, code, CheckDbService.FlagInactive);
        if (!chk.Ok) return BadRequest(new { error = chk.Error });
        if (r.ParentId != null && !await db.Orgs.AnyAsync(o => o.Id == r.ParentId)) return BadRequest(new { error = "Đơn vị cha không tồn tại." });
        var o = new Org { Code = code, Name = string.IsNullOrWhiteSpace(r.Name) ? code : r.Name!.Trim(), ParentId = r.ParentId, Remark = r.Remark };
        db.Orgs.Add(o); await db.SaveChangesAsync();
        await orgs.RebuildPathsAsync();
        return Ok(new { id = o.Id, buCode = o.BuCode, level = o.Level });
    }

    // Toàn bộ nhánh con (kể cả chính nó) của 1 đơn vị — dựa trên tiền tố BuPattern.
    [HttpGet("orgs/{id:guid}/subtree")]
    public async Task<IActionResult> OrgSubtree(Guid id, OrgService orgs)
    {
        if (!await db.Orgs.AnyAsync(o => o.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        var nodes = await orgs.SubtreeAsync(id);
        return Ok(nodes.Select(o => new { o.Id, o.Code, o.Name, o.BuCode, o.Level }));
    }

    // ── Phạm vi dữ liệu (port từ iNOS.InBrand: SysUserProvider "ViewAbility") ──
    // Gắn người dùng vào 1 đơn vị tổ chức + cờ SysAdmin (tương ứng SysUser.DLCode / SysUser.SysAdmin).
    [HttpPost("users/{id:guid}/scope")]
    public async Task<IActionResult> SetUserScope(Guid id, [FromBody] UserScopeReq r)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound(new { error = "Không tìm thấy." });
        if (r.OrgId != null && !await db.Orgs.AnyAsync(o => o.Id == r.OrgId))
            return BadRequest(new { error = "Đơn vị tổ chức không tồn tại." });
        u.IsSysAdmin = r.IsSysAdmin;
        u.OrgId = r.OrgId;
        await db.SaveChangesAsync();
        return Ok(new { ok = true, isSysAdmin = u.IsSysAdmin, orgId = u.OrgId });
    }

    // Phạm vi dữ liệu hiệu lực của 1 người dùng: danh sách đơn vị họ được phép thấy.
    [HttpGet("users/{id:guid}/scope")]
    public async Task<IActionResult> UserScope(Guid id)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound(new { error = "Không tìm thấy." });
        var visible = await scope.VisibleOrgsAsync(id);
        var unrestricted = await scope.VisibleOrgIdsAsync(id) == null;
        return Ok(new
        {
            userId = id, u.IsSysAdmin, u.OrgId, unrestricted,
            visibleOrgs = visible.Select(o => new { o.Id, o.Code, o.Name, o.BuCode, o.Level })
        });
    }

    // Kiểm tra 1 người dùng có được thấy 1 đơn vị tổ chức hay không (↔ myCache_ViewAbility_CheckAccessDealer).
    [HttpGet("users/{id:guid}/scope/check")]
    public async Task<IActionResult> CheckScope(Guid id, [FromQuery] Guid orgId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        var allowed = await scope.CanAccessOrgAsync(id, orgId);
        return Ok(new { userId = id, orgId, allowed });
    }

    // ── Phân hệ chức năng: Module → Function (port từ iNOS.InBrand: SysModule / SysFunction / SysFunctionInModule) ──
    // Cây module + chức năng con; "menu hiệu lực" của người dùng suy ra từ quyền nhóm (SysModuleManager.GetAllByUser).
    [HttpGet("modules")]
    public async Task<IActionResult> Modules()
    {
        var list = await modules.AllWithFunctionsAsync();
        return Ok(list.Select(x => new
        {
            x.Module.Id, x.Module.Code, x.Module.Title, x.Module.Description, x.Module.ModuleType,
            x.Module.ParentId, x.Module.SortOrder, x.Module.IsActive,
            functions = x.Functions.Select(f => new { f.Id, f.Code, f.Description })
        }));
    }

    [HttpPost("modules")]
    public async Task<IActionResult> CreateModule([FromBody] ModuleReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return BadRequest(new { error = "Cần mã module." });
        var code = r.Code.Trim();
        // Kiểm tra theo mẫu CheckDB (↔ MstModuleCheckDB): module PHẢI CHƯA tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.Module, code, CheckDbService.FlagInactive);
        if (!chk.Ok) return BadRequest(new { error = chk.Error });
        if (r.ParentId != null && !await db.Modules.AnyAsync(m => m.Id == r.ParentId)) return BadRequest(new { error = "Module cha không tồn tại." });
        var m = new Module { Code = code, Title = string.IsNullOrWhiteSpace(r.Title) ? code : r.Title!.Trim(), Description = r.Description, ModuleType = r.ModuleType, ParentId = r.ParentId, SortOrder = r.SortOrder };
        db.Modules.Add(m); await db.SaveChangesAsync();
        return Ok(new { id = m.Id });
    }

    // Toàn bộ nhánh con (kể cả chính nó) của 1 module.
    [HttpGet("modules/{id:guid}/subtree")]
    public async Task<IActionResult> ModuleSubtree(Guid id)
    {
        if (!await db.Modules.AnyAsync(m => m.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        var nodes = await modules.SubtreeAsync(id);
        return Ok(nodes.Select(m => new { m.Id, m.Code, m.Title, m.ParentId, m.SortOrder }));
    }

    // Thay thế toàn bộ chức năng của module (↔ SysFunctionInModule save).
    [HttpPut("modules/{id:guid}/functions")]
    public async Task<IActionResult> SetModuleFunctions(Guid id, [FromBody] ModuleFunctionsReq r)
    {
        var res = await modules.SetFunctionsAsync(id, r.FunctionCodes ?? []);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true, count = (r.FunctionCodes ?? []).Distinct().Count() });
    }

    // Menu hiệu lực của 1 người dùng (↔ SysModuleManager.GetAllByUser).
    [HttpGet("users/{id:guid}/menu")]
    public async Task<IActionResult> UserMenu(Guid id)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        var list = await modules.MenuForUserAsync(id);
        return Ok(new
        {
            userId = id,
            modules = list.Select(x => new
            {
                x.Module.Code, x.Module.Title, x.Module.ModuleType, x.Module.ParentId, x.Module.SortOrder,
                functions = x.Functions.Select(f => f.Code)
            })
        });
    }

    // ── Gán module trực tiếp cho nhóm (port từ iNOS.InBrand: Sys_Access = GroupCode + ModuleCode) ──
    // Màn hình SysGroupController.GetSysModule: liệt kê TẤT CẢ module kèm cờ "đã gán cho nhóm này chưa"
    // (SysAccessService.GetAllAccessByGroupCode); SaveModuleInGroup → SysAccessSave_New20171101 lưu thay-thế.
    [HttpGet("groups/{id:guid}/modules")]
    public async Task<IActionResult> GroupModules(Guid id)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound(new { error = "Không tìm thấy nhóm." });
        var list = await modules.ModulesForGroupAsync(id);
        return Ok(list.Select(x => new { x.Module.Id, x.Module.Code, x.Module.Title, x.Module.ModuleType, x.Module.ParentId, x.Module.SortOrder, x.Granted }));
    }

    // Mã các module ĐANG được gán cho nhóm (↔ GetAllAccessByGroupCode).
    [HttpGet("groups/{id:guid}/module-codes")]
    public async Task<IActionResult> GroupModuleCodes(Guid id)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound(new { error = "Không tìm thấy nhóm." });
        return Ok(new { groupId = id, moduleCodes = await modules.GrantedModuleCodesAsync(id) });
    }

    // Thay thế toàn bộ module gán cho nhóm (↔ SysAccessSave_New20171101, clear-all → insert-all).
    [HttpPut("groups/{id:guid}/modules")]
    public async Task<IActionResult> SetGroupModules(Guid id, [FromBody] GroupModulesReq r)
    {
        var res = await modules.SetGroupModulesAsync(id, r.ModuleCodes ?? []);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true, count = (r.ModuleCodes ?? []).Distinct().Count() });
    }

    // ── Nhóm cột hiển thị: ViewGroupView → ViewColumnInGroup → ViewColumnView (port từ iNOS.InBrand) ──
    // Cấu hình "cột hiển thị" và gom thành nhóm; lưu nhóm theo cơ chế thay-thế toàn bộ (ViewColumnInGroupSaveX).
    [HttpGet("view-columns")]
    public async Task<IActionResult> ViewColumns()
        => Ok((await db.ViewColumnViews.OrderBy(c => c.Code).ToListAsync())
            .Select(c => new { c.Id, c.Code, c.Name, c.Remark, c.IsActive }));

    [HttpPost("view-columns")]
    public async Task<IActionResult> CreateViewColumn([FromBody] ViewColumnReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return BadRequest(new { error = "Cần mã cột hiển thị." });
        var code = r.Code.Trim();
        if (await db.ViewColumnViews.AnyAsync(c => c.Code == code)) return BadRequest(new { error = "Mã cột hiển thị đã tồn tại." });
        var c = new ViewColumnView { Code = code, Name = string.IsNullOrWhiteSpace(r.Name) ? code : r.Name!.Trim(), Remark = r.Remark };
        db.ViewColumnViews.Add(c); await db.SaveChangesAsync();
        return Ok(new { id = c.Id });
    }

    [HttpPost("view-columns/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleViewColumn(Guid id)
    {
        var c = await db.ViewColumnViews.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { error = "Không tìm thấy." });
        c.IsActive = !c.IsActive; await db.SaveChangesAsync();
        return Ok(new { ok = true, isActive = c.IsActive });
    }

    [HttpGet("view-groups")]
    public async Task<IActionResult> ViewGroups()
    {
        var list = await viewGroups.AllWithColumnsAsync();
        return Ok(list.Select(x => new
        {
            x.Group.Id, x.Group.Code, x.Group.Name, x.Group.Remark, x.Group.IsActive,
            columns = x.Columns.Select(c => new { c.Id, c.Code, c.Name })
        }));
    }

    [HttpPost("view-groups")]
    public async Task<IActionResult> CreateViewGroup([FromBody] ViewGroupReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return BadRequest(new { error = "Cần mã nhóm cột hiển thị." });
        var code = r.Code.Trim();
        if (await db.ViewGroupViews.AnyAsync(g => g.Code == code)) return BadRequest(new { error = "Mã nhóm cột hiển thị đã tồn tại." });
        var g = new ViewGroupView { Code = code, Name = string.IsNullOrWhiteSpace(r.Name) ? code : r.Name!.Trim(), Remark = r.Remark };
        db.ViewGroupViews.Add(g); await db.SaveChangesAsync();
        return Ok(new { id = g.Id });
    }

    [HttpPost("view-groups/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleViewGroup(Guid id)
    {
        var g = await db.ViewGroupViews.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound(new { error = "Không tìm thấy." });
        g.IsActive = !g.IsActive; await db.SaveChangesAsync();
        return Ok(new { ok = true, isActive = g.IsActive });
    }

    // Thay thế toàn bộ cột của nhóm (↔ ViewColumnInGroupSaveX).
    [HttpPut("view-groups/{id:guid}/columns")]
    public async Task<IActionResult> SetViewGroupColumns(Guid id, [FromBody] ViewGroupColumnsReq r)
    {
        var res = await viewGroups.SetColumnsAsync(id, r.ColumnCodes ?? []);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true, count = (r.ColumnCodes ?? []).Distinct().Count() });
    }

    // Xoá nhóm cột hiển thị kèm dọn liên kết cột (↔ ViewColumnInGroupSaveX nhánh FlagIsDelete).
    [HttpDelete("view-groups/{id:guid}")]
    public async Task<IActionResult> DeleteViewGroup(Guid id)
    {
        if (!await viewGroups.DeleteGroupAsync(id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true });
    }

    // ── Đội người dùng: Sys_UserTeam (port từ iNOS.InBrand) ──
    // "Đội" thuộc 1 đơn vị tổ chức (DLCode) — khác với Group (nhóm quyền).
    [HttpGet("user-teams")]
    public async Task<IActionResult> UserTeams([FromQuery] Guid? orgId)
    {
        var list = orgId == null ? await teams.AllAsync() : await teams.ByOrgAsync(orgId.Value);
        return Ok(list.Select(t => new { t.Id, t.Code, t.Name, t.OrgId, t.IsActive, t.CreatedAt }));
    }

    [HttpPost("user-teams")]
    public async Task<IActionResult> CreateUserTeam([FromBody] UserTeamReq r)
    {
        var res = await teams.CreateAsync(r.Code, r.Name, r.OrgId);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true });
    }

    [HttpPut("user-teams/{id:guid}")]
    public async Task<IActionResult> UpdateUserTeam(Guid id, [FromBody] UserTeamReq r)
    {
        var res = await teams.UpdateAsync(id, r.Name, r.OrgId);
        if (!res.Ok) return BadRequest(new { error = res.Error });
        return Ok(new { ok = true });
    }

    [HttpPost("user-teams/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleUserTeam(Guid id)
    {
        if (!await teams.ToggleAsync(id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true });
    }

    [HttpDelete("user-teams/{id:guid}")]
    public async Task<IActionResult> DeleteUserTeam(Guid id)
    {
        if (!await teams.DeleteAsync(id)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true });
    }

    // ── Phiên làm việc hiệu lực: SysSession / GlobSession (port từ iNOS.InBrand) ──
    // Ảnh chụp quyền hiệu lực của người dùng (IsSysAdmin + module/chức năng + bối cảnh đơn vị/kho).
    [HttpGet("users/{id:guid}/session")]
    public async Task<IActionResult> UserSession(Guid id, SessionService sessions)
    {
        var snap = await sessions.BuildAsync(id);
        if (snap == null) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new
        {
            userId = snap.UserId, snap.Email, snap.FullName, snap.IsSysAdmin,
            snap.OrgId, snap.OrgName, snap.InvCode,
            modules = snap.ModuleCodes, functions = snap.FunctionCodes
        });
    }

    // Tạo + lưu 1 phiên (↔ GlobSessionManager.Add) — chụp quyền hiệu lực tại thời điểm tạo.
    [HttpPost("users/{id:guid}/session")]
    public async Task<IActionResult> CreateUserSession(Guid id, [FromQuery] int? ttlMinutes)
    {
        var ttl = ttlMinutes == null ? (TimeSpan?)null : TimeSpan.FromMinutes(Math.Clamp(ttlMinutes.Value, 1, 1440));
        var s = await sessions.CreateAsync(id, ttl);
        if (s == null) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { sessionId = s.SessionId, s.UserId, s.IsSysAdmin, s.OrgId, s.OrgName, s.InvCode, s.CreatedAt, s.ExpiresAt });
    }

    // Kiểm tra phiên có quyền với module/chức năng (↔ SysSession.HasModule/HasFunction).
    [HttpGet("users/{id:guid}/session/check")]
    public async Task<IActionResult> CheckSession(Guid id, [FromQuery] string? module, [FromQuery] string? function)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { error = "Không tìm thấy." });
        var res = await sessions.CheckAsync(id, module, function);
        return Ok(new { userId = id, module, function, allowed = res.Allowed, reason = res.Reason });
    }

    // Danh sách phiên gần đây (mới nhất trước) — phục vụ màn hình quản trị.
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromQuery] int take = 100)
        => Ok((await sessions.RecentAsync(take)).Select(s => new { s.SessionId, s.UserId, s.IsSysAdmin, s.OrgId, s.OrgName, s.InvCode, s.CreatedAt, s.ExpiresAt, s.IsActive }));

    // Kết thúc 1 phiên (↔ đăng xuất).
    [HttpPost("sessions/{sessionId}/end")]
    public async Task<IActionResult> EndSession(string sessionId)
    {
        if (!await sessions.EndAsync(sessionId)) return NotFound(new { error = "Không tìm thấy." });
        return Ok(new { ok = true });
    }

    // ── Kiểm tra tồn tại/trạng thái trước khi lưu (port từ iNOS.InBrand: mẫu "CheckDB") ──
    // iNOS dùng cùng một mẫu ở mọi manager (SysUserCheckDB / SysGroupCheckDB / MstModuleCheckDB /
    // MstDealerCheckDB): "1" = phải tồn tại, "0" = phải chưa tồn tại, và trạng thái thực tế phải
    // nằm trong danh sách cờ hoạt động cho phép. Endpoint này phơi mẫu kiểm tra đó ra API.
    [HttpGet("checkdb")]
    public async Task<IActionResult> CheckDb([FromQuery] string kind, [FromQuery] string? code,
        [FromQuery] string? exist, [FromQuery] string? active, CheckDbService checkDb)
    {
        if (!Enum.TryParse<CheckDbService.EntityKind>(kind, ignoreCase: true, out var k))
            return BadRequest(new { error = "Loại không hợp lệ (User/Group/Module/Org)." });
        var res = await checkDb.CheckAsync(k, code, exist ?? "", active ?? "");
        return Ok(new { kind = k.ToString(), code, res.Ok, res.Exists, res.Status, res.Error });
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
// Cập nhật hồ sơ theo cột cho phép (↔ SysUserUpdateX / Ft_Cols_Upd). Columns = danh sách cột được ghi.
public class UserUpdateReq { public string? Email { get; set; } public string? FullName { get; set; } public string? Roles { get; set; } public string? Tenant { get; set; } public bool IsActive { get; set; } = true; public bool IsSysAdmin { get; set; } public Guid? OrgId { get; set; } public List<string>? Columns { get; set; } }
public class ClientReq { public string ClientId { get; set; } = ""; public string? Name { get; set; } public string? RedirectUris { get; set; } public string? Grants { get; set; } public string? Scopes { get; set; } public string? Secret { get; set; } public bool RequirePkce { get; set; } = true; }
public class LicenseCheckReq { public string? LicenseKey { get; set; } public string? AppSlug { get; set; } public string? InstanceHost { get; set; } }
public class GroupReq { public string Code { get; set; } = ""; public string? Name { get; set; } public string? Description { get; set; } }
// Cập nhật hồ sơ nhóm theo cột cho phép (↔ SysGroupUpdateX / Ft_Cols_Upd). Columns = danh sách cột được ghi.
public class GroupUpdateReq { public string? Name { get; set; } public string? Description { get; set; } public bool IsActive { get; set; } = true; public Guid? OrgId { get; set; } public List<string>? Columns { get; set; } }
public class GroupAccessReq { public string ObjectCode { get; set; } = ""; public bool Grant { get; set; } = true; }
public class GroupMemberReq { public Guid UserId { get; set; } public bool Add { get; set; } = true; }
public class GroupMembersReq { public List<Guid>? UserIds { get; set; } }
public class GroupAccessSetReq { public List<string>? ObjectCodes { get; set; } }
public class OrgReq { public string Code { get; set; } = ""; public string? Name { get; set; } public Guid? ParentId { get; set; } public string? Remark { get; set; } }
public class UserScopeReq { public bool IsSysAdmin { get; set; } public Guid? OrgId { get; set; } }
public class ResetPasswordReq { public string NewPassword { get; set; } = ""; }
public class ChangePasswordReq { public string? CurrentPassword { get; set; } public string? NewPassword { get; set; } public string? ConfirmPassword { get; set; } }
public class ModuleReq { public string Code { get; set; } = ""; public string? Title { get; set; } public string? Description { get; set; } public string? ModuleType { get; set; } public Guid? ParentId { get; set; } public int SortOrder { get; set; } }
public class ModuleFunctionsReq { public List<string>? FunctionCodes { get; set; } }
public class GroupModulesReq { public List<string>? ModuleCodes { get; set; } }
public class ViewColumnReq { public string Code { get; set; } = ""; public string? Name { get; set; } public string? Remark { get; set; } }
public class ViewGroupReq { public string Code { get; set; } = ""; public string? Name { get; set; } public string? Remark { get; set; } }
public class ViewGroupColumnsReq { public List<string>? ColumnCodes { get; set; } }
public class UserTeamReq { public string Code { get; set; } = ""; public string? Name { get; set; } public Guid? OrgId { get; set; } }
