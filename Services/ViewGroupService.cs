using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Một nhóm cột hiển thị kèm danh sách cột thành viên (dùng để hiển thị/kiểm tra).</summary>
public sealed record ViewGroupWithColumns(ViewGroupView Group, List<ViewColumnView> Columns);

/// <summary>
/// Nhóm cột hiển thị (port từ iNOS.InBrand: View_GroupView / View_ColumnInGroup / View_ColumnView
/// + ViewColumnInGroupManager.ViewColumnInGroupSaveX).
///
/// iNOS cho phép cấu hình "cột hiển thị" (View_ColumnView) và gom chúng thành "nhóm cột hiển thị"
/// (View_GroupView) qua bảng nối View_ColumnInGroup. Khi lưu 1 nhóm, iNOS KHÔNG thêm/bớt từng dòng
/// mà dùng cơ chế "xoá sạch rồi ghi lại" (clear-all → insert-all): xoá hết cột của nhóm rồi ghi lại
/// đúng tập mới. Trước khi ghi, iNOS kiểm tra:
///   • nhóm phải tồn tại và đang hoạt động (ViewGroupViewCheckDB),
///   • mỗi cột phải tồn tại và đang hoạt động (ViewColumnViewCheckDB),
///   • danh sách cột không được rỗng (ViewColumnInGroup_Save_ColumnInGroupViewTblInvalid).
/// MiniSSO trước đây không có khái niệm cấu hình cột hiển thị theo nhóm — service này tái tạo đúng ngữ nghĩa.
/// </summary>
public sealed class ViewGroupService(AppDbContext db)
{
    /// <summary>Toàn bộ nhóm cột hiển thị (kèm cột thành viên), sắp theo mã.</summary>
    public async Task<List<ViewGroupWithColumns>> AllWithColumnsAsync()
    {
        var groups = await db.ViewGroupViews.OrderBy(g => g.Code).ToListAsync();
        var links = await db.ViewColumnInGroups.ToListAsync();
        var columns = await db.ViewColumnViews.ToListAsync();
        var colById = columns.ToDictionary(c => c.Id);

        return groups.Select(g => new ViewGroupWithColumns(
            g,
            links.Where(l => l.GroupViewId == g.Id && colById.ContainsKey(l.ColumnViewId))
                 .Select(l => colById[l.ColumnViewId])
                 .OrderBy(c => c.Code)
                 .ToList())).ToList();
    }

    /// <summary>Danh sách cột hiển thị của 1 nhóm (đã lọc cột còn tồn tại), sắp theo mã.</summary>
    public async Task<List<ViewColumnView>> ColumnsOfGroupAsync(Guid groupViewId)
    {
        var colIds = await db.ViewColumnInGroups.Where(l => l.GroupViewId == groupViewId)
            .Select(l => l.ColumnViewId).ToListAsync();
        if (colIds.Count == 0) return [];
        return await db.ViewColumnViews.Where(c => colIds.Contains(c.Id)).OrderBy(c => c.Code).ToListAsync();
    }

    /// <summary>
    /// Thay thế toàn bộ cột của 1 nhóm cột hiển thị (↔ ViewColumnInGroupSaveX).
    /// Xoá hết liên kết hiện có rồi ghi lại đúng tập <paramref name="columnCodes"/>.
    /// Ràng buộc: nhóm phải tồn tại &amp; đang hoạt động; mỗi cột phải tồn tại &amp; đang hoạt động;
    /// danh sách cột không được rỗng.
    /// </summary>
    public async Task<GroupResult> SetColumnsAsync(Guid groupViewId, IEnumerable<string> columnCodes)
    {
        var group = await db.ViewGroupViews.FirstOrDefaultAsync(g => g.Id == groupViewId);
        if (group == null) return GroupResult.Fail("Không tìm thấy nhóm cột hiển thị.");
        if (!group.IsActive) return GroupResult.Fail("Nhóm cột hiển thị đang bị khoá.");

        var codes = columnCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
        if (codes.Count == 0) return GroupResult.Fail("Danh sách cột hiển thị không được rỗng.");

        var cols = await db.ViewColumnViews.Where(c => codes.Contains(c.Code)).ToListAsync();
        if (cols.Count != codes.Count) return GroupResult.Fail("Có cột hiển thị không tồn tại.");
        var inactive = cols.FirstOrDefault(c => !c.IsActive);
        if (inactive != null) return GroupResult.Fail($"Cột hiển thị '{inactive.Code}' đang bị khoá.");

        // Clear-all → insert-all.
        var existing = await db.ViewColumnInGroups.Where(l => l.GroupViewId == groupViewId).ToListAsync();
        db.ViewColumnInGroups.RemoveRange(existing);
        foreach (var c in cols)
            db.ViewColumnInGroups.Add(new ViewColumnInGroup { GroupViewId = groupViewId, ColumnViewId = c.Id });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>
    /// Xoá 1 nhóm cột hiển thị kèm dọn liên kết cột (↔ ViewColumnInGroupSaveX nhánh FlagIsDelete):
    /// xoá mọi liên kết View_ColumnInGroup của nhóm trước khi xoá nhóm.
    /// </summary>
    public async Task<bool> DeleteGroupAsync(Guid groupViewId)
    {
        var group = await db.ViewGroupViews.FirstOrDefaultAsync(g => g.Id == groupViewId);
        if (group == null) return false;

        db.ViewColumnInGroups.RemoveRange(await db.ViewColumnInGroups.Where(l => l.GroupViewId == groupViewId).ToListAsync());
        db.ViewGroupViews.Remove(group);
        await db.SaveChangesAsync();
        return true;
    }
}
