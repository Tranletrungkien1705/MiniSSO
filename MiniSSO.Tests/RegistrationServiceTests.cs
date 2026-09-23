using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test tự đăng ký tham gia hệ thống (port từ iNOS.InBrand: AccountController.Join +
/// AccountService.Register + SysUserManager.Register → SysUserAddX_New20190917).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class RegistrationServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly RegistrationService _svc;

    private readonly Org _activeOrg;
    private readonly Org _lockedOrg;

    public RegistrationServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new RegistrationService(_db, new CheckDbService(_db));

        _activeOrg = new Org { Code = "10", Name = "Miền Bắc", IsActive = true };
        _lockedOrg = new Org { Code = "99", Name = "Đơn vị khoá", IsActive = false };
        _db.Orgs.AddRange(_activeOrg, _lockedOrg);
        _db.Users.Add(new AppUser { Email = "existing@minisso.dev", FullName = "Đã có" });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Register_CreatesActiveUser_WithHashedPassword()
    {
        var res = await _svc.RegisterAsync("new@minisso.dev", "Secret@123", "Người mới");
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync(x => x.Email == "new@minisso.dev");
        Assert.True(u.IsActive);                 // ↔ Enable = true
        Assert.False(u.IsLockedOut);             // ↔ Lockout = false
        Assert.Equal("vi", u.Language);          // ↔ Language = "vi"
        Assert.NotNull(u.LockoutDate);           // ↔ LockoutDate = now
        Assert.True(PasswordHasher.Verify("Secret@123", u.PasswordHash));
        Assert.False(PasswordHasher.Verify("Secret@123", "Secret@123"));   // đã băm, không lưu thô
    }

    [Fact]
    public async Task Register_NormalisesEmailToLowercase()
    {
        var res = await _svc.RegisterAsync("  Mixed@MiniSSO.DEV ", "Secret@123", "X");
        Assert.True(res.Ok);
        Assert.True(await _db.Users.AnyAsync(u => u.Email == "mixed@minisso.dev"));
    }

    [Fact]
    public async Task Register_TrimsFullNameAndPhone()
    {
        var res = await _svc.RegisterAsync("trim@minisso.dev", "Secret@123", "  Tên  ", "  0900  ");
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync(x => x.Email == "trim@minisso.dev");
        Assert.Equal("Tên", u.FullName);
        Assert.Equal("0900", u.PhoneNo);
    }

    [Fact]
    public async Task Register_RejectsEmptyEmail()
    {
        var res = await _svc.RegisterAsync("  ", "Secret@123", "X");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Register_RejectsExistingEmail()
    {
        // ↔ SysUserCheckDB với strFlagExistToCheck = "0": người dùng PHẢI CHƯA tồn tại.
        var res = await _svc.RegisterAsync("existing@minisso.dev", "Secret@123", "X");
        Assert.False(res.Ok);
        Assert.Equal(1, await _db.Users.CountAsync(u => u.Email == "existing@minisso.dev"));
    }

    [Fact]
    public async Task Register_RejectsEmptyPassword()
    {
        var res = await _svc.RegisterAsync("nopass@minisso.dev", "  ", "X");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Register_RejectsTooShortPassword()
    {
        var res = await _svc.RegisterAsync("short@minisso.dev", "123", "X");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Register_AcceptsOrg_WhenActive()
    {
        var res = await _svc.RegisterAsync("withorg@minisso.dev", "Secret@123", "X", null, _activeOrg.Id);
        Assert.True(res.Ok);
        var u = await _db.Users.SingleAsync(x => x.Email == "withorg@minisso.dev");
        Assert.Equal(_activeOrg.Id, u.OrgId);
    }

    [Fact]
    public async Task Register_RejectsUnknownOrg()
    {
        var res = await _svc.RegisterAsync("badorg@minisso.dev", "Secret@123", "X", null, Guid.NewGuid());
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Register_RejectsLockedOrg()
    {
        // ↔ MstDealerCheckDB với strFlagActiveListToCheck = "1": đơn vị phải đang hoạt động.
        var res = await _svc.RegisterAsync("lockedorg@minisso.dev", "Secret@123", "X", null, _lockedOrg.Id);
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Register_ReturnsNewUserId()
    {
        var res = await _svc.RegisterAsync("id@minisso.dev", "Secret@123", "X");
        Assert.True(res.Ok);
        Assert.NotNull(res.UserId);
        Assert.True(await _db.Users.AnyAsync(u => u.Id == res.UserId));
    }
}
