using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;

namespace MiniSSO.Services;

/// <summary>
/// Phân quyền theo nhóm (port từ iNOS.InBrand: Sys_UserInGroup → Sys_Group → Sys_Access → Sys_Object).
/// "Quyền hiệu lực" của 1 người dùng = hợp (union) các đối tượng quyền của mọi nhóm đang hoạt động mà họ thuộc.
/// </summary>
public sealed class RbacService(AppDbContext db)
{
    /// <summary>Mã các đối tượng quyền hiệu lực của người dùng (đã lọc nhóm/đối tượng còn hoạt động).</summary>
    public async Task<string[]> EffectivePermissionsAsync(Guid userId)
    {
        var groupIds = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        if (groupIds.Count == 0) return [];

        var activeGroupIds = await db.Groups
            .Where(g => groupIds.Contains(g.Id) && g.IsActive)
            .Select(g => g.Id).ToListAsync();
        if (activeGroupIds.Count == 0) return [];

        var objectIds = await db.GroupAccesses
            .Where(a => activeGroupIds.Contains(a.GroupId))
            .Select(a => a.ObjectId).Distinct().ToListAsync();

        return await db.PermissionObjects
            .Where(o => objectIds.Contains(o.Id) && o.IsActive)
            .Select(o => o.Code)
            .Distinct()
            .OrderBy(c => c)
            .ToArrayAsync();
    }

    /// <summary>Mã các nhóm đang hoạt động mà người dùng thuộc về.</summary>
    public async Task<string[]> GroupCodesAsync(Guid userId)
    {
        var groupIds = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        if (groupIds.Count == 0) return [];
        return await db.Groups
            .Where(g => groupIds.Contains(g.Id) && g.IsActive)
            .Select(g => g.Code)
            .Distinct()
            .OrderBy(c => c)
            .ToArrayAsync();
    }

    /// <summary>Người dùng có quyền hiệu lực với đối tượng quyền <paramref name="objectCode"/> hay không.</summary>
    public async Task<bool> HasPermissionAsync(Guid userId, string objectCode)
    {
        var perms = await EffectivePermissionsAsync(userId);
        return perms.Contains(objectCode, StringComparer.OrdinalIgnoreCase);
    }
}
