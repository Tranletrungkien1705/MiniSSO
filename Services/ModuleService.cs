using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>Một module kèm danh sách chức năng con (dùng để dựng menu).</summary>
public sealed record ModuleWithFunctions(Module Module, List<Function> Functions);

/// <summary>
/// Phân hệ chức năng: cây Module → Function (port từ iNOS.InBrand:
/// SysModule / SysFunction / SysFunctionInModule + SysModuleManager.GetAllByUser).
///
/// iNOS tổ chức menu thành CÂY module (SysModule.ParentCode) và mỗi module gồm nhiều chức năng
/// (SysFunction) qua bảng nối SysFunctionInModule. Nhóm được cấp quyền tới MODULE (SysAccess);
/// "menu hiệu lực" của người dùng = các module (kèm chức năng) mà mọi nhóm đang hoạt động của họ
/// được cấp. MiniSSO trước đây chỉ có PermissionObject phẳng — service này tái tạo đúng ngữ nghĩa.
/// </summary>
public sealed class ModuleService(AppDbContext db)
{
    /// <summary>Toàn bộ module (kèm chức năng) đang hoạt động, sắp theo thứ tự hiển thị.</summary>
    public async Task<List<ModuleWithFunctions>> AllWithFunctionsAsync()
    {
        var modules = await db.Modules.Where(m => m.IsActive).OrderBy(m => m.SortOrder).ThenBy(m => m.Code).ToListAsync();
        var links = await db.FunctionInModules.ToListAsync();
        var functions = await db.Functions.Where(f => f.IsActive).ToListAsync();
        var fnById = functions.ToDictionary(f => f.Id);

        return modules.Select(m => new ModuleWithFunctions(
            m,
            links.Where(l => l.ModuleId == m.Id && fnById.ContainsKey(l.FunctionId))
                 .Select(l => fnById[l.FunctionId])
                 .OrderBy(f => f.Code)
                 .ToList())).ToList();
    }

    /// <summary>
    /// Menu hiệu lực của 1 người dùng (↔ SysModuleManager.GetAllByUser):
    /// lấy các nhóm đang hoạt động của người dùng → các đối tượng quyền (PermissionObject) được cấp →
    /// các module có Code trùng với PermissionObject.Module → kèm chức năng của module đó.
    /// Trả về danh sách module (đã lọc hoạt động) kèm chức năng, sắp theo thứ tự hiển thị.
    /// </summary>
    public async Task<List<ModuleWithFunctions>> MenuForUserAsync(Guid userId)
    {
        var groupIds = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        if (groupIds.Count == 0) return [];

        var activeGroupIds = await db.Groups
            .Where(g => groupIds.Contains(g.Id) && g.IsActive)
            .Select(g => g.Id).ToListAsync();
        if (activeGroupIds.Count == 0) return [];

        // Đối tượng quyền hiệu lực → mã module (PermissionObject.Module) mà người dùng được chạm tới.
        var objectIds = await db.GroupAccesses
            .Where(a => activeGroupIds.Contains(a.GroupId))
            .Select(a => a.ObjectId).Distinct().ToListAsync();

        var moduleCodes = await db.PermissionObjects
            .Where(o => objectIds.Contains(o.Id) && o.IsActive && o.Module != null)
            .Select(o => o.Module!)
            .Distinct()
            .ToListAsync();
        if (moduleCodes.Count == 0) return [];

        var modules = await db.Modules
            .Where(m => m.IsActive && moduleCodes.Contains(m.Code))
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Code)
            .ToListAsync();
        if (modules.Count == 0) return [];

        var moduleIds = modules.Select(m => m.Id).ToList();
        var links = await db.FunctionInModules.Where(l => moduleIds.Contains(l.ModuleId)).ToListAsync();
        var functions = await db.Functions.Where(f => f.IsActive).ToListAsync();
        var fnById = functions.ToDictionary(f => f.Id);

