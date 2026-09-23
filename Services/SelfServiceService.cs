using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Tự phục vụ tài khoản (port từ iNOS.InBrand: AccountController.ChangePassword + SysUserManager.ResetPassword).
///
/// iNOS cho phép người dùng ĐANG ĐĂNG NHẬP tự đổi mật khẩu của chính mình (màn hình Account/ChangePassword):
///   • mật khẩu mới bắt buộc không rỗng (CUtils.IsNullOrEmpty(passnew)),
///   • mật khẩu nhập lại bắt buộc không rỗng và PHẢI khớp mật khẩu mới (passnew.Equals(confpass)),
///   • sau đó gọi SysUserService.ResetPass(session.SysUser.Code, passnew) — chỉ ghi mật khẩu mới cho CHÍNH user đó.
///
/// MiniSSO trước đây chỉ có đặt lại mật khẩu do ADMIN thực hiện (AccountSecurityService.ResetPasswordAsync);
/// KHÔNG có luồng người dùng tự đổi mật khẩu (phải xác nhận mật khẩu mới + kiểm tra mật khẩu hiện tại).
/// Service này bổ sung đúng luồng đó, tách biệt khỏi quyền quản trị.
/// </summary>
public sealed class SelfServiceService(AppDbContext db)
{
    /// <summary>Độ dài tối thiểu của mật khẩu mới (nhất quán với luồng admin).</summary>
    public const int MinPasswordLength = 6;

    /// <summary>
    /// Người dùng tự đổi mật khẩu (↔ AccountController.ChangePassword + SysUserManager.ResetPassword).
    /// Ràng buộc theo đúng iNOS:
    ///   • người dùng phải tồn tại và đang hoạt động,
    ///   • mật khẩu hiện tại phải khớp (xác thực lại trước khi đổi),
    ///   • mật khẩu mới không rỗng và đủ độ dài,
    ///   • mật khẩu nhập lại phải khớp mật khẩu mới.
    /// Khi thành công: ghi mật khẩu mới (băm) và xoá bộ đếm đăng nhập sai.
    /// </summary>
    public async Task<GroupResult> ChangeOwnPasswordAsync(Guid userId, string? currentPassword, string? newPassword, string? confirmPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return GroupResult.Fail("Không tìm thấy người dùng.");
        if (!user.IsActive) return GroupResult.Fail("Tài khoản đã bị vô hiệu hoá.");

        // Xác thực lại mật khẩu hiện tại (iNOS: session đã đăng nhập, nhưng vẫn yêu cầu mật khẩu cũ hợp lệ).
        if (!PasswordHasher.Verify(currentPassword ?? "", user.PasswordHash))
            return GroupResult.Fail("Mật khẩu hiện tại không đúng.");

        // Mật khẩu mới không rỗng (↔ CUtils.IsNullOrEmpty(passnew)).
        if (string.IsNullOrWhiteSpace(newPassword))
            return GroupResult.Fail("Mật khẩu mới trống.");

        // Mật khẩu nhập lại không rỗng và phải khớp (↔ passnew.Equals(confpass)).
        if (string.IsNullOrWhiteSpace(confirmPassword))
            return GroupResult.Fail("Nhập lại mật khẩu mới trống.");
        if (!string.Equals(newPassword.Trim(), confirmPassword.Trim(), StringComparison.Ordinal))
            return GroupResult.Fail("Nhập lại mật khẩu mới không đúng, vui lòng nhập lại.");

        if (newPassword.Trim().Length < MinPasswordLength)
            return GroupResult.Fail($"Mật khẩu mới phải từ {MinPasswordLength} ký tự.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.FailedLoginCount = 0;   // đổi mật khẩu thành công → xoá bộ đếm sai
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }
}
