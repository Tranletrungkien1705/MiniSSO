using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Kết quả 1 lần kiểm tra phạm vi dữ liệu (kèm lý do khi bị từ chối).</summary>
public sealed record ScopeCheck(bool Allowed, string? Reason)
{
    public static ScopeCheck Allow() => new(true, null);
    public static ScopeCheck Deny(string reason) => new(false, reason);
}

/// <summary>
/// Phạm vi dữ liệu theo nhánh tổ chức (port từ iNOS.InBrand: SysUserProvider "ViewAbility").
///
/// iNOS gắn mỗi người dùng vào 1 đại lý (SysUser.DLCode) nằm trong cây đại lý (materialized path:
/// DLBUCode/DLBUPattern/DLLevel). Khi truy vấn dữ liệu, hệ thống chỉ cho người dùng thấy các bản ghi
/// thuộc nhánh của mình — trừ 2 trường hợp đặc biệt:
///   • SysAdmin = true  → thấy TẤT CẢ (Mst_Dealer_ViewAbility_GetFromListUser nhánh SysAdmin),
///   • người dùng ở nút gốc (DLCode = DLCodeRoot) → thấy TẤT CẢ.
/// Ngược lại, phạm vi = các đơn vị có DLBUCode khớp tiền tố DLBUPattern của người dùng (chính nó + nhánh con).
///
/// MiniSSO tái tạo đúng logic đó trên cây <see cref="Org"/> (đã có BuCode/BuPattern/Level):
///   • <see cref="VisibleOrgIdsAsync"/>  ↔ Sys_User_GetListAbilityViewOfUser + Mst_Dealer_ViewAbility_GetFromListUser
///   • <see cref="CanAccessOrgAsync"/>   ↔ myCache_ViewAbility_CheckAccessDealer
///   • <see cref="IsExactOrgAsync"/>     ↔ myCache_ViewAbility_CheckExactDealer
///   • <see cref="IsExactUserAsync"/>    ↔ myCache_ViewAbility_CheckExactUser
///   • <see cref="IsRootOrgAsync"/>      ↔ myCache_CheckDealerRoot
///   • <see cref="CheckAccessAsync"/>    ↔ myCache_ViewAbility_CheckAccess (gộp các cờ kiểm tra)
/// </summary>
public sealed class DataScopeService(AppDbContext db)
{
    /// <summary>Mã đơn vị gốc quy ước (tương ứng TConst.BizMix.DLCodeRoot = '0' trong iNOS).</summary>
    public const string RootCode = OrgService.RootCode;

    /// <summary>
    /// Phạm vi dữ liệu của 1 người dùng: tập Id các đơn vị tổ chức họ được phép thấy.
    /// SysAdmin hoặc người dùng ở nút gốc → toàn bộ cây; ngược lại → chính đơn vị + toàn bộ nhánh con.
    /// Trả về <c>null</c> nghĩa là "không giới hạn" (thấy tất cả) — tiện cho việc lọc.
    /// </summary>
    public async Task<HashSet<Guid>?> VisibleOrgIdsAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return [];

        // SysAdmin: bỏ qua mọi giới hạn (nhánh SysAdmin trong Mst_Dealer_ViewAbility_GetFromListUser).
        if (user.IsSysAdmin) return null;

        // Không gắn đơn vị: không thấy dữ liệu nào (an toàn theo mặc định).
        if (user.OrgId == null) return [];

        var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == user.OrgId);
        if (org == null) return [];

        // Người dùng ở nút gốc: thấy tất cả (nhánh DLCode == DLCodeRoot).
        if (org.BuCode == RootCode) return null;

        // Còn lại: chính nó + nhánh con, dựa trên tiền tố BuCode (tương ứng DLBUCode LIKE DLBUPattern).
        var ids = await db.Orgs
            .Where(o => o.BuCode == org.BuCode || o.BuCode.StartsWith(org.BuCode + "."))
            .Select(o => o.Id)
            .ToListAsync();
        return ids.ToHashSet();
    }

    /// <summary>Danh sách đơn vị tổ chức người dùng được phép thấy (rỗng nếu không thấy gì; toàn bộ nếu không giới hạn).</summary>
    public async Task<List<Org>> VisibleOrgsAsync(Guid userId)
    {
        var scope = await VisibleOrgIdsAsync(userId);
        var all = await db.Orgs.OrderBy(o => o.BuCode).ToListAsync();
        return scope == null ? all : all.Where(o => scope.Contains(o.Id)).ToList();
    }

    /// <summary>Người dùng có được thấy 1 đơn vị tổ chức cụ thể hay không (↔ myCache_ViewAbility_CheckAccessDealer).</summary>
    public async Task<bool> CanAccessOrgAsync(Guid userId, Guid orgId)
    {
        var scope = await VisibleOrgIdsAsync(userId);
        return scope == null || scope.Contains(orgId);
    }

    /// <summary>Đơn vị mục tiêu có đúng là đơn vị của người dùng hay không (↔ myCache_ViewAbility_CheckExactDealer).</summary>
    public async Task<bool> IsExactOrgAsync(Guid userId, Guid orgId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return user?.OrgId != null && user.OrgId == orgId;
    }

    /// <summary>Người dùng mục tiêu có đúng là chính người dùng đang xét hay không (↔ myCache_ViewAbility_CheckExactUser).</summary>
    public async Task<bool> IsExactUserAsync(Guid userId, Guid targetUserId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return user != null && user.Id == targetUserId;
    }

    /// <summary>Người dùng có thuộc đơn vị gốc hay không (↔ myCache_CheckDealerRoot).</summary>
    public async Task<bool> IsRootOrgAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.OrgId == null) return false;
        var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == user.OrgId);
        return org != null && org.BuCode == RootCode;
    }

    /// <summary>
    /// Kiểm tra tổng hợp (↔ myCache_ViewAbility_CheckAccess): gộp các cờ kiểm tra phạm vi.
    /// Trả về <see cref="ScopeCheck"/> — bị từ chối kèm lý do cụ thể (giống ServiceException của iNOS).
    /// </summary>
    public async Task<ScopeCheck> CheckAccessAsync(
        Guid userId,
        Guid? targetOrgId = null,
        Guid? targetUserId = null,
        bool checkOrgAccess = false,
        bool checkOrgExact = false,
        bool checkUserExact = false,
        bool checkRoot = false)
    {
        if (checkOrgAccess && targetOrgId != null && !await CanAccessOrgAsync(userId, targetOrgId.Value))
            return ScopeCheck.Deny("Đơn vị mục tiêu nằm ngoài phạm vi dữ liệu của bạn.");

        if (checkOrgExact && targetOrgId != null && !await IsExactOrgAsync(userId, targetOrgId.Value))
            return ScopeCheck.Deny("Chỉ được thao tác trên đơn vị của chính mình.");

        if (checkUserExact && targetUserId != null && !await IsExactUserAsync(userId, targetUserId.Value))
            return ScopeCheck.Deny("Chỉ được thao tác trên chính tài khoản của mình.");

        if (checkRoot && !await IsRootOrgAsync(userId))
            return ScopeCheck.Deny("Chức năng này chỉ dành cho đơn vị gốc.");

        return ScopeCheck.Allow();
    }
}