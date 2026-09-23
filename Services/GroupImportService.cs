using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Kết quả 1 lần nhập nhóm từ file (kèm lý do khi bị từ chối + số nhóm đã thêm).</summary>
public sealed record GroupImportResult(bool Ok, string? Error, int Imported)
{
    public static GroupImportResult Success(int imported) => new(true, null, imported);
    public static GroupImportResult Fail(string error) => new(false, error, 0);
}

/// <summary>
/// Nhập danh sách NHÓM từ file (port từ iNOS.InBrand: SysGroupController.Import).
///
/// iNOS cho phép nhập hàng loạt nhóm người dùng từ file Excel: đọc bảng bắt đầu từ ô "A2", rồi
/// kiểm tra lần lượt TRƯỚC KHI ghi bất kỳ dòng nào:
///   • file phải đúng định dạng (.xlsx/.xls) và đúng SỐ CỘT (3 cột: Code, DLCode, Description);
///   • mỗi dòng: Code / DLCode / Description KHÔNG được trống;
///   • Description không vượt quá <c>ParamsClient.RemarkLength</c> (= 400) ký tự;
///   • Code không được LẶP trong chính file;
///   • nếu mọi dòng hợp lệ thì mới thêm từng nhóm với Enable = true.
///
/// MiniSSO trước đây chỉ tạo nhóm ĐƠN LẺ (GroupController.Create) — KHÔNG có luồng nhập hàng loạt.
/// Service này tái tạo đúng các quy tắc kiểm tra của iNOS; phần "đọc file" dùng CSV (thay cho Excel)
/// vì MiniSSO không kèm thư viện đọc Excel — quy tắc nghiệp vụ giữ nguyên, chỉ khác định dạng đầu vào.
/// </summary>
public sealed class GroupImportService(AppDbContext db)
{
    /// <summary>Giới hạn độ dài mô tả (↔ iNOS ParamsClient.RemarkLength = 400).</summary>
    public const int RemarkLength = 400;

    /// <summary>Số cột bắt buộc của file (↔ iNOS: table.Columns.Count != 3).</summary>
    public const int RequiredColumns = 3;

    /// <summary>
    /// Nhập nhóm từ nội dung CSV. Mỗi dòng: <c>Code,DLCode,Description</c> (dòng đầu là tiêu đề, bỏ qua).
    /// Trả về lỗi (không ghi gì) nếu vi phạm bất kỳ quy tắc nào của iNOS; ngược lại thêm tất cả nhóm.
    /// </summary>
    public async Task<GroupImportResult> ImportCsvAsync(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return GroupImportResult.Fail("File import không có dữ liệu!");

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        // Dòng đầu là tiêu đề (↔ iNOS đọc từ ô "A2").
        if (lines.Count <= 1)
            return GroupImportResult.Fail("File import không có dữ liệu!");
        var rows = lines.Skip(1).ToList();

        // Đúng số cột (↔ iNOS: table.Columns.Count != 3 → "File excel import không hợp lệ!").
        var parsed = new List<(string Code, string DlCode, string Description)>();
        foreach (var line in rows)
        {
            var cells = line.Split(',');
            if (cells.Length != RequiredColumns)
                return GroupImportResult.Fail("File import không hợp lệ!");
            parsed.Add((cells[0].Trim(), cells[1].Trim(), cells[2].Trim()));
        }

        // Kiểm tra rỗng + độ dài mô tả (↔ iNOS "Check null").
        foreach (var (code, dlCode, description) in parsed)
        {
            if (string.IsNullOrEmpty(code))
                return GroupImportResult.Fail("Mã nhóm người dùng không được trống!");
            if (string.IsNullOrEmpty(dlCode))
                return GroupImportResult.Fail("Mã đơn vị không được trống!");
            if (string.IsNullOrEmpty(description))
                return GroupImportResult.Fail("Mô tả nhóm người dùng không được trống!");
            if (description.Length > RemarkLength)
                return GroupImportResult.Fail($"Mô tả nhóm người dùng > {RemarkLength} ký tự!");
        }

        // Kiểm tra trùng mã trong file (↔ iNOS "Check duplicate").
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, _, _) in parsed)
            if (!seen.Add(code))
                return GroupImportResult.Fail($"Mã nhóm người dùng '{code}' bị lặp trong file!");

        // Mọi dòng hợp lệ → thêm nhóm (↔ iNOS: item.Enable = true; SysGroupService.Add).
        // Đơn vị (DLCode) tra theo Org.Code; không có thì để null (nhóm toàn cục).
        var orgByCode = await db.Orgs.ToDictionaryAsync(o => o.Code, o => o.Id);
        foreach (var (code, dlCode, description) in parsed)
        {
            var normalized = code.ToUpperInvariant();
            if (await db.Groups.AnyAsync(g => g.Code == normalized))
                return GroupImportResult.Fail($"Mã nhóm người dùng '{code}' đã tồn tại trong hệ thống!");
            db.Groups.Add(new Group
            {
                Code = normalized,
                Name = normalized,
                Description = description,
                OrgId = orgByCode.TryGetValue(dlCode, out var oid) ? oid : null,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
        return GroupImportResult.Success(parsed.Count);
    }
}
