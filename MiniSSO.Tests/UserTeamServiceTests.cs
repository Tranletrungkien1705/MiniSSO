using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test đội người dùng (port từ iNOS.InBrand: Sys_UserTeam + SysUserTeamManager).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class UserTeamServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly UserTeamService _svc;

    private readonly Org _north, _inactive;

    public UserTeamServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new UserTeamService(_db);

        _north = new Org { Code = "10", Name = "Miền Bắc" };
        _inactive = new Org { Code = "99", Name = "Đơn vị khoá", IsActive = false };
        _db.Orgs.AddRange(_north, _inactive);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Create_AddsTeam()
    {
        var res = await _svc.CreateAsync("TEAM_HN1", "Đội Hà Nội 1", _north.Id);
        Assert.True(res.Ok);
        var t = await _db.UserTeams.SingleAsync();
        Assert.Equal("TEAM_HN1", t.Code);
        Assert.Equal("Đội Hà Nội 1", t.Name);
        Assert.Equal(_north.Id, t.OrgId);
        Assert.True(t.IsActive);
    }

    [Fact]
    public async Task Create_RequiresCode()
    {
        var res = await _svc.CreateAsync("  ", "x", null);
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.UserTeams.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsDuplicateCode()
    {
        await _svc.CreateAsync("TEAM_HN1", "Đội 1", null);
        var res = await _svc.CreateAsync("TEAM_HN1", "Đội khác", null);
        Assert.False(res.Ok);
        Assert.Equal(1, await _db.UserTeams.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsUnknownOrg()
    {
        var res = await _svc.CreateAsync("TEAM_X", "Đội X", Guid.NewGuid());
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.UserTeams.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsInactiveOrg()
    {
        var res = await _svc.CreateAsync("TEAM_X", "Đội X", _inactive.Id);
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.UserTeams.CountAsync());
    }

    [Fact]
    public async Task Create_DefaultsNameToCode()
    {
        await _svc.CreateAsync("TEAM_GLOBAL", null, null);
        var t = await _db.UserTeams.SingleAsync();
        Assert.Equal("TEAM_GLOBAL", t.Name);
        Assert.Null(t.OrgId);
    }

    [Fact]
    public async Task Update_ChangesNameAndOrg()
    {
        await _svc.CreateAsync("TEAM_HN1", "Đội 1", null);
        var t = await _db.UserTeams.SingleAsync();
        var res = await _svc.UpdateAsync(t.Id, "Đội mới", _north.Id);
        Assert.True(res.Ok);
        var updated = await _db.UserTeams.SingleAsync();
        Assert.Equal("Đội mới", updated.Name);
        Assert.Equal(_north.Id, updated.OrgId);
    }

    [Fact]
    public async Task Update_UnknownTeam_Fails()
    {
        var res = await _svc.UpdateAsync(Guid.NewGuid(), "x", null);
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Toggle_FlipsActive()
    {
        await _svc.CreateAsync("TEAM_HN1", "Đội 1", null);
        var t = await _db.UserTeams.SingleAsync();
        Assert.True(await _svc.ToggleAsync(t.Id));
        Assert.False((await _db.UserTeams.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Delete_RemovesTeam()
    {
        await _svc.CreateAsync("TEAM_HN1", "Đội 1", null);
        var t = await _db.UserTeams.SingleAsync();
        Assert.True(await _svc.DeleteAsync(t.Id));
        Assert.Equal(0, await _db.UserTeams.CountAsync());
    }

    [Fact]
    public async Task ByOrg_FiltersTeams()
    {
        await _svc.CreateAsync("TEAM_HN1", "Đội 1", _north.Id);
        await _svc.CreateAsync("TEAM_GLOBAL", "Đội toàn cục", null);
        var list = await _svc.ByOrgAsync(_north.Id);
        Assert.Single(list);
        Assert.Equal("TEAM_HN1", list[0].Code);
    }
}
