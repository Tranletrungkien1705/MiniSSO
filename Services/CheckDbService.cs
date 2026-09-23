using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Kiểm tra tồn tại/trạng thái trước khi lưu (port từ iNOS.InBrand: mẫu "CheckDB").
///
/// iNOS dùng CÙNG một mẫu kiểm tra ở mọi manager trước khi ghi dữ liệu:
///   • SysUserManager.SysUserCheckDB,
///   • SysGroupManager.SysGroupCheckDB,
///   • SysModuleManager.MstModuleCheckDB,
///   • MstDealerManager.MstDealerCheckDB, …
/// Mỗi hàm nhận 2 tham số điều khiển:
///   • strFlagExistToCheck  — "1" (Flag.Active/Yes): bản ghi PHẢI tồn tại; "0" (Flag.Inactive/No): bản ghi PHẢI KHÔNG tồn tại;
///   • strFlagActiveListToCheck — danh sách cờ hoạt động cho phép (vd "1" = chỉ nhận bản ghi đang hoạt động);
///     trạng thái thực tế của bản ghi ("1"/"0") phải nằm trong danh sách này.
/// Khi vi phạm, iNOS ném ServiceException kèm mã lỗi (…_CheckDB_UserCodeNotFound / …_CodeExist / …_FlagActiveNotMatched).
///
/// MiniSSO trước đây kiểm tra rải rác, mỗi chỗ một kiểu (AnyAsync + thông báo tự do), KHÔNG có
/// một mẫu kiểm tra dùng chung. Service này tái tạo đúng ngữ nghĩa trên cho 4 loại thực thể
/// đang có: Người dùng, Nhóm, Module, Đơn vị tổ chức.
/// </summary>
public sealed class CheckDbService(AppDbContext db)
{
    // ── Hằng cờ (↔ iNOS.InBrand Constants.Flag) ──
    public const string FlagActive = "1";     // Flag.Active / Flag.Yes
    public const string FlagInactive = "0";   // Flag.Inactive / Flag.No

    /// <summary>Loại thực thể có thể kiểm tra (↔ các *CheckDB của iNOS).</summary>
    public enum EntityKind { User, Group, Module, Org, DealerType }

    /// <summary>Kết quả 1 lần kiểm tra: hợp lệ hay không + lý do + trạng thái thực tế của bản ghi.</summary>
    public sealed record CheckResult(bool Ok, string? Error, bool Exists, string Status)
    {
        public static CheckResult Pass(bool exists, string status) => new(true, null, exists, status);
        public static CheckResult Fail(string error, bool exists, string status) => new(false, error, exists, status);
    }

    /// <summary>
    /// Kiểm tra 1 bản ghi theo mẫu CheckDB của iNOS.
    /// </summary>
    /// <param name="kind">Loại thực thể.</param>
    /// <param name="code">Mã định danh bản ghi (SysUser.Code / SysGroup.Code / SysModule.Code / Mst_Org.OrgID).</param>
    /// <param name="flagExistToCheck">"1" = phải tồn tại; "0" = phải chưa tồn tại; "" = bỏ qua kiểm tra tồn tại.</param>
    /// <param name="flagActiveListToCheck">Danh sách cờ hoạt động cho phép (vd "1"); "" = bỏ qua kiểm tra trạng thái.</param>
    public async Task<CheckResult> CheckAsync(EntityKind kind, string? code, string flagExistToCheck = "", string flagActiveListToCheck = "")
    {
        var c = (code ?? "").Trim();
        var (exists, isActive) = await LookupAsync(kind, c);
        var status = exists ? (isActive ? FlagActive : FlagInactive) : "";

        // strFlagExistToCheck: "1" → phải tồn tại; "0" → phải chưa tồn tại.
        if (flagExistToCheck == FlagActive && !exists)
            return CheckResult.Fail($"{Label(kind)} '{c}' không tồn tại.", exists, status);
        if (flagExistToCheck == FlagInactive && exists)
            return CheckResult.Fail($"{Label(kind)} '{c}' đã tồn tại.", exists, status);

        // strFlagActiveListToCheck: trạng thái thực tế phải nằm trong danh sách cho phép.
        if (!string.IsNullOrEmpty(flagActiveListToCheck) && !flagActiveListToCheck.Contains(status))
            return CheckResult.Fail(
                $"{Label(kind)} '{c}' có trạng thái '{status}' không thuộc danh sách cho phép '{flagActiveListToCheck}'.",
                exists, status);

        return CheckResult.Pass(exists, status);
    }

    /// <summary>Tra cứu tồn tại + trạng thái hoạt động của 1 bản ghi theo loại.</summary>
    private async Task<(bool Exists, bool IsActive)> LookupAsync(EntityKind kind, string code)
    {
        if (string.IsNullOrEmpty(code)) return (false, false);
        return kind switch
        {
            EntityKind.User => await db.Users.Where(u => u.Email == code).Select(u => new { u.IsActive })
                .FirstOrDefaultAsync() is { } u ? (true, u.IsActive) : (false, false),
            EntityKind.Group => await db.Groups.Where(g => g.Code == code).Select(g => new { g.IsActive })
                .FirstOrDefaultAsync() is { } g ? (true, g.IsActive) : (false, false),
            EntityKind.Module => await db.Modules.Where(m => m.Code == code).Select(m => new { m.IsActive })
                .FirstOrDefaultAsync() is { } m ? (true, m.IsActive) : (false, false),
            EntityKind.Org => await db.Orgs.Where(o => o.Code == code).Select(o => new { o.IsActive })
                .FirstOrDefaultAsync() is { } o ? (true, o.IsActive) : (false, false),
            EntityKind.DealerType => await db.DealerTypes.Where(t => t.Code == code).Select(t => new { t.IsActive })
                .FirstOrDefaultAsync() is { } t ? (true, t.IsActive) : (false, false),
            _ => (false, false)
        };
    }

    private static string Label(EntityKind kind) => kind switch
    {
        EntityKind.User => "Người dùng",
        EntityKind.Group => "Nhóm",
        EntityKind.Module => "Module",
        EntityKind.Org => "Đơn vị tổ chức",
        EntityKind.DealerType => "Loại đại lý",
        _ => "Bản ghi"
    };
}
