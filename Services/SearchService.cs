using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Một trang kết quả tìm kiếm (tương ứng PageInfo của iNOS: dữ liệu + tổng số + số trang).</summary>
public sealed record PagedResult<T>(List<T> Items, int Total, int PageIndex, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Người dùng kèm danh sách nhóm (tương ứng SysUser.Groups được gắn trong SysUserManager.Search).</summary>
public sealed record UserSearchRow(AppUser User, List<Group> Groups);

/// <summary>
/// Tìm kiếm có phân trang + lọc theo phạm vi dữ liệu (port từ iNOS.InBrand:
/// SysUserManager.Search + SysGroupManager.Search).
///
/// iNOS KHÔNG trả về toàn bộ bảng khi màn hình quản trị tìm kiếm: mỗi lần gọi Search truyền
/// (searchDic, pageIndex, pageSize, orderByColums) và nhận về 1 TRANG kết quả (PageInfo).
/// Trước khi truy vấn, iNOS chèn thêm điều kiện phạm vi dữ liệu:
///   • nếu người gọi là SysAdmin HOẶC ở nút gốc (DLCode = DLCodeRoot) → KHÔNG giới hạn,
///   • ngược lại → chỉ thấy bản ghi có DLCode nằm trong danh sách đại lý mà người gọi được thấy
///     (searchDic.Add("SysUser.DLCode in", lstMstDealer)).
/// Sau khi lấy trang, SysUserManager.Search còn GẮN NHÓM cho từng người dùng
/// (item.Groups = các nhóm mà người dùng thuộc về).
///
/// MiniSSO tái tạo đúng luồng đó trên cây <see cref="Org"/> (đã có BuCode/BuPattern/Level) và
/// tái dùng <see cref="DataScopeService"/> để tính phạm vi. MiniSSO trước đây chỉ có danh sách
/// phẳng (db.Users.ToListAsync) — KHÔNG có phân trang, KHÔNG lọc phạm vi, KHÔNG gắn nhóm.
/// </summary>
public sealed class SearchService(AppDbContext db, DataScopeService scope)
{
    /// <summary>Kích thước trang mặc định (tương ứng pageSize mặc định của iNOS).</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Tìm kiếm người dùng có phân trang (↔ SysUserManager.Search).
    /// Lọc theo phạm vi dữ liệu của <paramref name="callerId"/> (SysAdmin/nút gốc → tất cả),
    /// theo từ khoá (khớp email/họ tên, không phân biệt hoa thường) và trạng thái hoạt động,
    /// rồi gắn danh sách nhóm cho từng người dùng trong trang.
    /// </summary>
    public async Task<PagedResult<UserSearchRow>> SearchUsersAsync(
        Guid callerId, string? keyword = null, bool? isActive = null,
        int pageIndex = 0, int pageSize = DefaultPageSize, string orderBy = "email")
    {
        var (page, size) = Normalize(pageIndex, pageSize);

        var query = db.Users.AsQueryable();

        // Lọc phạm vi dữ liệu (↔ searchDic.Add("SysUser.DLCode in", lstMstDealer)).
        var visible = await scope.VisibleOrgIdsAsync(callerId);
        if (visible != null)
            query = query.Where(u => u.OrgId != null && visible.Contains(u.OrgId.Value));

        // Từ khoá: khớp email hoặc họ tên (không phân biệt hoa thường).
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.ToLower().Contains(k) || u.FullName.ToLower().Contains(k));
        }

        if (isActive != null)
            query = query.Where(u => u.IsActive == isActive.Value);

        var total = await query.CountAsync();
        var users = await OrderUsers(query, orderBy).Skip(page * size).Take(size).ToListAsync();

        // Gắn nhóm cho từng người dùng trong trang (↔ item.Groups = ...).
        var rows = new List<UserSearchRow>(users.Count);
        foreach (var u in users)
            rows.Add(new UserSearchRow(u, await GroupsOfUserAsync(u.Id)));

        return new PagedResult<UserSearchRow>(rows, total, page, size);
    }

    /// <summary>
    /// Tìm kiếm nhóm có phân trang (↔ SysGroupManager.Search).
    /// Lọc theo phạm vi dữ liệu của <paramref name="callerId"/> (nhóm toàn cục OrgId = null luôn hiển thị),
    /// theo từ khoá (khớp mã/tên) và trạng thái hoạt động.
    /// </summary>
    public async Task<PagedResult<Group>> SearchGroupsAsync(
        Guid callerId, string? keyword = null, bool? isActive = null,
        int pageIndex = 0, int pageSize = DefaultPageSize, string orderBy = "code")
    {
        var (page, size) = Normalize(pageIndex, pageSize);

        var query = db.Groups.AsQueryable();

        // Lọc phạm vi dữ liệu (↔ searchDic.Add("SysGroup.DLCode in", lstMstDealer)).
        // Nhóm toàn cục (OrgId = null) không thuộc đại lý nào nên luôn được thấy.
        var visible = await scope.VisibleOrgIdsAsync(callerId);
        if (visible != null)
            query = query.Where(g => g.OrgId == null || visible.Contains(g.OrgId.Value));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLowerInvariant();
            query = query.Where(g => g.Code.ToLower().Contains(k) || g.Name.ToLower().Contains(k));
        }

        if (isActive != null)
            query = query.Where(g => g.IsActive == isActive.Value);

        var total = await query.CountAsync();
        var groups = await OrderGroups(query, orderBy).Skip(page * size).Take(size).ToListAsync();
        return new PagedResult<Group>(groups, total, page, size);
    }

    /// <summary>Danh sách nhóm của 1 người dùng (↔ SysGroupProvider.GetAllByUser), sắp theo mã.</summary>
    private async Task<List<Group>> GroupsOfUserAsync(Guid userId)
    {
        var groupIds = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        return await db.Groups.Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Code).ToListAsync();
    }

    private static (int page, int size) Normalize(int pageIndex, int pageSize)
    {
        var page = Math.Max(0, pageIndex);
        var size = pageSize <= 0 ? DefaultPageSize : Math.Clamp(pageSize, 1, 200);
        return (page, size);
    }

    private static IQueryable<AppUser> OrderUsers(IQueryable<AppUser> q, string orderBy) => orderBy?.ToLowerInvariant() switch
    {
        "fullname" => q.OrderBy(u => u.FullName).ThenBy(u => u.Email),
        "created" => q.OrderByDescending(u => u.CreatedAt),
        _ => q.OrderBy(u => u.Email),
    };

    private static IQueryable<Group> OrderGroups(IQueryable<Group> q, string orderBy) => orderBy?.ToLowerInvariant() switch
    {
        "name" => q.OrderBy(g => g.Name).ThenBy(g => g.Code),
        "created" => q.OrderByDescending(g => g.CreatedAt),
        _ => q.OrderBy(g => g.Code),
    };
}
