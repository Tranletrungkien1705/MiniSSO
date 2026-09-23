using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test gán module trực tiếp cho nhóm (port từ iNOS.InBrand: Sys_Access = GroupCode + ModuleCode,
/// SysGroupController.GetSysModule + SysAccessService.GetAllAccessByGroupCode + SysAccessSave_New20171101).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class GroupModuleServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly ModuleService _svc;

    private readonly Module _sso, _user, _report;

    public GroupModuleServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new ModuleService(_db);

        _sso = new Module { Code = "sso", Title = "Định danh", ModuleType = "MENU", SortOrder = 1 };
        _user = new Module { Code = "sso.user", Title = "Người dùng", ModuleType = "PAGE", ParentId = _sso.Id, SortOrder = 1 };
        _report = new Module { Code = "report", Title = "Báo cáo", ModuleType = "MENU", SortOrder = 2 };
        _db.Modules.AddRange(_sso, _user, _report);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private Group SeedGroup(string code, bool active = true)
    {
        var g = new Group { Code = code, Name = code, IsActive = active };
        _db.Groups.Add(g); _db.SaveChanges();
        return g;
    }

    [Fact]
    public async Task SetGroupModules_ReplacesAll()
    {
        var g = SeedGroup("G1");
        var res = await _svc.SetGroupModulesAsync(g.Id, new[] { "sso", "report" });
        Assert.True(res.Ok);
        Assert.Equal(2, await _db.GroupModuleAccesses.CountAsync(a => a.GroupId == g.Id));

        // Lưu lại tập mới → thay-thế toàn bộ (clear-all → insert-all).
        await _svc.SetGroupModulesAsync(g.Id, new[] { "report" });
        var codes = await _svc.GrantedModuleCodesAsync(g.Id);
        Assert.Single(codes);
        Assert.Equal("report", codes[0]);
    }

    [Fact]
    public async Task SetGroupModules_Empty_ClearsAll()
    {
        var g = SeedGroup("G1");
        await _svc.SetGroupModulesAsync(g.Id, new[] { "sso", "report" });
        var res = await _svc.SetGroupModulesAsync(g.Id, Array.Empty<string>());
        Assert.True(res.Ok);
        Assert.Empty(await _svc.GrantedModuleCodesAsync(g.Id));
    }

    [Fact]
    public async Task SetGroupModules_RejectsUnknownModule()
    {
        var g = SeedGroup("G1");
        var res = await _svc.SetGroupModulesAsync(g.Id, new[] { "nope.code" });
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.GroupModuleAccesses.CountAsync(a => a.GroupId == g.Id));
    }

    [Fact]
    public async Task SetGroupModules_UnknownGroup_Fails()
    {
        var res = await _svc.SetGroupModulesAsync(Guid.NewGuid(), new[] { "sso" });
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task GrantedModuleCodes_UnknownGroup_Empty()
    {
        Assert.Empty(await _svc.GrantedModuleCodesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GrantedModuleCodes_SortedByCode()
    {
        var g = SeedGroup("G1");
        await _svc.SetGroupModulesAsync(g.Id, new[] { "report", "sso" });
        var codes = await _svc.GrantedModuleCodesAsync(g.Id);
        Assert.Equal(new[] { "report", "sso" }, codes);
    }

    [Fact]
    public async Task ModulesForGroup_FlagsGranted()
    {
        var g = SeedGroup("G1");
        await _svc.SetGroupModulesAsync(g.Id, new[] { "sso.user" });

        var list = await _svc.ModulesForGroupAsync(g.Id);
        Assert.Equal(3, list.Count);
        Assert.True(list.Single(x => x.Module.Code == "sso.user").Granted);
        Assert.False(list.Single(x => x.Module.Code == "sso").Granted);
        Assert.False(list.Single(x => x.Module.Code == "report").Granted);
    }

    [Fact]
    public async Task ModulesForGroup_UnknownGroup_Empty()
    {
        Assert.Empty(await _svc.ModulesForGroupAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetGroupModules_DeduplicatesCodes()
    {
        var g = SeedGroup("G1");
        var res = await _svc.SetGroupModulesAsync(g.Id, new[] { "sso", "sso", " report " });
        Assert.True(res.Ok);
        Assert.Equal(2, await _db.GroupModuleAccesses.CountAsync(a => a.GroupId == g.Id));
    }
}
