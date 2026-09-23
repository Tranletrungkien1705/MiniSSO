using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Kết quả 1 thao tác gán nhóm (kèm lý do khi bị từ chối).</summary>
public sealed record GroupResult(bool Ok, string? Error)
{
    public static GroupResult Success() => new(true, null);
    public static GroupResult Fail(string error) => new(false, error);
}

/// <summary>
/// Quản lý thành viên &amp; quyền của nhóm (port từ iNOS.InBrand:
/// SysUserInGroupManager.SysUserInGroupSave_New20171101 + SysAccessManager.SysAccessSave_New20171101
/// + SysGroupManager.Remove/SysGroupDeleteX).
///
/// iNOS KHÔNG thêm/bớt từng dòng một khi lưu thành viên/quyền của nhóm, mà dùng cơ chế
/// "xoá sạch rồi ghi lại" (clear-all → insert-all): mỗi lần lưu, toàn bộ thành viên (hoặc
/// quyền) hiện có của nhóm bị xoá và thay bằng đúng tập mới gửi lên. MiniSSO trước đây chỉ
/// có thêm/bớt từng dòng (SetGroupMember/SetGroupAccess) — bổ sung đúng ngữ nghĩa thay-thế.
///
/// Ngoài ra iNOS ràng buộc: thành viên thêm vào nhóm phải CÙNG đơn vị (DLCode) với nhóm
/// (Sys_UserInGroup_Save_InputTblDtl_InvalidDLCode). MiniSSO tái tạo ràng buộc này trên
/// <see cref="AppUser.OrgId"/> ↔ <see cref="Group.OrgId"/>.
/// </summary>
public sealed class GroupService(AppDbContext db)
{
    /// <summary>
    /// Thay thế toàn bộ thành viên của nhóm (↔ SysUserInGroupSave_New20171101).
    /// Xoá hết thành viên hiện có rồi ghi lại đúng tập <paramref name="userIds"/>.
    /// Ràng buộc: mọi người dùng phải tồn tại và (nếu nhóm có gắn đơn vị) phải cùng đơn vị với nhóm.
    /// </summary>
    public async Task<GroupResult> SetMembersAsync(Guid groupId, IEnumerable<Guid> userIds)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return GroupResult.Fail("Không tìm thấy nhóm.");

        var ids = userIds.Distinct().ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        if (users.Count != ids.Count) return GroupResult.Fail("Có người dùng không tồn tại.");

        // Ràng buộc cùng đơn vị (↔ Sys_UserInGroup_Save_InputTblDtl_InvalidDLCode):
        // chỉ áp dụng khi nhóm được gắn đơn vị; người dùng không gắn đơn vị cũng bị coi là khác đơn vị.
        if (group.OrgId != null)
        {
            var mismatch = users.FirstOrDefault(u => u.OrgId != group.OrgId);
            if (mismatch != null)
                return GroupResult.Fail($"Người dùng '{mismatch.Email}' không cùng đơn vị với nhóm.");
        }

        // Clear-all → insert-all.
        var existing = await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync();
        db.GroupMembers.RemoveRange(existing);
        foreach (var uid in ids)
            db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = uid });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>
    /// Thay thế toàn bộ quyền (đối tượng quyền) của nhóm (↔ SysAccessSave_New20171101).
    /// Xoá hết cấp quyền hiện có rồi ghi lại đúng tập <paramref name="objectCodes"/>.
    /// </summary>
    public async Task<GroupResult> SetAccessAsync(Guid groupId, IEnumerable<string> objectCodes)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return GroupResult.Fail("Không tìm thấy nhóm.");

        var codes = objectCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
        var objs = await db.PermissionObjects.Where(o => codes.Contains(o.Code)).ToListAsync();
        if (objs.Count != codes.Count) return GroupResult.Fail("Có đối tượng quyền không tồn tại.");

        // Clear-all → insert-all.
        var existing = await db.GroupAccesses.Where(a => a.GroupId == groupId).ToListAsync();
        db.GroupAccesses.RemoveRange(existing);
        foreach (var o in objs)
            db.GroupAccesses.Add(new GroupAccess { GroupId = groupId, ObjectId = o.Id });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>
    /// Xoá nhóm kèm dọn dẹp liên quan (↔ SysGroupManager.Remove → SysGroupDeleteX + RemoveBySysGroup):
    /// xoá mọi thành viên (Sys_UserInGroup) và cấp quyền (Sys_Access) của nhóm trước khi xoá nhóm.
    /// </summary>
    public async Task<bool> DeleteGroupAsync(Guid groupId)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return false;

        db.GroupMembers.RemoveRange(await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync());
        db.GroupAccesses.RemoveRange(await db.GroupAccesses.Where(a => a.GroupId == groupId).ToListAsync());
        db.Groups.Remove(group);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Truy vấn thành viên nhóm (port từ iNOS.InBrand: SysUserProvider.GetAllUserByGroupCode /
    //    GetAllUserNotInGroup + SysUserInGroupProvider.RemoveByUser) ──
    // iNOS dùng 2 truy vấn này cho màn hình "Gán người dùng vào nhóm" (SysGroupController.GetSysUser):
    //   • GetAllUserByGroupCode: danh sách người dùng ĐANG thuộc 1 nhóm (để tích sẵn),
    //   • GetAllUserNotInGroup:  danh sách người dùng CHƯA thuộc bất kỳ nhóm nào.
    // MiniSSO trước đây chỉ thêm/bớt/thay-thế thành viên mà KHÔNG truy vấn được "ai đang trong nhóm".

    /// <summary>
    /// Danh sách người dùng đang thuộc 1 nhóm (↔ SysUserProvider.GetAllUserByGroupCode).
    /// Trả về rỗng nếu nhóm không tồn tại.
    /// </summary>
    public async Task<List<AppUser>> MembersOfGroupAsync(Guid groupId)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return [];
        var userIds = await db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
        return await db.Users.Where(u => userIds.Contains(u.Id)).OrderBy(u => u.Email).ToListAsync();
    }

    /// <summary>
    /// Danh sách người dùng CHƯA thuộc bất kỳ nhóm nào (↔ SysUserProvider.GetAllUserNotInGroup).
    /// </summary>
    public async Task<List<AppUser>> UsersNotInAnyGroupAsync()
    {
        var memberIds = await db.GroupMembers.Select(m => m.UserId).Distinct().ToListAsync();
        return await db.Users.Where(u => !memberIds.Contains(u.Id)).OrderBy(u => u.Email).ToListAsync();
    }

    /// <summary>
    /// Gỡ 1 người dùng khỏi MỌI nhóm (↔ SysUserInGroupProvider.RemoveByUser) — dùng khi xoá người dùng.
    /// Trả về số liên kết đã gỡ.
    /// </summary>
    public async Task<int> RemoveUserFromAllGroupsAsync(Guid userId)
    {
        var links = await db.GroupMembers.Where(m => m.UserId == userId).ToListAsync();
        if (links.Count == 0) return 0;
        db.GroupMembers.RemoveRange(links);
        await db.SaveChangesAsync();
        return links.Count;
    }
}
