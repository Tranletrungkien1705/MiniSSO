using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Tự đăng ký tham gia hệ thống (port từ iNOS.InBrand: AccountController.Join + AccountService.Register
/// + SysUserManager.Register → SysUserAddX_New20190917).
///
/// iNOS cho phép một người dùng (đã xác thực qua OAuth bên ngoài) tự hoàn tất hồ sơ để trở thành
/// người dùng hệ thống (màn hình Account/Join): điền Code, FullName, Email rồi gọi
/// AccountService.Register → SysUserManager.Register → Add. Khi thêm, SysUserAddX_New20190917 áp
/// các quy tắc:
///   • Code bắt buộc (SysUserCheckDB với strFlagExistToCheck = "0" → PHẢI CHƯA tồn tại),
///   • DLCode tuỳ chọn: nếu có thì phải tồn tại & đang hoạt động (MstDealerCheckDB),
///   • Email/FullName/PhoneNo/VerificationCode được Trim,
///   • Lockout = false, Enable = true, Language = "vi", LockoutDate = now,
///   • mật khẩu được băm trước khi lưu.
///
/// MiniSSO trước đây CHỈ có luồng ADMIN tạo người dùng (ApiV1Controller.CreateUser / UserController.Create);
/// KHÔNG có luồng người dùng TỰ đăng ký (tự đặt mật khẩu, tự khai hồ sơ, tài khoản tạo ra ở trạng thái
/// hoạt động ngay). Service này bổ sung đúng luồng đó, tách biệt khỏi quyền quản trị.
/// </summary>
public sealed class RegistrationService(AppDbContext db, CheckDbService checkDb)
{
    /// <summary>Độ dài tối thiểu của mật khẩu khi tự đăng ký (nhất quán với luồng admin).</summary>
    public const int MinPasswordLength = 6;

    /// <summary>Ngôn ngữ mặc định khi đăng ký (↔ SysUserAddX_New20190917: objSysUser.Language = "vi").</summary>
    public const string DefaultLanguage = "vi";

    /// <summary>
    /// Người dùng tự đăng ký (↔ AccountController.Join + SysUserManager.Register).
    /// Ràng buộc theo đúng iNOS:
    ///   • email (Code) bắt buộc và PHẢI CHƯA tồn tại (↔ SysUserCheckDB FlagInactive),
    ///   • mật khẩu bắt buộc và đủ độ dài,
    ///   • đơn vị (nếu có) phải tồn tại &amp; đang hoạt động (↔ MstDealerCheckDB),
    ///   • tài khoản tạo ra ở trạng thái hoạt động, không bị khoá, ngôn ngữ mặc định "vi".
    /// Trả về Id người dùng mới khi thành công.
    /// </summary>
    public async Task<RegistrationResult> RegisterAsync(string? email, string? password, string? fullName,
        string? phoneNo = null, Guid? orgId = null)
    {
        var mail = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mail))
            return RegistrationResult.Fail("Cần email đăng ký.");

        // Mật khẩu bắt buộc và đủ độ dài (iNOS băm mật khẩu trước khi lưu).
        if (string.IsNullOrWhiteSpace(password))
            return RegistrationResult.Fail("Cần mật khẩu.");
        if (password.Trim().Length < MinPasswordLength)
            return RegistrationResult.Fail($"Mật khẩu phải từ {MinPasswordLength} ký tự.");

        // Kiểm tra theo mẫu CheckDB (↔ SysUserCheckDB): người dùng PHẢI CHƯA tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.User, mail, CheckDbService.FlagInactive);
        if (!chk.Ok) return RegistrationResult.Fail(chk.Error!);

        // Đơn vị (nếu có) phải tồn tại & đang hoạt động (↔ MstDealerCheckDB).
        if (orgId != null)
        {
            var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == orgId);
            if (org == null) return RegistrationResult.Fail("Đơn vị tổ chức không tồn tại.");
            if (!org.IsActive) return RegistrationResult.Fail("Đơn vị tổ chức đã bị khoá.");
        }

        var user = new AppUser
        {
            Email = mail,
            FullName = (fullName ?? "").Trim(),
            PhoneNo = string.IsNullOrWhiteSpace(phoneNo) ? null : phoneNo.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            OrgId = orgId,
            // ↔ SysUserAddX_New20190917: Lockout = false, Enable = true, Language = "vi".
            IsActive = true,
            IsLockedOut = false,
            LockoutDate = DateTime.UtcNow,
            Language = DefaultLanguage
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return RegistrationResult.Success(user.Id);
    }
}

/// <summary>Kết quả 1 lần tự đăng ký (kèm lý do khi bị từ chối + Id người dùng mới).</summary>
public sealed record RegistrationResult(bool Ok, string? Error, Guid? UserId = null)
{
    public static RegistrationResult Success(Guid userId) => new(true, null, userId);
    public static RegistrationResult Fail(string error) => new(false, error);
}
