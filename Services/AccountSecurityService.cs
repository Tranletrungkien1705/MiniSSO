using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Kết quả 1 lần xác thực đăng nhập (kèm lý do khi thất bại).</summary>
public sealed record LoginOutcome(AppUser? User, bool Ok, string? Reason)
{
    public static LoginOutcome Success(AppUser u) => new(u, true, null);
    public static LoginOutcome Fail(string reason) => new(null, false, reason);
}

/// <summary>
/// Bảo mật tài khoản (port từ iNOS.InBrand SysUser: Enable / Lockout / LockoutDate / VerificationCode).
/// iNOS tách "Enable" (bị vô hiệu hoá) khỏi "Lockout" (bị khoá do đăng nhập sai nhiều lần) và ghi
/// LockoutDate khi khoá. MiniSSO trước đây chỉ có IsActive; service này bổ sung:
///   • đếm số lần đăng nhập sai liên tiếp (FailedLoginCount),
///   • tự khoá tạm thời sau <see cref="MaxFailedAttempts"/> lần sai (IsLockedOut + LockoutDate + LockoutUntil),
///   • mở khoá khi hết hạn hoặc khi admin mở thủ công,
///   • ghi nhật ký mỗi lần thử đăng nhập (LoginAttempt).
/// </summary>
public sealed class AccountSecurityService(AppDbContext db)
{
    /// <summary>Số lần đăng nhập sai liên tiếp tối đa trước khi khoá tài khoản.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Thời gian khoá tạm thời (phút) sau khi vượt ngưỡng sai.</summary>
    public const int LockoutMinutes = 15;

    /// <summary>
    /// Xác thực đăng nhập theo chính sách bảo mật: kiểm tra vô hiệu hoá → khoá → mật khẩu,
    /// cập nhật bộ đếm sai/khoá và ghi nhật ký. Trả về <see cref="LoginOutcome"/>.
    /// </summary>
    public async Task<LoginOutcome> AuthenticateAsync(string email, string password, string? remoteIp = null)
    {
        var norm = (email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == norm);

        // Không tìm thấy người dùng: vẫn ghi log (không tiết lộ tài khoản có tồn tại hay không).
        if (user == null)
        {
            await LogAsync(norm, null, false, "user_not_found", remoteIp);
            return LoginOutcome.Fail("Email hoặc mật khẩu không đúng.");
        }

        // Tài khoản bị vô hiệu hoá (SysUser.Enable = false).
        if (!user.IsActive)
        {
            await LogAsync(norm, user.Id, false, "disabled", remoteIp);
            return LoginOutcome.Fail("Tài khoản đã bị vô hiệu hoá.");
        }

        // Đang bị khoá: tự mở nếu đã hết hạn khoá tạm thời, ngược lại từ chối.
        if (user.IsLockedOut)
        {
            if (user.LockoutUntil != null && user.LockoutUntil <= DateTime.UtcNow)
            {
                Unlock(user);   // hết hạn khoá tạm thời → mở
            }
            else
            {
                await LogAsync(norm, user.Id, false, "locked_out", remoteIp);
                return LoginOutcome.Fail("Tài khoản đang bị khoá. Vui lòng thử lại sau hoặc liên hệ quản trị.");
            }
        }

        // Sai mật khẩu: tăng bộ đếm, khoá nếu vượt ngưỡng.
        if (!PasswordHasher.Verify(password ?? "", user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.IsLockedOut = true;
                user.LockoutDate = DateTime.UtcNow;
                user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            }
            await LogAsync(norm, user.Id, false, "bad_password", remoteIp);
            await db.SaveChangesAsync();
            return LoginOutcome.Fail(user.IsLockedOut
                ? $"Sai mật khẩu quá {MaxFailedAttempts} lần. Tài khoản bị khoá {LockoutMinutes} phút."
                : "Email hoặc mật khẩu không đúng.");
        }

        // Thành công: xoá bộ đếm sai, ghi mốc đăng nhập.
        user.FailedLoginCount = 0;
        user.LastLoginAt = DateTime.UtcNow;
        await LogAsync(norm, user.Id, true, null, remoteIp);
        await db.SaveChangesAsync();
        return LoginOutcome.Success(user);
    }

    /// <summary>Mở khoá tài khoản thủ công (admin) — xoá cờ khoá + bộ đếm sai.</summary>
    public async Task<bool> UnlockAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return false;
        Unlock(user);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Khoá tài khoản thủ công (admin) — khoá vĩnh viễn tới khi mở lại.</summary>
    public async Task<bool> LockAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return false;
        user.IsLockedOut = true;
        user.LockoutDate = DateTime.UtcNow;
        user.LockoutUntil = null;   // khoá vĩnh viễn tới khi admin mở
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Đặt lại mật khẩu (admin) — băm mật khẩu mới, xoá bộ đếm sai và mở khoá.</summary>
    public async Task<bool> ResetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        Unlock(user);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Nhật ký đăng nhập gần đây (mới nhất trước).</summary>
    public async Task<List<LoginAttempt>> RecentAttemptsAsync(int take = 100) =>
        await db.LoginAttempts.OrderByDescending(a => a.AttemptedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();

    private static void Unlock(AppUser user)
    {
        user.IsLockedOut = false;
        user.LockoutUntil = null;
        user.FailedLoginCount = 0;
    }

    private async Task LogAsync(string email, Guid? userId, bool success, string? reason, string? remoteIp)
    {
        db.LoginAttempts.Add(new LoginAttempt
        {
            Email = email, UserId = userId, Success = success, Reason = reason, RemoteIp = remoteIp
        });
        await db.SaveChangesAsync();
    }
}