using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Cập nhật hồ sơ người dùng theo DANH SÁCH CỘT CHO PHÉP (port từ iNOS.InBrand:
/// SysUserManager.SysUserUpdateX / SysUserUpdateX_New20190917 — mẫu "Ft_Cols_Upd").
///
/// iNOS KHÔNG cập nhật toàn bộ bản ghi khi sửa người dùng. Mỗi lần Update, tầng trên truyền vào
/// một chuỗi <c>objFt_Cols_Upd</c> liệt kê các cột được phép ghi (vd "Sys_User.Email, Sys_User.FullName,
/// Sys_User.Enable, ..."); manager đọc bản ghi hiện có rồi CHỈ gán lại những cột có trong danh sách
/// (bUpd_Email, bUpd_FullName, …), các cột khác giữ nguyên. Nhờ vậy nhiều màn hình khác nhau có thể
/// sửa các tập trường khác nhau trên cùng 1 người dùng mà không ghi đè lẫn nhau.
///
/// MiniSSO trước đây chỉ có bật/tắt (IsActive), khoá/mở khoá, đặt lại mật khẩu và gắn phạm vi —
/// KHÔNG có luồng "sửa hồ sơ" tổng quát. Service này tái tạo đúng ngữ nghĩa cập nhật-theo-cột của iNOS
/// trên các trường tương ứng của <see cref="AppUser"/>.
/// </summary>
public sealed class UserProfileService(AppDbContext db)
{
    /// <summary>
    /// Tên các cột hồ sơ có thể cập nhật (tương ứng các cột trong objFt_Cols_Upd của iNOS).
    /// Dùng làm danh sách trắng: cột ngoài danh sách này bị bỏ qua.
    /// </summary>
    public static readonly string[] UpdatableColumns =
    [
        "Email", "FullName", "Roles", "Tenant", "IsActive", "IsSysAdmin", "OrgId"
    ];

    /// <summary>
    /// Cập nhật hồ sơ 1 người dùng, chỉ ghi các cột có trong <paramref name="columns"/>
    /// (↔ SysUserUpdateX: bUpd_* theo strFt_Cols_Upd). <paramref name="columns"/> rỗng/null = cập nhật
    /// tất cả cột cho phép (giống iNOS khi Ft_Cols_Upd rỗng thì mọi bUpd_* đều false — ở đây chọn ngữ nghĩa
    /// "rỗng = tất cả" cho tiện dùng, và ghi rõ trong tài liệu).
    ///
    /// Ràng buộc (↔ SysUserCheckDB với strFlagExistToCheck = "1"): người dùng PHẢI tồn tại.
    /// Nếu cập nhật Email: email mới phải không rỗng và chưa bị người khác dùng (Email là duy nhất).
    /// Nếu cập nhật OrgId: đơn vị phải tồn tại và đang hoạt động (↔ MstDealerCheckDB).
    /// </summary>
    public async Task<GroupResult> UpdateAsync(Guid userId, UserProfilePatch patch, IEnumerable<string>? columns = null)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return GroupResult.Fail("Không tìm thấy người dùng.");

        // Tập cột được phép ghi: rỗng = tất cả cột cho phép.
        var cols = columns == null
            ? new HashSet<string>(UpdatableColumns, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(columns.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
        if (cols.Count == 0) cols = new HashSet<string>(UpdatableColumns, StringComparer.OrdinalIgnoreCase);

        bool Upd(string name) => cols.Contains(name);

        // ── Kiểm tra trước khi ghi (↔ SysUserCheckDB + ràng buộc duy nhất của Email) ──
        if (Upd("Email"))
        {
            var email = (patch.Email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email)) return GroupResult.Fail("Email không được để trống.");
            if (await db.Users.AnyAsync(u => u.Id != userId && u.Email.ToLower() == email))
                return GroupResult.Fail("Email đã được người dùng khác sử dụng.");
            user.Email = email;
        }

        if (Upd("OrgId") && patch.OrgId != null)
        {
            var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == patch.OrgId);
            if (org == null) return GroupResult.Fail("Đơn vị tổ chức không tồn tại.");
            if (!org.IsActive) return GroupResult.Fail("Đơn vị tổ chức đang bị khoá.");
        }

        // ── Ghi các cột có trong danh sách (↔ if (bUpd_X) { objSysUserDB.X = objSysUser.X; }) ──
        if (Upd("FullName")) user.FullName = (patch.FullName ?? "").Trim();
        if (Upd("Roles")) user.Roles = (patch.Roles ?? "").Trim();
        if (Upd("Tenant")) user.Tenant = string.IsNullOrWhiteSpace(patch.Tenant) ? null : patch.Tenant!.Trim();
        if (Upd("IsActive")) user.IsActive = patch.IsActive;
        if (Upd("IsSysAdmin")) user.IsSysAdmin = patch.IsSysAdmin;
        if (Upd("OrgId")) user.OrgId = patch.OrgId;

        await db.SaveChangesAsync();
        return GroupResult.Success();
    }
}

/// <summary>
/// Giá trị mới cho các cột hồ sơ người dùng (tương ứng các trường của SysUser trong SysUserUpdateX).
/// Chỉ những cột có tên trong danh sách cột cho phép mới thực sự được ghi.
/// </summary>
public sealed record UserProfilePatch(
    string? Email,
    string? FullName,
    string? Roles,
    string? Tenant,
    bool IsActive,
    bool IsSysAdmin,
    Guid? OrgId);