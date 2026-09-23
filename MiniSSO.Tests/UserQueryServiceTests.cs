using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test truy vấn người dùng theo đơn vị / trạng thái (port từ iNOS.InBrand:
/// SysUserService.GetByDLCode + GetAllByEnable).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class UserQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly UserQueryService _svc;

    private readonly Org _north, _south;
    private readonly AppUser _uNorth, _uSouth, _uInactive, _uNoOrg;
    private readonly Group _grp;

    public UserQueryServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new UserQueryService(_db);

        _north = new Org { Code = "10", Name = "Miền Bắc" };
        _south = new Org { Code = "20", Name = "Miền Nam" };
        _db.Orgs.AddRange(_north, _south);

        _uNorth = new AppUser { Email = "north@x.dev", FullName = "Bắc", OrgId = _north.Id };
        _uSouth = new AppUser { Email = "south@x.dev", FullName = "Nam", OrgId = _south.Id };
        _uInactive = new AppUser { Email = "off@x.dev", FullName = "Nghỉ", OrgId = _north.Id, IsActive = false };
        _uNoOrg = new AppUser { Email = "none@x.dev", FullName = "Không đơn vị" };
        _db.Users.AddRange(_uNorth, _uSouth, _uInactive, _uNoOrg);

        _grp = new Group { Code = "SALES", Name = "Kinh doanh" };
        _db.Groups.Add(_grp);
        _db.SaveChanges();

        _db.GroupMembers.Add(new GroupMember { GroupId = _grp.Id, UserId = _uNorth.Id });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task ByOrg_ReturnsOnlyUsersOfOrg()
    {
        var rows = await _svc.ByOrgAsync(_north.Id);
        Assert.Equal(2, rows.Count);   // north + inactive (cùng đơn vị)
        Assert.All(rows, r => Assert.Equal(_north.Id, r.User.OrgId));
    }

    [Fact]
    public async Task ByOrg_UnknownOrg_ReturnsEmpty()
    {
        var rows = await _svc.ByOrgAsync(Guid.NewGuid());
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ByOrg_OrdersByEmail()
    {
        var rows = await _svc.ByOrgAsync(_north.Id);
        Assert.Equal("north@x.dev", rows[0].User.Email);
        Assert.Equal("off@x.dev", rows[1].User.Email);
    }

    [Fact]
    public async Task ByOrg_AttachesGroups()
    {
        var rows = await _svc.ByOrgAsync(_north.Id);
        var north = rows.Single(r => r.User.Email == "north@x.dev");
        Assert.Single(north.Groups);
        Assert.Equal("SALES", north.Groups[0].Code);

        var off = rows.Single(r => r.User.Email == "off@x.dev");
        Assert.Empty(off.Groups);
    }

    [Fact]
    public async Task ByActive_DefaultTrue_ReturnsOnlyActive()
    {
        var users = await _svc.ByActiveAsync();
        Assert.Equal(3, users.Count);
        Assert.DoesNotContain(users, u => u.Email == "off@x.dev");
    }

    [Fact]
    public async Task ByActive_False_ReturnsOnlyInactive()
    {
        var users = await _svc.ByActiveAsync(false);
        Assert.Single(users);
        Assert.Equal("off@x.dev", users[0].Email);
    }

    [Fact]
    public async Task ByActive_Null_ReturnsAll()
    {
        var users = await _svc.ByActiveAsync(null);
        Assert.Equal(4, users.Count);
    }

    [Fact]
    public async Task ByActive_OrdersByEmail()
    {
        var users = await _svc.ByActiveAsync(null);
        Assert.Equal(new[] { "none@x.dev", "north@x.dev", "off@x.dev", "south@x.dev" }, users.Select(u => u.Email).ToArray());
    }
}
