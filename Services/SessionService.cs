using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Ảnh chụp "phiên làm việc hiệu lực" của 1 người dùng (tương ứng iNOS.InBrand SysSession).
/// Gộp: định danh người dùng + cờ SysAdmin + bối cảnh đơn vị/kho + danh sách module (kèm chức năng)
/// mà người dùng được chạm tới. Có sẵn <see cref="HasModule"/> / <see cref="HasFunction"/> để màn hình
/// kiểm tra quyền nhanh mà không phải truy vấn lại.
/// </summary>
public sealed record SessionSnapshot(
    Guid UserId,
    string Email,
    string FullName,
    bool IsSysAdmin,
    Guid? OrgId,
    string? OrgName,
    string? InvCode,
    IReadOnlyList<string> ModuleCodes,
    IReadOnlyList<string> FunctionCodes)
{
    /// <summary>Người dùng có module <paramref name="code"/> trong phiên hay không (↔ SysSession.HasModule).</summary>
    public bool HasModule(string code) =>
        string.IsNullOrEmpty(code) ? ModuleCodes.Count == 0
            : ModuleCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Người dùng có chức năng <paramref name="code"/> trong phiên hay không (↔ SysSession.HasFunction).</summary>
    public bool HasFunction(string code) =>
        string.IsNullOrEmpty(code) ? FunctionCodes.Count == 0
            : FunctionCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Phiên làm việc hiệu lực (port từ iNOS.InBrand: GlobSession / SysSession / GlobSessionManager.GetFullPermission).
///
/// iNOS tạo 1 GlobSession khi đăng nhập rồi dựng SysSession = ảnh chụp quyền hiệu lực: IsSysAdmin +
/// SysUser + danh sách Module (kèm Function) + bối cảnh đơn vị (DLName) / kho (InvCode). SysSession có
/// HasModule/HasFunction để màn hình kiểm tra nhanh. MiniSSO trước đây chỉ có các mảnh rời (RbacService,
/// ModuleService, DataScopeService) — service này gộp lại thành 1 "phiên" và lưu vết phiên (UserSession).
/// </summary>
public sealed class SessionService(AppDbContext db, ModuleService modules, DataScopeService scope)
{
    /// <summary>
    /// Dựng ảnh chụp phiên hiệu lực của 1 người dùng (↔ GlobSessionManager.GetFullPermission):
    /// định danh + IsSysAdmin + bối cảnh đơn vị + module/chức năng hiệu lực (từ menu theo nhóm).
    /// Trả về <c>null</c> nếu không tìm thấy người dùng.
    /// </summary>
    public async Task<SessionSnapshot?> BuildAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;

        // Bối cảnh đơn vị (SysSession.DLName ↔ SysUser.DLCode).
        string? orgName = null;
        if (user.OrgId != null)
            orgName = await db.Orgs.Where(o => o.Id == user.OrgId).Select(o => o.Name).FirstOrDefaultAsync();

        // Module + chức năng hiệu lực (↔ SysModuleManager.GetAllByUser qua menu theo nhóm).
        var menu = await modules.MenuForUserAsync(userId);
        var moduleCodes = menu.Select(m => m.Module.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var functionCodes = menu.SelectMany(m => m.Functions).Select(f => f.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new SessionSnapshot(
            user.Id, user.Email, user.FullName, user.IsSysAdmin, user.OrgId, orgName, user.Tenant,
            moduleCodes, functionCodes);
    }

    /// <summary>
    /// Tạo + lưu 1 phiên làm việc (↔ GlobSessionManager.Add): sinh mã phiên, chụp quyền hiệu lực tại
    /// thời điểm tạo. Trả về <c>null</c> nếu không tìm thấy người dùng.
    /// </summary>
    public async Task<UserSession?> CreateAsync(Guid userId, TimeSpan? ttl = null)
    {
        var snapshot = await BuildAsync(userId);
        if (snapshot == null) return null;

        var session = new UserSession
        {
            SessionId = TokenService.NewOpaque(),
            UserId = userId,
            IsSysAdmin = snapshot.IsSysAdmin,
            OrgId = snapshot.OrgId,
            OrgName = snapshot.OrgName,
            InvCode = snapshot.InvCode,
            ExpiresAt = ttl == null ? null : DateTime.UtcNow.Add(ttl.Value)
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    /// <summary>Phiên còn hiệu lực (đang hoạt động và chưa hết hạn) theo mã phiên.</summary>
    public async Task<UserSession?> GetActiveAsync(string sessionId)
    {
        var s = await db.UserSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId);
        if (s == null || !s.IsActive) return null;
        if (s.ExpiresAt != null && s.ExpiresAt < DateTime.UtcNow) return null;
        return s;
    }

    /// <summary>Kết thúc 1 phiên (↔ đăng xuất): đánh dấu không còn hiệu lực.</summary>
    public async Task<bool> EndAsync(string sessionId)
    {
        var s = await db.UserSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId);
        if (s == null) return false;
        s.IsActive = false;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Kết thúc mọi phiên đang hoạt động của 1 người dùng (↔ SysUserLogOutX).</summary>
    public async Task<int> EndAllForUserAsync(Guid userId)
    {
        var active = await db.UserSessions.Where(x => x.UserId == userId && x.IsActive).ToListAsync();
        foreach (var s in active) s.IsActive = false;
        await db.SaveChangesAsync();
        return active.Count;
    }

    /// <summary>Danh sách phiên gần đây (mới nhất trước) — phục vụ màn hình quản trị.</summary>
    public async Task<List<UserSession>> RecentAsync(int take = 100)
        => await db.UserSessions.OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();

    /// <summary>
    /// Kiểm tra phiên có quyền với 1 module/chức năng (↔ SysSession.HasModule/HasFunction).
    /// Trả về <see cref="ScopeCheck"/> — bị từ chối kèm lý do cụ thể.
    /// </summary>
    public async Task<ScopeCheck> CheckAsync(Guid userId, string? moduleCode = null, string? functionCode = null)
    {
        var snapshot = await BuildAsync(userId);
        if (snapshot == null) return ScopeCheck.Deny("Không tìm thấy người dùng.");

        if (!string.IsNullOrWhiteSpace(moduleCode) && !snapshot.HasModule(moduleCode))
            return ScopeCheck.Deny($"Không có module '{moduleCode}' trong phiên.");

        if (!string.IsNullOrWhiteSpace(functionCode) && !snapshot.HasFunction(functionCode))
            return ScopeCheck.Deny($"Không có chức năng '{functionCode}' trong phiên.");

        return ScopeCheck.Allow();
    }
}
