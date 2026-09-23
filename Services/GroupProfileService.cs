using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Cập nhật hồ sơ NHÓM theo DANH SÁCH CỘT CHO PHÉP (port từ iNOS.InBrand:
/// SysGroupManager.SysGroupUpdateX — mẫu "Ft_Cols_Upd").
///
/// iNOS KHÔNG cập nhật toàn bộ bản ghi khi sửa nhóm. Mỗi lần Update, tầng trên truyền vào một chuỗi
/// <c>objFt_Cols_Upd</c> liệt kê các cột được phép ghi (trong SysGroupUpdateX là
/// "Sys_Group.Description, Sys_Group.Enable, Sys_Group.FlagActive"); manager đọc bản ghi hiện có rồi
/// CHỈ gán lại những cột có trong danh sách (bUpd_Description, bUpd_Enable, bUpd_FlagActive), các cột
/// khác giữ nguyên. Nhờ vậy nhiều màn hình khác nhau có thể sửa các tập trường khác nhau trên cùng 1
/// nhóm mà không ghi đè lẫn nhau.
///
/// MiniSSO trước đây chỉ có bật/tắt (IsActive) và tạo nhóm — KHÔNG có luồng "sửa hồ sơ nhóm" tổng quát
/// (UserProfileService đã có cho người dùng nhưng chưa có bản tương ứng cho nhóm). Service này tái tạo
/// đúng ngữ nghĩa cập nhật-theo-cột của iNOS trên các trường tương ứng của <see cref="Group"/>.
/// </summary>
public sealed class GroupProfileService(AppDbContext db)
{
    /// <summary>
    /// Tên các cột hồ sơ nhóm có thể cập nhật (tương ứng các cột trong objFt_Cols_Upd của iNOS:
    /// Sys_Group.Description / Sys_Group.Enable / Sys_Group.FlagActive, cộng thêm Name/OrgId của MiniSSO).
    /// Dùng làm danh sách trắng: cột ngoài danh sách này bị bỏ qua.
    /// </summary>
    public static readonly string[] UpdatableColumns =
    [
        "Name", "Description", "IsActive", "OrgId"
    ];

    /// <summary>
    /// Cập nhật hồ sơ 1 nhóm, chỉ ghi các cột có trong <paramref name="columns"/>
    /// (↔ SysGroupUpdateX: bUpd_* theo strFt_Cols_Upd). <paramref name="columns"/> rỗng/null = cập nhật
    /// tất cả cột cho phép (ngữ nghĩa "rỗng = tất cả" cho tiện dùng, ghi rõ trong tài liệu).
    ///
    /// Ràng buộc (↔ SysGroupCheckDB với strFlagExistToCheck = "1"): nhóm PHẢI tồn tại.
    /// Nếu cập nhật OrgId: đơn vị phải tồn tại và đang hoạt động (↔ MstDealerCheckDB).
    /// </summary>
    public async Task<GroupResult> UpdateAsync(Guid groupId, GroupProfilePatch patch, IEnumerable<string>? columns = null)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return GroupResult.Fail("Không tìm thấy nhóm.");

        // Tập cột được phép ghi: rỗng = tất cả cột cho phép.
        var cols = columns == null
            ? new HashSet<string>(UpdatableColumns, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(columns.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
        if (cols.Count == 0) cols = new HashSet<string>(UpdatableColumns, StringComparer.OrdinalIgnoreCase);

        bool Upd(string name) => cols.Contains(name);

        // ── Kiểm tra trước khi ghi (↔ SysGroupCheckDB + MstDealerCheckDB) ──
        if (Upd("OrgId") && patch.OrgId != null)
        {
            var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == patch.OrgId);
            if (org == null) return GroupResult.Fail("Đơn vị tổ chức không tồn tại.");
            if (!org.IsActive) return GroupResult.Fail("Đơn vị tổ chức đang bị khoá.");
        }

        // ── Ghi các cột có trong danh sách (↔ if (bUpd_X) { objSysGroupDB.X = objSysGroup.X; }) ──
        if (Upd("Name"))
        {
            var name = (patch.Name ?? "").Trim();
            group.Name = string.IsNullOrWhiteSpace(name) ? group.Code : name;
        }
        if (Upd("Description")) group.Description = string.IsNullOrWhiteSpace(patch.Description) ? null : patch.Description!.Trim();
        if (Upd("IsActive")) group.IsActive = patch.IsActive;
        if (Upd("OrgId")) group.OrgId = patch.OrgId;

        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>
    /// Danh sách nhóm mà 1 người dùng thuộc về (↔ SysGroupProvider.GetAllByUser).
    /// iNOS dùng truy vấn này để dựng "nhóm của người dùng" (SysModuleManager.GetAllByUser gọi lại nó
    /// để suy ra menu hiệu lực). Trả về rỗng nếu người dùng không tồn tại hoặc không thuộc nhóm nào.
    /// </summary>
    public async Task<List<Group>> GroupsOfUserAsync(Guid userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return [];
        var groupIds = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        if (groupIds.Count == 0) return [];
        return await db.Groups.Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Code).ToListAsync();
    }
}

/// <summary>
/// Giá trị mới cho các cột hồ sơ nhóm (tương ứng các trường của SysGroup trong SysGroupUpdateX).
/// Chỉ những cột có tên trong danh sách cột cho phép mới thực sự được ghi.
/// </summary>
public sealed record GroupProfilePatch(
    string? Name,
    string? Description,
    bool IsActive,
    Guid? OrgId);
