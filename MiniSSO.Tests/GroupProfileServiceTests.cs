using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test cập nhật hồ sơ NHÓM theo cột cho phép (port từ iNOS.InBrand: SysGroupManager.SysGroupUpdateX
/// — mẫu "Ft_Cols_Upd") + truy vấn nhóm của 1 người dùng (↔ SysGroupProvider.GetAllByUser).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class GroupProfileServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GroupProfileService _svc;

    private readonly Group _group;
    private readonly AppUser _user;
    private readonly Org _north, _inactive;

    public GroupProfileServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new GroupProfileService(_db);

        _north = new Org { Code = "10", Name = "Miền Bắc" };
        _inactive = new Org { Code = "99", Name = "Đơn vị khoá", IsActive = false };
        _group = new Group { Code = "SALES", Name = "Kinh doanh", Description = "Nhóm kinh doanh" };
        _user = new AppUser { Email = "u@minisso.dev", FullName = "Người dùng" };
        _db.Orgs.AddRange(_north, _inactive);
        _db.Groups.Add(_group);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private static GroupProfilePatch Patch(string? name = null, string? description = null,
        bool isActive = true, Guid? orgId = null)
        => new(name, description, isActive, orgId);

    [Fact]
    public async Task Update_OnlyWritesListedColumns()
    {
        // Chỉ cập nhật Description; Name/IsActive/OrgId phải giữ nguyên (↔ bUpd_* theo Ft_Cols_Upd).
        var res = await _svc.UpdateAsync(_group.Id, Patch(name: "Tên mới", description: "Mô tả mới", isActive: false, orgId: _north.Id),
            ["Description"]);
        Assert.True(res.Ok);
        var g = await _db.Groups.SingleAsync();
        Assert.Equal("Mô tả mới", g.Description);
        Assert.Equal("Kinh doanh", g.Name);   // không đổi
        Assert.True(g.IsActive);               // không đổi
        Assert.Null(g.OrgId);                  // không đổi
    }

    [Fact]
    public async Task Update_EmptyColumns_WritesAllAllowed()
    {
        var res = await _svc.UpdateAsync(_group.Id, Patch(name: "Tên mới", description: "Mô tả mới", isActive: false, orgId: _north.Id),
            null);
        Assert.True(res.Ok);
        var g = await _db.Groups.SingleAsync();
        Assert.Equal("Tên mới", g.Name);
        Assert.Equal("Mô tả mới", g.Description);
        Assert.False(g.IsActive);
        Assert.Equal(_north.Id, g.OrgId);
    }

    [Fact]
    public async Task Update_UnknownGroup_Fails()
    {
        var res = await _svc.UpdateAsync(Guid.NewGuid(), Patch(name: "x"), ["Name"]);
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Update_EmptyName_FallsBackToCode()
    {
        var res = await _svc.UpdateAsync(_group.Id, Patch(name: "   "), ["Name"]);
        Assert.True(res.Ok);
        Assert.Equal("SALES", (await _db.Groups.SingleAsync()).Name);
    }

    [Fact]
    public async Task Update_Org_Valid()
    {
        var res = await _svc.UpdateAsync(_group.Id, Patch(orgId: _north.Id), ["OrgId"]);
        Assert.True(res.Ok);
        Assert.Equal(_north.Id, (await _db.Groups.SingleAsync()).OrgId);
    }

    [Fact]
    public async Task Update_Org_Unknown_Rejected()
    {
        var res = await _svc.UpdateAsync(_group.Id, Patch(orgId: Guid.NewGuid()), ["OrgId"]);
        Assert.False(res.Ok);
        Assert.Null((await _db.Groups.SingleAsync()).OrgId);
    }

    [Fact]
    public async Task Update_Org_Inactive_Rejected()
    {
        var res = await _svc.UpdateAsync(_group.Id, Patch(orgId: _inactive.Id), ["OrgId"]);
        Assert.False(res.Ok);
        Assert.Null((await _db.Groups.SingleAsync()).OrgId);
    }

    [Fact]
    public async Task Update_Flag_OnlyWhenListed()
    {
        // IsActive không nằm trong danh sách → không đổi dù patch gửi giá trị khác.
        var res = await _svc.UpdateAsync(_group.Id, Patch(isActive: false), ["Name"]);
        Assert.True(res.Ok);
        Assert.True((await _db.Groups.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Update_Flag_WhenListed()
    {
        var res = await _svc.UpdateAsync(_group.Id, Patch(isActive: false), ["IsActive"]);
        Assert.True(res.Ok);
        Assert.False((await _db.Groups.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Update_UnknownColumn_Ignored()
    {
        // Cột ngoài danh sách trắng bị bỏ qua; không có cột hợp lệ nào → không ghi gì.
        var res = await _svc.UpdateAsync(_group.Id, Patch(name: "Tên mới"), ["Code", "Bogus"]);
        Assert.True(res.Ok);
        Assert.Equal("Kinh doanh", (await _db.Groups.SingleAsync()).Name);
    }

    [Fact]
    public async Task GroupsOfUser_ReturnsOnlyMemberships()
    {
        var other = new Group { Code = "DEALER", Name = "Đại lý" };
        _db.Groups.Add(other);
        await _db.SaveChangesAsync();
        _db.GroupMembers.Add(new GroupMember { GroupId = _group.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();

        var list = await _svc.GroupsOfUserAsync(_user.Id);
        Assert.Single(list);
        Assert.Equal("SALES", list[0].Code);
    }

    [Fact]
    public async Task GroupsOfUser_UnknownUser_Empty()
    {
        var list = await _svc.GroupsOfUserAsync(Guid.NewGuid());
        Assert.Empty(list);
    }

    [Fact]
    public async Task GroupsOfUser_NoMembership_Empty()
    {
        var list = await _svc.GroupsOfUserAsync(_user.Id);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GroupsOfUser_OrderedByCode()
    {
        var a = new Group { Code = "AAA", Name = "A" };
        var z = new Group { Code = "ZZZ", Name = "Z" };
        _db.Groups.AddRange(a, z);
        await _db.SaveChangesAsync();
        _db.GroupMembers.AddRange(
            new GroupMember { GroupId = z.Id, UserId = _user.Id },
            new GroupMember { GroupId = a.Id, UserId = _user.Id },
            new GroupMember { GroupId = _group.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();

        var list = await _svc.GroupsOfUserAsync(_user.Id);
        Assert.Equal(new[] { "AAA", "SALES", "ZZZ" }, list.Select(g => g.Code).ToArray());
    }
}
