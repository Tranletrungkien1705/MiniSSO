using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Cây tổ chức (port từ iNOS.InBrand: Mst_Org + Mst_Org_UpdBU).
/// iNOS lưu cây dạng "materialized path": mỗi nút có cha (OrgParent) và 3 cột dẫn xuất
/// OrgBUCode (đường dẫn mã, vd "0.10.20"), OrgBUPattern (tiền tố nhánh, vd "0.10.20%"),
/// OrgLevel (độ sâu). Service này tái tạo lại 3 cột đó sau mỗi lần thêm/sửa/đổi cha,
/// để lấy nhanh toàn bộ nhánh con của 1 đơn vị bằng 1 truy vấn tiền tố.
/// </summary>
public sealed class OrgService(AppDbContext db)
{
    /// <summary>Mã gốc quy ước (tương ứng @strOrgID_Root = '0' trong iNOS).</summary>
    public const string RootCode = "0";

    /// <summary>
    /// Tính lại BuCode/BuPattern/Level cho toàn bộ cây (tương ứng Mst_Org_UpdBU).
    /// Gốc (ParentId == null) nhận BuCode = "0", Level = 1; con = "&lt;BuCode cha&gt;.&lt;Code&gt;", Level = cha + 1.
    /// </summary>
    public async Task RebuildPathsAsync()
    {
        var all = await db.Orgs.ToListAsync();
        var byId = all.ToDictionary(o => o.Id);
        var childrenOf = all
            .Where(o => o.ParentId != null && byId.ContainsKey(o.ParentId.Value))
            .GroupBy(o => o.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Duyệt theo chiều sâu từ các nút gốc; nút mồ côi (cha không tồn tại) coi như gốc.
        var roots = all.Where(o => o.ParentId == null || !byId.ContainsKey(o.ParentId.Value)).ToList();
        var visited = new HashSet<Guid>();
        var stack = new Stack<Org>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node.Id)) continue;   // chống vòng lặp nếu dữ liệu lỗi

            if (node.ParentId != null && byId.TryGetValue(node.ParentId.Value, out var parent) && visited.Contains(parent.Id))
            {
                node.BuCode = $"{parent.BuCode}.{node.Code}";
                node.Level = parent.Level + 1;
            }
            else
            {
                node.BuCode = RootCode;
                node.Level = 1;
            }
            node.BuPattern = node.BuCode + "%";

            if (childrenOf.TryGetValue(node.Id, out var kids))
                foreach (var k in kids) stack.Push(k);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Toàn bộ nhánh con (kể cả chính nó) của 1 đơn vị, dựa trên tiền tố BuPattern.</summary>
    public async Task<List<Org>> SubtreeAsync(Guid orgId)
    {
        var org = await db.Orgs.FirstOrDefaultAsync(o => o.Id == orgId);
        if (org == null) return [];
        return await db.Orgs
            .Where(o => o.BuCode == org.BuCode || o.BuCode.StartsWith(org.BuCode + "."))
            .OrderBy(o => o.BuCode)
            .ToListAsync();
    }

    /// <summary>Đường dẫn tổ tiên (từ gốc tới chính nó) của 1 đơn vị.</summary>
    public async Task<List<Org>> AncestorsAsync(Guid orgId)
    {
        var all = await db.Orgs.ToListAsync();
        var byId = all.ToDictionary(o => o.Id);
        var chain = new List<Org>();
        var cur = all.FirstOrDefault(o => o.Id == orgId);
        var guard = 0;
        while (cur != null && guard++ < 64)
        {
            chain.Insert(0, cur);
            cur = cur.ParentId != null && byId.TryGetValue(cur.ParentId.Value, out var p) ? p : null;
        }
        return chain;
    }
}
