using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Đội người dùng (port từ iNOS.InBrand: Sys_UserTeam + SysUserTeamManager/SysUserTeamProvider).
///
/// iNOS có bảng Sys_UserTeam (TeamCode PK, DLCode, TeamName, FlagActive): mỗi "đội" thuộc một
/// đại lý/đơn vị (DLCode). Đây là khái niệm TỔ CHỨC (đội trong đơn vị), KHÁC với Group (nhóm quyền):
/// Group gom người dùng để cấp quyền, còn Team gom người dùng theo đơn vị nghiệp vụ.
///
/// Service tái tạo đúng ngữ nghĩa kiểm tra của iNOS khi thêm/sửa đội:
///   • mã đội bắt buộc và duy nhất (SysUserTeam.TeamCode [PrimaryKey][RequireField]),
///   • đơn vị của đội (nếu có) phải tồn tại và đang hoạt động (↔ MstDealerCheckDB),
///   • cờ hoạt động (FlagActive) bật/tắt được.
/// MiniSSO trước đây không có khái niệm "đội".
/// </summary>
public sealed class UserTeamService(AppDbContext db)
{
    /// <summary>Toàn bộ đội (kèm tên đơn vị), sắp theo mã.</summary>
    public async Task<List<UserTeam>> AllAsync()
        => await db.UserTeams.OrderBy(t => t.Code).ToListAsync();

    /// <summary>Danh sách đội thuộc 1 đơn vị tổ chức (↔ lọc theo Sys_UserTeam.DLCode).</summary>
    public async Task<List<UserTeam>> ByOrgAsync(Guid orgId)
        => await db.UserTeams.Where(t => t.OrgId == orgId).OrderBy(t => t.Code).ToListAsync();

    /// <summary>
    /// Tạo 1 đội mới (↔ SysUserTeamManager.Add). Ràng buộc: mã đội bắt buộc &amp; chưa tồn tại;
    /// đơn vị (nếu có) phải tồn tại và đang hoạt động.
    /// </summary>
    public async Task<GroupResult> CreateAsync(string code, string? name, Guid? orgId)
    {
        if (string.IsNullOrWhiteSpace(code)) return GroupResult.Fail("Cần mã đội.");
        var c = code.Trim();
        if (await db.UserTeams.AnyAsync(t => t.Code == c)) return GroupResult.Fail("Mã đội đã tồn tại.");

        if (orgId != null)
        {
            var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == orgId);
            if (org == null) return GroupResult.Fail("Đơn vị của đội không tồn tại.");
            if (!org.IsActive) return GroupResult.Fail("Đơn vị của đội đang bị khoá.");
        }

        db.UserTeams.Add(new UserTeam { Code = c, Name = string.IsNullOrWhiteSpace(name) ? c : name!.Trim(), OrgId = orgId });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>
    /// Cập nhật tên/đơn vị của 1 đội (↔ SysUserTeamManager.Update). Ràng buộc đơn vị giống khi tạo.
    /// </summary>
    public async Task<GroupResult> UpdateAsync(Guid id, string? name, Guid? orgId)
    {
        var team = await db.UserTeams.FirstOrDefaultAsync(t => t.Id == id);
        if (team == null) return GroupResult.Fail("Không tìm thấy đội.");

        if (orgId != null)
        {
            var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == orgId);
            if (org == null) return GroupResult.Fail("Đơn vị của đội không tồn tại.");
            if (!org.IsActive) return GroupResult.Fail("Đơn vị của đội đang bị khoá.");
        }

        team.Name = string.IsNullOrWhiteSpace(name) ? team.Code : name!.Trim();
        team.OrgId = orgId;
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>Bật/tắt cờ hoạt động của đội (↔ Sys_UserTeam.FlagActive).</summary>
    public async Task<bool> ToggleAsync(Guid id)
    {
        var team = await db.UserTeams.FirstOrDefaultAsync(t => t.Id == id);
        if (team == null) return false;
        team.IsActive = !team.IsActive;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Xoá 1 đội (↔ SysUserTeamManager.Remove).</summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        var team = await db.UserTeams.FirstOrDefaultAsync(t => t.Id == id);
        if (team == null) return false;
        db.UserTeams.Remove(team);
        await db.SaveChangesAsync();
        return true;
    }
}
