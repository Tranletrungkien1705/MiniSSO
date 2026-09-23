using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Loại đại lý (port từ iNOS.InBrand: Mst_DealerType + MstDealerTypeManager).
///
/// iNOS có bảng Mst_DealerType (DLType PK, DLTypeName, FlagActive) — danh mục "loại đại lý" dùng để
/// PHÂN LOẠI các đại lý/đơn vị (Mst_Dealer.DLType trỏ tới đây). MstDealerTypeManager áp đúng mẫu
/// CheckDB dùng chung trước khi ghi:
///   • MstDealerTypeCheckDB(strFlagExistToCheck, strFlagActiveListToCheck):
///       - "1" (Flag.Active/Yes) → loại PHẢI tồn tại,
///       - "0" (Flag.Inactive/No) → loại PHẢI chưa tồn tại,
///       - trạng thái thực tế (FlagActive) phải nằm trong danh sách cờ cho phép.
///   • DLType [PrimaryKey][RequireField] → mã loại bắt buộc & duy nhất,
///   • DLTypeName [RequireField] → tên loại bắt buộc.
///
/// MiniSSO trước đây có Org (đơn vị tổ chức) nhưng KHÔNG có danh mục "loại đại lý" để phân loại đơn vị.
/// Service này bổ sung đúng danh mục đó, tái sử dụng <see cref="CheckDbService"/> cho mẫu CheckDB.
/// </summary>
public sealed class DealerTypeService(AppDbContext db, CheckDbService checkDb)
{
    /// <summary>Toàn bộ loại đại lý, sắp theo mã.</summary>
    public async Task<List<DealerType>> AllAsync()
        => await db.DealerTypes.OrderBy(t => t.Code).ToListAsync();

    /// <summary>Danh sách loại đại lý đang hoạt động (↔ lọc theo FlagActive = "1").</summary>
    public async Task<List<DealerType>> ActiveAsync()
        => await db.DealerTypes.Where(t => t.IsActive).OrderBy(t => t.Code).ToListAsync();

    /// <summary>
    /// Tạo 1 loại đại lý mới (↔ MstDealerTypeManager.Add). Ràng buộc theo đúng iNOS:
    ///   • mã loại bắt buộc (DLType [RequireField]),
    ///   • mã loại PHẢI CHƯA tồn tại (↔ MstDealerTypeCheckDB với strFlagExistToCheck = "0"),
    ///   • tên loại bắt buộc (DLTypeName [RequireField]).
    /// </summary>
    public async Task<GroupResult> CreateAsync(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code)) return GroupResult.Fail("Cần mã loại đại lý.");
        var c = code.Trim();

        // Kiểm tra theo mẫu CheckDB (↔ MstDealerTypeCheckDB): loại PHẢI CHƯA tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.DealerType, c, CheckDbService.FlagInactive);
        if (!chk.Ok) return GroupResult.Fail(chk.Error!);

        if (string.IsNullOrWhiteSpace(name)) return GroupResult.Fail("Cần tên loại đại lý.");

        db.DealerTypes.Add(new DealerType { Code = c, Name = name!.Trim() });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>
    /// Cập nhật tên của 1 loại đại lý (↔ MstDealerTypeManager.Update). Ràng buộc:
    ///   • loại PHẢI tồn tại (↔ MstDealerTypeCheckDB với strFlagExistToCheck = "1"),
    ///   • tên loại bắt buộc (DLTypeName [RequireField]).
    /// </summary>
    public async Task<GroupResult> UpdateAsync(Guid id, string? name)
    {
        var t = await db.DealerTypes.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return GroupResult.Fail("Không tìm thấy loại đại lý.");

        // Kiểm tra theo mẫu CheckDB (↔ MstDealerTypeCheckDB): loại PHẢI tồn tại.
        var chk = await checkDb.CheckAsync(CheckDbService.EntityKind.DealerType, t.Code, CheckDbService.FlagActive);
        if (!chk.Ok) return GroupResult.Fail(chk.Error!);

        if (string.IsNullOrWhiteSpace(name)) return GroupResult.Fail("Cần tên loại đại lý.");

        t.Name = name!.Trim();
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>Bật/tắt cờ hoạt động của loại đại lý (↔ Mst_DealerType.FlagActive).</summary>
    public async Task<bool> ToggleAsync(Guid id)
    {
        var t = await db.DealerTypes.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return false;
        t.IsActive = !t.IsActive;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Xoá 1 loại đại lý (↔ MstDealerTypeManager.Remove).</summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        var t = await db.DealerTypes.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return false;
        db.DealerTypes.Remove(t);
        await db.SaveChangesAsync();
        return true;
    }
}