        return modules.Select(m => new ModuleWithFunctions(
            m,
            links.Where(l => l.ModuleId == m.Id && fnById.ContainsKey(l.FunctionId))
                 .Select(l => fnById[l.FunctionId])
                 .OrderBy(f => f.Code)
                 .ToList())).ToList();
    }

    /// <summary>
    /// Thay thế toàn bộ chức năng của 1 module (↔ SysFunctionInModule save theo cơ chế clear-all → insert-all).
    /// Xoá hết liên kết hiện có rồi ghi lại đúng tập <paramref name="functionCodes"/>.
    /// </summary>
    public async Task<GroupResult> SetFunctionsAsync(Guid moduleId, IEnumerable<string> functionCodes)
    {
        if (!await db.Modules.AnyAsync(m => m.Id == moduleId)) return GroupResult.Fail("Không tìm thấy module.");

        var codes = functionCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
        var fns = await db.Functions.Where(f => codes.Contains(f.Code)).ToListAsync();
        if (fns.Count != codes.Count) return GroupResult.Fail("Có chức năng không tồn tại.");

        var existing = await db.FunctionInModules.Where(l => l.ModuleId == moduleId).ToListAsync();
        db.FunctionInModules.RemoveRange(existing);
        foreach (var f in fns)
            db.FunctionInModules.Add(new FunctionInModule { ModuleId = moduleId, FunctionId = f.Id });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }

    /// <summary>Toàn bộ nhánh con (kể cả chính nó) của 1 module, dựa trên quan hệ cha-con.</summary>
    public async Task<List<Module>> SubtreeAsync(Guid moduleId)
    {
        var all = await db.Modules.ToListAsync();
        var byId = all.ToDictionary(m => m.Id);
        if (!byId.ContainsKey(moduleId)) return [];

        var result = new List<Module>();
        var stack = new Stack<Guid>();
        stack.Push(moduleId);
        var visited = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id) || !byId.TryGetValue(id, out var node)) continue;
            result.Add(node);
            foreach (var child in all.Where(m => m.ParentId == id))
                stack.Push(child.Id);
        }
        return result.OrderBy(m => m.SortOrder).ThenBy(m => m.Code).ToList();
    }

    // ── Gán module trực tiếp cho nhóm (port từ iNOS.InBrand: Sys_Access = GroupCode + ModuleCode) ──
    // iNOS nối TRỰC TIẾP nhóm ↔ module qua bảng Sys_Access. Màn hình SysGroupController.GetSysModule
    // liệt kê TẤT CẢ module kèm cờ "đã gán cho nhóm này chưa" (SysAccessService.GetAllAccessByGroupCode),
    // và SaveModuleInGroup → SysAccessSave_New20171101 lưu theo cơ chế "xoá sạch rồi ghi lại".

    /// <summary>
    /// Mã các module ĐANG được gán cho 1 nhóm (↔ SysAccessService.GetAllAccessByGroupCode).
    /// Trả về rỗng nếu nhóm không tồn tại.
    /// </summary>
    public async Task<List<string>> GrantedModuleCodesAsync(Guid groupId)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return [];
        var moduleIds = await db.GroupModuleAccesses.Where(a => a.GroupId == groupId).Select(a => a.ModuleId).ToListAsync();
        return await db.Modules.Where(m => moduleIds.Contains(m.Id)).OrderBy(m => m.Code).Select(m => m.Code).ToListAsync();
    }

    /// <summary>
    /// TẤT CẢ module kèm cờ "đã gán cho nhóm này chưa" (↔ SysGroupController.GetSysModule):
    /// dựng màn hình "Gán module vào nhóm" — mỗi module có <c>Granted</c> = nhóm đang được cấp module đó.
    /// Trả về rỗng nếu nhóm không tồn tại.
    /// </summary>
    public async Task<List<ModuleGrant>> ModulesForGroupAsync(Guid groupId)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return [];
        var grantedIds = (await db.GroupModuleAccesses.Where(a => a.GroupId == groupId).Select(a => a.ModuleId).ToListAsync()).ToHashSet();
        var modules = await db.Modules.OrderBy(m => m.SortOrder).ThenBy(m => m.Code).ToListAsync();
        return modules.Select(m => new ModuleGrant(m, grantedIds.Contains(m.Id))).ToList();
    }

    /// <summary>
    /// Thay thế toàn bộ module được gán cho nhóm (↔ SysAccessSave_New20171101, cơ chế clear-all → insert-all).
    /// Xoá hết liên kết hiện có rồi ghi lại đúng tập <paramref name="moduleCodes"/>.
    /// Ràng buộc: nhóm phải tồn tại; mọi module phải tồn tại (↔ MstModuleCheckDB).
    /// </summary>
    public async Task<GroupResult> SetGroupModulesAsync(Guid groupId, IEnumerable<string> moduleCodes)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return GroupResult.Fail("Không tìm thấy nhóm.");

        var codes = moduleCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
        var mods = await db.Modules.Where(m => codes.Contains(m.Code)).ToListAsync();
        if (mods.Count != codes.Count) return GroupResult.Fail("Có module không tồn tại.");

        // Clear-all → insert-all.
        var existing = await db.GroupModuleAccesses.Where(a => a.GroupId == groupId).ToListAsync();
        db.GroupModuleAccesses.RemoveRange(existing);
        foreach (var m in mods)
            db.GroupModuleAccesses.Add(new GroupModuleAccess { GroupId = groupId, ModuleId = m.Id });
        await db.SaveChangesAsync();
        return GroupResult.Success();
    }
}

/// <summary>1 module kèm cờ đã gán cho nhóm hay chưa (dùng cho màn hình "Gán module vào nhóm").</summary>
public sealed record ModuleGrant(Module Module, bool Granted);
