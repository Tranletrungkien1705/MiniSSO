using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Kết quả 1 thao tác xoá người dùng (kèm số liên kết đã dọn khi thành công).</summary>
public sealed record UserDeleteResult(bool Ok, string? Error, int RemovedGroupLinks)
{
    public static UserDeleteResult Success(int removedGroupLinks) => new(true, null, removedGroupLinks);
    public static UserDeleteResult Fail(string error) => new(false, error, 0);
}

/// <summary>
/// Xoá người dùng kèm dọn liên kết (port từ iNOS.InBrand:
/// SysUserManager.Remove → SysUserDeleteX + SysUserInGroupProvider.RemoveByUser).
///
/// iNOS KHÔNG xoá thẳng dòng Sys_User: <c>Remove</c> mở transaction, gọi <c>SysUserDeleteX</c> để
/// kiểm tra người dùng PHẢI tồn tại (SysUserCheckDB với strFlagExistToCheck = "1") rồi xoá bản ghi;
/// bản <c>Remove</c> cũ (đang bị comment trong nguồn) còn gỡ người dùng khỏi MỌI nhóm trước khi xoá
/// (uigMng.RemoveByUser → DELETE FROM SysUserInGroup WHERE UserCode = @userCode).
///
/// MiniSSO trước đây chỉ có <see cref="GroupService.RemoveUserFromAllGroupsAsync"/> (gỡ khỏi nhóm)
/// nhưng KHÔNG có luồng xoá người dùng. Service này tái tạo đúng ngữ nghĩa: kiểm tra tồn tại →
/// gỡ khỏi mọi nhóm → xoá người dùng, tất cả trong 1 thao tác.
/// </summary>
public sealed class UserDeleteService(AppDbContext db)
{
    /// <summary>
    /// Xoá 1 người dùng (↔ SysUserManager.Remove → SysUserDeleteX + RemoveByUser).
    /// Ràng buộc: người dùng PHẢI tồn tại (↔ SysUserCheckDB với strFlagExistToCheck = "1").
    /// Trước khi xoá, gỡ người dùng khỏi MỌI nhóm (↔ SysUserInGroupProvider.RemoveByUser).
    /// Trả về <see cref="UserDeleteResult"/> kèm số liên kết nhóm đã dọn.
    /// </summary>
    public async Task<UserDeleteResult> DeleteAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return UserDeleteResult.Fail("Không tìm thấy người dùng.");

        // Gỡ khỏi mọi nhóm trước khi xoá (↔ SysUserInGroupProvider.RemoveByUser).
        var links = await db.GroupMembers.Where(m => m.UserId == userId).ToListAsync();
        if (links.Count > 0) db.GroupMembers.RemoveRange(links);

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return UserDeleteResult.Success(links.Count);
    }
}
