using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test cập nhật hồ sơ người dùng theo cột cho phép (port từ iNOS.InBrand: SysUserManager.SysUserUpdateX
/// — mẫu "Ft_Cols_Upd"). Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class UserProfileServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly UserProfileService _svc;

    private readonly AppUser _user;
    private readonly Org _north, _inactive;

    public UserProfileServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new UserProfileService(_db);

        _north = new Org { Code = "10", Name = "Miền Bắc" };
        _inactive = new Org { Code = "99", Name = "Đơn vị khoá", IsActive = false };
        _user = new AppUser { Email = "u@minisso.dev", FullName = "Người dùng", Roles = "Sales", Tenant = "Demo" };
        _db.Orgs.AddRange(_north, _inactive);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private static UserProfilePatch Patch(string? email = null, string? fullName = null, string? roles = null,
        string? tenant = null, bool isActive = true, bool isSysAdmin = false, Guid? orgId = null)
        => new(email, fullName, roles, tenant, isActive, isSysAdmin, orgId);

    [Fact]
    public async Task Update_OnlyWritesListedColumns()
    {
        // Chỉ cập nhật FullName; Email/Roles/Tenant phải giữ nguyên (↔ bUpd_* theo Ft_Cols_Upd).
        var res = await _svc.UpdateAsync(_user.Id, Patch(email: "changed@x.dev", fullName: "Tên mới", roles: "Admin", tenant: "X"),
            ["FullName"]);
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync();
        Assert.Equal("Tên mới", u.FullName);
        Assert.Equal("u@minisso.dev", u.Email);   // không đổi
        Assert.Equal("Sales", u.Roles);            // không đổi
        Assert.Equal("Demo", u.Tenant);            // không đổi
    }

    [Fact]
    public async Task Update_EmptyColumns_WritesAllAllowed()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(email: "new@x.dev", fullName: "Tên mới", roles: "Admin", tenant: "X"),
            null);
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync();
        Assert.Equal("new@x.dev", u.Email);
        Assert.Equal("Tên mới", u.FullName);
        Assert.Equal("Admin", u.Roles);
        Assert.Equal("X", u.Tenant);
    }

    [Fact]
    public async Task Update_UnknownUser_Fails()
    {
        var res = await _svc.UpdateAsync(Guid.NewGuid(), Patch(fullName: "x"), ["FullName"]);
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Update_EmailNormalizedToLower()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(email: "  Mixed@Case.DEV "), ["Email"]);
        Assert.True(res.Ok);
        Assert.Equal("mixed@case.dev", (await _db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task Update_EmptyEmail_Rejected()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(email: "   "), ["Email"]);
        Assert.False(res.Ok);
        Assert.Equal("u@minisso.dev", (await _db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task Update_DuplicateEmail_Rejected()
    {
        _db.Users.Add(new AppUser { Email = "other@minisso.dev", FullName = "Khác" });
        await _db.SaveChangesAsync();
        var res = await _svc.UpdateAsync(_user.Id, Patch(email: "other@minisso.dev"), ["Email"]);
        Assert.False(res.Ok);
        Assert.Equal("u@minisso.dev", (await _db.Users.SingleAsync(u => u.Id == _user.Id)).Email);
    }

    [Fact]
    public async Task Update_SameEmail_Allowed()
    {
        // Giữ nguyên email của chính mình không bị coi là trùng.
        var res = await _svc.UpdateAsync(_user.Id, Patch(email: "u@minisso.dev", fullName: "Tên mới"), ["Email", "FullName"]);
        Assert.True(res.Ok);
        Assert.Equal("Tên mới", (await _db.Users.SingleAsync()).FullName);
    }

    [Fact]
    public async Task Update_Org_Valid()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(orgId: _north.Id), ["OrgId"]);
        Assert.True(res.Ok);
        Assert.Equal(_north.Id, (await _db.Users.SingleAsync()).OrgId);
    }

    [Fact]
    public async Task Update_Org_Unknown_Rejected()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(orgId: Guid.NewGuid()), ["OrgId"]);
        Assert.False(res.Ok);
        Assert.Null((await _db.Users.SingleAsync()).OrgId);
    }

    [Fact]
    public async Task Update_Org_Inactive_Rejected()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(orgId: _inactive.Id), ["OrgId"]);
        Assert.False(res.Ok);
        Assert.Null((await _db.Users.SingleAsync()).OrgId);
    }

    [Fact]
    public async Task Update_Flags_OnlyWhenListed()
    {
        // IsActive/IsSysAdmin không nằm trong danh sách → không đổi dù patch gửi giá trị khác.
        var res = await _svc.UpdateAsync(_user.Id, Patch(isActive: false, isSysAdmin: true), ["FullName"]);
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync();
        Assert.True(u.IsActive);
        Assert.False(u.IsSysAdmin);
    }

    [Fact]
    public async Task Update_Flags_WhenListed()
    {
        var res = await _svc.UpdateAsync(_user.Id, Patch(isActive: false, isSysAdmin: true), ["IsActive", "IsSysAdmin"]);
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync();
        Assert.False(u.IsActive);
        Assert.True(u.IsSysAdmin);
    }

    [Fact]
    public async Task Update_UnknownColumn_Ignored()
    {
        // Cột ngoài danh sách trắng bị bỏ qua; không có cột hợp lệ nào → không ghi gì.
        var res = await _svc.UpdateAsync(_user.Id, Patch(fullName: "Tên mới"), ["PasswordHash", "Bogus"]);
        Assert.True(res.Ok);
        Assert.Equal("Người dùng", (await _db.Users.SingleAsync()).FullName);
    }
}