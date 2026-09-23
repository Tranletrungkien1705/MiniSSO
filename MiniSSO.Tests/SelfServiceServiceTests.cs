using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test tự đổi mật khẩu (port từ iNOS.InBrand: AccountController.ChangePassword + SysUserManager.ResetPassword).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class SelfServiceServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly SelfServiceService _svc;

    private readonly AppUser _user;
    private readonly AppUser _inactive;

    public SelfServiceServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new SelfServiceService(_db);

        _user = new AppUser { Email = "u@minisso.dev", FullName = "Người dùng", PasswordHash = PasswordHasher.Hash("Old@123") };
        _inactive = new AppUser { Email = "off@minisso.dev", FullName = "Bị khoá", IsActive = false, PasswordHash = PasswordHasher.Hash("Old@123") };
        _db.Users.AddRange(_user, _inactive);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Change_Succeeds_WithCorrectCurrentAndMatchingConfirm()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Old@123", "New@456", "New@456");
        Assert.True(res.Ok);
        var updated = await _db.Users.SingleAsync(u => u.Id == _user.Id);
        Assert.True(PasswordHasher.Verify("New@456", updated.PasswordHash));
        Assert.False(PasswordHasher.Verify("Old@123", updated.PasswordHash));
    }

    [Fact]
    public async Task Change_RejectsWrongCurrentPassword()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Wrong@123", "New@456", "New@456");
        Assert.False(res.Ok);
        var updated = await _db.Users.SingleAsync(u => u.Id == _user.Id);
        Assert.True(PasswordHasher.Verify("Old@123", updated.PasswordHash));   // không đổi
    }

    [Fact]
    public async Task Change_RejectsMismatchedConfirm()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Old@123", "New@456", "Other@789");
        Assert.False(res.Ok);
        var updated = await _db.Users.SingleAsync(u => u.Id == _user.Id);
        Assert.True(PasswordHasher.Verify("Old@123", updated.PasswordHash));
    }

    [Fact]
    public async Task Change_RejectsEmptyNewPassword()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Old@123", "  ", "  ");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Change_RejectsEmptyConfirm()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Old@123", "New@456", "");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Change_RejectsTooShortNewPassword()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Old@123", "123", "123");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Change_RejectsInactiveUser()
    {
        var res = await _svc.ChangeOwnPasswordAsync(_inactive.Id, "Old@123", "New@456", "New@456");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Change_RejectsUnknownUser()
    {
        var res = await _svc.ChangeOwnPasswordAsync(Guid.NewGuid(), "Old@123", "New@456", "New@456");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Change_ResetsFailedLoginCount()
    {
        _user.FailedLoginCount = 3;
        await _db.SaveChangesAsync();
        var res = await _svc.ChangeOwnPasswordAsync(_user.Id, "Old@123", "New@456", "New@456");
        Assert.True(res.Ok);
        var updated = await _db.Users.SingleAsync(u => u.Id == _user.Id);
        Assert.Equal(0, updated.FailedLoginCount);
    }
}
