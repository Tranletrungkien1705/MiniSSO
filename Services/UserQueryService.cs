using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Truy vấn danh sách người dùng theo đơn vị / theo trạng thái hoạt động
/// (port từ iNOS.InBrand: SysUserService.GetByDLCode + GetAllByEnable).
///
/// iNOS KHÔNG có 1 hàm "lấy tất cả người dùng" duy nhất: mỗi màn hình gọi một truy vấn có điều kiện:
///   • GetByDLCode(sessionId, user)  → danh sách người dùng thuộc MỘT đại lý/đơn vị
///     (searchDic.Add("SysUser.DLCode =", user.DLCode)), kèm GẮN NHÓM cho từng người
///     (ReturnTableDict có cả "SysUser" và "SysGroup").
///   • GetAllByEnable(sessionId, enable = "True") → danh sách người dùng lọc theo cờ hoạt động
///     (searchDic.Add("SysUser.Enable =", enable)). Màn hình "Gán người dùng vào nhóm"
///     (SysGroupController.GetSysUser) dùng đúng truy vấn này để chỉ liệt kê người dùng ĐANG hoạt động.
///
/// MiniSSO trước đây chỉ có danh sách phẳng (db.Users.ToListAsync) và các truy vấn theo NHÓM
/// (GroupService.MembersOfGroupAsync / UsersNotInAnyGroupAsync) — KHÔNG có truy vấn theo ĐƠN VỊ
/// và KHÔNG lọc theo trạng thái hoạt động. Bổ sung đúng 2 truy vấn của iNOS.
/// </summary>
public sealed class UserQueryService(AppDbContext db)
{
    /// <summary>
    /// Danh sách người dùng thuộc 1 đơn vị tổ chức (↔ SysUserService.GetByDLCode), kèm nhóm của từng người.
    /// Trả về rỗng nếu đơn vị không tồn tại. Sắp theo email.
    /// </summary>
    public async Task<List<UserSearchRow>> ByOrgAsync(Guid orgId)
    {
        if (!await db.Orgs.AnyAsync(o => o.Id == orgId)) return [];

        var users = await db.Users.Where(u => u.OrgId == orgId).OrderBy(u => u.Email).ToListAsync();
        var rows = new List<UserSearchRow>(users.Count);
        foreach (var u in users)
            rows.Add(new UserSearchRow(u, await GroupsOfUserAsync(u.Id)));
        return rows;
    }

    /// <summary>
    /// Danh sách người dùng lọc theo trạng thái hoạt động (↔ SysUserService.GetAllByEnable).
    /// <paramref name="isActive"/> = true → chỉ người đang hoạt động (Enable = "True", mặc định của iNOS);
    /// false → chỉ người bị vô hiệu hoá; null → tất cả. Sắp theo email.
    /// </summary>
    public async Task<List<AppUser>> ByActiveAsync(bool? isActive = true)
    {
        var query = db.Users.AsQueryable();
        if (isActive != null)
            query = query.Where(u => u.IsActive == isActive.Value);
        return await query.OrderBy(u => u.Email).ToListAsync();
    }

    /// <summary>Danh sách nhóm của 1 người dùng (↔ SysGroupProvider.GetAllByUser), sắp theo mã.</summary>
    private async Task<List<Group>> GroupsOfUserAsync(Guid userId)
    {
        var groupIds = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        return await db.Groups.Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Code).ToListAsync();
    }
}
