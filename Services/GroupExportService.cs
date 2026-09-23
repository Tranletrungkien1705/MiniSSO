using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Xuất danh sách NHÓM ra file (port từ iNOS.InBrand: SysGroupController.Export + ExportTemplate
/// + GetImportDicColums / GetImportDicColumsTemplate).
///
/// iNOS cho phép xuất toàn bộ nhóm người dùng ra Excel theo 2 chế độ:
///   • <b>Export</b> — xuất dữ liệu thật: mỗi nhóm gồm các cột Code, DLCode, Description, Enable
///     (GetImportDicColums), lấy từ SysGroupService.GetAll(sessionId, "Code, Description, Enable");
///   • <b>ExportTemplate</b> — xuất file MẪU rỗng chỉ có tiêu đề Code, DLCode, Description
///     (GetImportDicColumsTemplate) để người dùng điền rồi nhập lại qua luồng Import.
///
/// MiniSSO trước đây chỉ có luồng NHẬP (GroupImportService) mà KHÔNG có luồng XUẤT tương ứng.
/// Service này tái tạo đúng 2 chế độ trên; phần "ghi file" dùng CSV (thay cho Excel) vì MiniSSO
/// không kèm thư viện Excel — quy tắc nghiệp vụ (tập cột + tiêu đề) giữ nguyên, chỉ khác định dạng.
/// </summary>
public sealed class GroupExportService(AppDbContext db)
{
    /// <summary>Tiêu đề cột khi xuất dữ liệu thật (↔ iNOS GetImportDicColums).</summary>
    public static readonly string[] DataHeaders = { "Code", "DLCode", "Description", "Enable" };

    /// <summary>Tiêu đề cột của file mẫu (↔ iNOS GetImportDicColumsTemplate — không có cột Enable).</summary>
    public static readonly string[] TemplateHeaders = { "Code", "DLCode", "Description" };

    /// <summary>
    /// Xuất toàn bộ nhóm ra nội dung CSV (↔ SysGroupController.Export).
    /// Mỗi dòng: <c>Code,DLCode,Description,Enable</c>; DLCode là mã đơn vị (Org.Code) hoặc rỗng nếu nhóm toàn cục;
    /// Enable = "1"/"0" (↔ TConst.Flag.Yes/No). Sắp theo mã nhóm. Luôn có dòng tiêu đề.
    /// </summary>
    public async Task<string> ExportCsvAsync()
    {
        var groups = await db.Groups.OrderBy(g => g.Code).ToListAsync();
        var orgById = await db.Orgs.ToDictionaryAsync(o => o.Id, o => o.Code);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(',', DataHeaders));
        foreach (var g in groups)
        {
            var dlCode = g.OrgId != null && orgById.TryGetValue(g.OrgId.Value, out var c) ? c : "";
            sb.AppendLine(string.Join(',', Csv(g.Code), Csv(dlCode), Csv(g.Description ?? ""), g.IsActive ? "1" : "0"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Xuất file MẪU rỗng (↔ SysGroupController.ExportTemplate): chỉ có dòng tiêu đề
    /// <c>Code,DLCode,Description</c> để người dùng điền rồi nhập lại qua luồng Import.
    /// </summary>
    public string ExportTemplateCsv()
        => string.Join(',', TemplateHeaders) + "\n";

    /// <summary>Bọc 1 ô CSV: nếu chứa dấu phẩy / nháy kép / xuống dòng thì bọc trong nháy kép và nhân đôi nháy kép.</summary>
    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
