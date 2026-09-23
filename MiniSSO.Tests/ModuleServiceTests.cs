using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test phân hệ chức năng Module → Function (port từ iNOS.InBrand:
/// SysModule / SysFunction / SysFunctionInModule + SysModuleManager.GetAllByUser).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class ModuleServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly ModuleService _svc;

    private readonly Module _sso, _user, _group;
    private readonly Function _fCreate, _fLock, _fGrant;

    public ModuleServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new ModuleService(_db);

        _sso = new Module { Code = "sso", Title = "Định danh", ModuleType = "MENU", SortOrder = 1 };
        _user = new Module { Code = "sso.user", Title = "Người dùng", ModuleType = "PAGE", ParentId = _sso.Id, SortOrder = 1 };
        _group = new Module { Code = "sso.group", Title = "Nhóm", ModuleType = "PAGE", ParentId = _sso.Id, SortOrder = 2 };
        _db.Modules.AddRange(_sso, _user, _group);

        _fCreate = new Function { Code = "user.create", Description = "Tạo người dùng" };
        _fLock = new Function { Code = "user.lock", Description = "Khoá tài khoản" };
        _fGrant = new Function { Code = "group.grant", Description = "Cấp quyền" };
        _db.Functions.AddRange(_fCreate, _fLock, _fGrant);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private AppUser SeedUser(string email)
    {
        var u = new AppUser { Email = email, FullName = email, PasswordHash = PasswordHasher.Hash("x") };
        _db.Users.Add(u); _db.SaveChanges();
        return u;
    }

    private Group SeedGroup(string code, bool active = true)
    {
        var g = new Group { Code = code, Name = code, IsActive = active };
        _db.Groups.Add(g); _db.SaveChanges();
        return g;
    }

    private PermissionObject SeedObject(string code, string? module)
    {
        var o = new PermissionObject { Code = code, Name = code, Module = module };
        _db.PermissionObjects.Add(o); _db.SaveChanges();
        return o;
    }

    [Fact]
    public async Task SetFunctions_ReplacesAll()
    {
        await _svc.SetFunctionsAsync(_user.Id, new[] { "user.create", "user.lock" });
        Assert.Equal(2, await _db.FunctionInModules.CountAsync(l => l.ModuleId == _user.Id));

        await _svc.SetFunctionsAsync(_user.Id, new[] { "user.lock" });
        var codes = await _db.FunctionInModules.Where(l => l.ModuleId == _user.Id)
            .Join(_db.Functions, l => l.FunctionId, f => f.Id, (l, f) => f.Code).ToListAsync();
        Assert.Single(codes);
        Assert.Equal("user.lock", codes[0]);
    }

    [Fact]
    public async Task SetFunctions_RejectsUnknownFunction()
    {
        var res = await _svc.SetFunctionsAsync(_user.Id, new[] { "nope.code" });
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.FunctionInModules.CountAsync(l => l.ModuleId == _user.Id));
    }

    [Fact]
    public async Task SetFunctions_UnknownModule_Fails()
    {
        var res = await _svc.SetFunctionsAsync(Guid.NewGuid(), new[] { "user.create" });
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task AllWithFunctions_ReturnsTreeWithFunctions()
    {
        await _svc.SetFunctionsAsync(_user.Id, new[] { "user.create", "user.lock" });
        await _svc.SetFunctionsAsync(_group.Id, new[] { "group.grant" });

        var all = await _svc.AllWithFunctionsAsync();
        Assert.Equal(3, all.Count);
        var user = all.Single(x => x.Module.Code == "sso.user");
        Assert.Equal(2, user.Functions.Count);
        var group = all.Single(x => x.Module.Code == "sso.group");
        Assert.Single(group.Functions);
    }

    [Fact]
    public async Task MenuForUser_NoGroups_Empty()
    {
        var u = SeedUser("a@t.dev");
        Assert.Empty(await _svc.MenuForUserAsync(u.Id));
    }

    [Fact]
    public async Task MenuForUser_UnionsModulesAcrossActiveGroups()
    {
        var u = SeedUser("a@t.dev");
        var g1 = SeedGroup("G1");
        var g2 = SeedGroup("G2");
        _db.GroupMembers.AddRange(
            new GroupMember { GroupId = g1.Id, UserId = u.Id },
            new GroupMember { GroupId = g2.Id, UserId = u.Id });

        // G1 → quyền "user.manage" (module sso.user); G2 → quyền "group.manage" (module sso.group).
        var oUser = SeedObject("user.manage", "sso.user");
        var oGroup = SeedObject("group.manage", "sso.group");
        _db.GroupAccesses.AddRange(
            new GroupAccess { GroupId = g1.Id, ObjectId = oUser.Id },
            new GroupAccess { GroupId = g2.Id, ObjectId = oGroup.Id });
        await _db.SaveChangesAsync();

        await _svc.SetFunctionsAsync(_user.Id, new[] { "user.create" });
        await _svc.SetFunctionsAsync(_group.Id, new[] { "group.grant" });

        var menu = await _svc.MenuForUserAsync(u.Id);
        Assert.Equal(2, menu.Count);
        Assert.Contains(menu, x => x.Module.Code == "sso.user" && x.Functions.Any(f => f.Code == "user.create"));
        Assert.Contains(menu, x => x.Module.Code == "sso.group" && x.Functions.Any(f => f.Code == "group.grant"));
    }

    [Fact]
    public async Task MenuForUser_InactiveGroup_Excluded()
    {
        var u = SeedUser("a@t.dev");
        var g = SeedGroup("G1", active: false);
        _db.GroupMembers.Add(new GroupMember { GroupId = g.Id, UserId = u.Id });
        var o = SeedObject("user.manage", "sso.user");
        _db.GroupAccesses.Add(new GroupAccess { GroupId = g.Id, ObjectId = o.Id });
        await _db.SaveChangesAsync();

        Assert.Empty(await _svc.MenuForUserAsync(u.Id));
    }

    [Fact]
    public async Task MenuForUser_ObjectWithoutModule_Ignored()
    {
        var u = SeedUser("a@t.dev");
        var g = SeedGroup("G1");
        _db.GroupMembers.Add(new GroupMember { GroupId = g.Id, UserId = u.Id });
        var o = SeedObject("report.view", module: null);   // không gắn module
        _db.GroupAccesses.Add(new GroupAccess { GroupId = g.Id, ObjectId = o.Id });
        await _db.SaveChangesAsync();

        Assert.Empty(await _svc.MenuForUserAsync(u.Id));
    }

    [Fact]
    public async Task Subtree_ReturnsSelfAndDescendants()
    {
        var nodes = await _svc.SubtreeAsync(_sso.Id);
        Assert.Equal(3, nodes.Count);
        Assert.Contains(nodes, m => m.Code == "sso");
        Assert.Contains(nodes, m => m.Code == "sso.user");
        Assert.Contains(nodes, m => m.Code == "sso.group");

        var leaf = await _svc.SubtreeAsync(_user.Id);
 Assert.Single(leaf);
    }
}
