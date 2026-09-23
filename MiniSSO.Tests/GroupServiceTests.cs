using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test quản lý thành viên/quyền của nhóm (port từ iNOS.InBrand:
/// SysUserInGroupManager.SysUserInGroupSave + SysAccessManager.SysAccessSave + SysGroupManager.Remove).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class GroupServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GroupService _svc;

    private readonly Org _north, _south;
    private readonly PermissionObject _pUser, _pReport;

    public GroupServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new GroupService(_db);

        _north = new Org { Code = "10", Name = "Miền Bắc" };
        _south = new Org { Code = "20", Name = "Miền Nam" };
        _db.Orgs.AddRange(_north, _south);
        _pUser = new PermissionObject { Code = "user.manage", Name = "Quản lý người dùng" };
        _pReport = new PermissionObject { Code = "report.view", Name = "Xem báo cáo" };
        _db.PermissionObjects.AddRange(_pUser, _pReport);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private AppUser SeedUser(string email, Guid? orgId = null)
    {
        var u = new AppUser { Email = email, FullName = email, PasswordHash = PasswordHasher.Hash("x"), OrgId = orgId };
        _db.Users.Add(u); _db.SaveChanges();
        return u;
    }

    private Group SeedGroup(string code, Guid? orgId = null)
    {
        var g = new Group { Code = code, Name = code, OrgId = orgId };
        _db.Groups.Add(g); _db.SaveChanges();
        return g;
    }

    [Fact]
    public async Task SetMembers_ReplacesAll_NotIncremental()
    {
        var g = SeedGroup("G1");
        var a = SeedUser("a@t.dev");
        var b = SeedUser("b@t.dev");
        var c = SeedUser("c@t.dev");

        await _svc.SetMembersAsync(g.Id, new[] { a.Id, b.Id });
        Assert.Equal(2, await _db.GroupMembers.CountAsync(m => m.GroupId == g.Id));

        // Lưu lại với tập mới → thay thế toàn bộ (b bị bỏ, c được thêm).
        await _svc.SetMembersAsync(g.Id, new[] { a.Id, c.Id });
        var ids = await _db.GroupMembers.Where(m => m.GroupId == g.Id).Select(m => m.UserId).ToListAsync();
        Assert.Equal(2, ids.Count);
        Assert.Contains(a.Id, ids);
        Assert.Contains(c.Id, ids);
        Assert.DoesNotContain(b.Id, ids);
    }

    [Fact]
    public async Task SetMembers_Empty_ClearsAll()
    {
        var g = SeedGroup("G1");
        var a = SeedUser("a@t.dev");
        await _svc.SetMembersAsync(g.Id, new[] { a.Id });
        await _svc.SetMembersAsync(g.Id, Array.Empty<Guid>());
        Assert.Equal(0, await _db.GroupMembers.CountAsync(m => m.GroupId == g.Id));
    }

    [Fact]
    public async Task SetMembers_RejectsUserFromDifferentOrg()
    {
        var g = SeedGroup("G1", orgId: _north.Id);
        var same = SeedUser("same@t.dev", orgId: _north.Id);
        var other = SeedUser("other@t.dev", orgId: _south.Id);

        var res = await _svc.SetMembersAsync(g.Id, new[] { same.Id, other.Id });
        Assert.False(res.Ok);
        Assert.NotNull(res.Error);
        // Không ghi gì khi vi phạm ràng buộc.
        Assert.Equal(0, await _db.GroupMembers.CountAsync(m => m.GroupId == g.Id));
    }

    [Fact]
    public async Task SetMembers_GlobalGroup_AcceptsAnyOrg()
    {
        var g = SeedGroup("G1");   // OrgId = null → toàn cục
        var a = SeedUser("a@t.dev", orgId: _north.Id);
        var b = SeedUser("b@t.dev", orgId: _south.Id);
        var res = await _svc.SetMembersAsync(g.Id, new[] { a.Id, b.Id });
        Assert.True(res.Ok);
        Assert.Equal(2, await _db.GroupMembers.CountAsync(m => m.GroupId == g.Id));
    }

    [Fact]
    public async Task SetMembers_RejectsUnknownUser()
    {
        var g = SeedGroup("G1");
        var res = await _svc.SetMembersAsync(g.Id, new[] { Guid.NewGuid() });
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task SetAccess_ReplacesAll()
    {
        var g = SeedGroup("G1");
        await _svc.SetAccessAsync(g.Id, new[] { "user.manage", "report.view" });
        Assert.Equal(2, await _db.GroupAccesses.CountAsync(a => a.GroupId == g.Id));

        await _svc.SetAccessAsync(g.Id, new[] { "report.view" });
        var objIds = await _db.GroupAccesses.Where(a => a.GroupId == g.Id).Select(a => a.ObjectId).ToListAsync();
        Assert.Single(objIds);
        Assert.Equal(_pReport.Id, objIds[0]);
    }

    [Fact]
    public async Task SetAccess_RejectsUnknownObject()
    {
        var g = SeedGroup("G1");
        var res = await _svc.SetAccessAsync(g.Id, new[] { "nope.code" });
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.GroupAccesses.CountAsync(a => a.GroupId == g.Id));
    }

    [Fact]
    public async Task DeleteGroup_CascadesMembersAndAccess()
    {
        var g = SeedGroup("G1");
        var a = SeedUser("a@t.dev");
        await _svc.SetMembersAsync(g.Id, new[] { a.Id });
        await _svc.SetAccessAsync(g.Id, new[] { "user.manage" });

        Assert.True(await _svc.DeleteGroupAsync(g.Id));
        Assert.False(await _db.Groups.AnyAsync(x => x.Id == g.Id));
        Assert.Equal(0, await _db.GroupMembers.CountAsync(m => m.GroupId == g.Id));
        Assert.Equal(0, await _db.GroupAccesses.CountAsync(x => x.GroupId == g.Id));
        // Người dùng vẫn còn (chỉ xoá liên kết nhóm).
        Assert.True(await _db.Users.AnyAsync(u => u.Id == a.Id));
    }

    [Fact]
    public async Task DeleteGroup_Unknown_ReturnsFalse()
    {
        Assert.False(await _svc.DeleteGroupAsync(Guid.NewGuid()));
    }
}