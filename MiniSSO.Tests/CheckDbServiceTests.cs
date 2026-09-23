using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test mẫu kiểm tra trước khi lưu (port từ iNOS.InBrand: SysUserCheckDB / SysGroupCheckDB /
/// MstModuleCheckDB / MstDealerCheckDB). Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá).
/// </summary>
public class CheckDbServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly CheckDbService _svc;

    public CheckDbServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new CheckDbService(_db);

        _db.Users.AddRange(
            new AppUser { Email = "active@minisso.dev", IsActive = true },
            new AppUser { Email = "locked@minisso.dev", IsActive = false });
        _db.Groups.AddRange(
            new Group { Code = "SYSADMIN", IsActive = true },
            new Group { Code = "OLD", IsActive = false });
        _db.Modules.Add(new Module { Code = "sso", IsActive = true });
        _db.Orgs.Add(new Org { Code = "10", Name = "Miền Bắc", IsActive = true });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    // ── strFlagExistToCheck = "1" (phải tồn tại) ──

    [Fact]
    public async Task Exist_Active_WhenPresent_Passes()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.User, "active@minisso.dev", CheckDbService.FlagActive);
        Assert.True(r.Ok);
        Assert.True(r.Exists);
        Assert.Equal("1", r.Status);
    }

    [Fact]
    public async Task Exist_Active_WhenMissing_Fails()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.User, "nobody@minisso.dev", CheckDbService.FlagActive);
        Assert.False(r.Ok);
        Assert.False(r.Exists);
        Assert.Contains("không tồn tại", r.Error);
    }

    // ── strFlagExistToCheck = "0" (phải chưa tồn tại) ──

    [Fact]
    public async Task Exist_Inactive_WhenMissing_Passes()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.Group, "NEWGROUP", CheckDbService.FlagInactive);
        Assert.True(r.Ok);
        Assert.False(r.Exists);
    }

    [Fact]
    public async Task Exist_Inactive_WhenPresent_Fails()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.Group, "SYSADMIN", CheckDbService.FlagInactive);
        Assert.False(r.Ok);
        Assert.True(r.Exists);
        Assert.Contains("đã tồn tại", r.Error);
    }

    // ── strFlagActiveListToCheck (trạng thái phải nằm trong danh sách cho phép) ──

    [Fact]
    public async Task ActiveList_AllowsActiveRecord()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.Module, "sso", "", CheckDbService.FlagActive);
        Assert.True(r.Ok);
        Assert.Equal("1", r.Status);
    }

    [Fact]
    public async Task ActiveList_RejectsInactiveRecord()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.Group, "OLD", "", CheckDbService.FlagActive);
        Assert.False(r.Ok);
        Assert.Equal("0", r.Status);
        Assert.Contains("không thuộc danh sách cho phép", r.Error);
    }

    [Fact]
    public async Task ActiveList_AllowsInactiveWhenRequested()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.Group, "OLD", "", CheckDbService.FlagInactive);
        Assert.True(r.Ok);
        Assert.Equal("0", r.Status);
    }

    // ── Kết hợp cả hai điều kiện + các loại thực thể khác ──

    [Fact]
    public async Task Combined_ExistAndActive_Passes()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.Org, "10", CheckDbService.FlagActive, CheckDbService.FlagActive);
        Assert.True(r.Ok);
        Assert.True(r.Exists);
        Assert.Equal("1", r.Status);
    }

    [Fact]
    public async Task Combined_ExistAndActive_FailsOnStatus()
    {
        // Tồn tại nhưng bị khoá → vi phạm danh sách cờ hoạt động.
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.User, "locked@minisso.dev", CheckDbService.FlagActive, CheckDbService.FlagActive);
        Assert.False(r.Ok);
        Assert.True(r.Exists);
        Assert.Equal("0", r.Status);
    }

    [Fact]
    public async Task EmptyCode_IsTreatedAsMissing()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.User, "  ", CheckDbService.FlagActive);
        Assert.False(r.Ok);
        Assert.False(r.Exists);
    }

    [Fact]
    public async Task NoChecks_AlwaysPasses()
    {
        var r = await _svc.CheckAsync(CheckDbService.EntityKind.User, "whatever@x.dev");
        Assert.True(r.Ok);
        Assert.False(r.Exists);
    }
}
