using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test chính sách bảo mật tài khoản (port từ iNOS.InBrand SysUser: Enable / Lockout / LockoutDate).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class AccountSecurityTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly AccountSecurityService _svc;

    public AccountSecurityTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new AccountSecurityService(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private AppUser SeedUser(string email = "u@test.dev", string pw = "Secret@1", bool active = true)
    {
        var u = new AppUser { Email = email, FullName = "Test", PasswordHash = PasswordHasher.Hash(pw), IsActive = active };
        _db.Users.Add(u); _db.SaveChanges();
        return u;
    }

    [Fact]
    public async Task CorrectPassword_Succeeds_AndResetsCounter()
    {
        var u = SeedUser();
        var r = await _svc.AuthenticateAsync("u@test.dev", "Secret@1");
        Assert.True(r.Ok);
        Assert.Equal(0, u.FailedLoginCount);
        Assert.NotNull(u.LastLoginAt);
    }

    [Fact]
    public async Task WrongPassword_IncrementsCounter()
    {
        var u = SeedUser();
        await _svc.AuthenticateAsync("u@test.dev", "sai");
        Assert.Equal(1, u.FailedLoginCount);
        Assert.False(u.IsLockedOut);
    }

    [Fact]
    public async Task MaxFailedAttempts_LocksAccount_WithLockoutDate()
    {
        var u = SeedUser();
        for (var i = 0; i < AccountSecurityService.MaxFailedAttempts; i++)
            await _svc.AuthenticateAsync("u@test.dev", "sai");

        Assert.True(u.IsLockedOut);
        Assert.NotNull(u.LockoutDate);
        Assert.NotNull(u.LockoutUntil);
    }

    [Fact]
    public async Task LockedAccount_RejectsEvenCorrectPassword()
    {
        var u = SeedUser();
        for (var i = 0; i < AccountSecurityService.MaxFailedAttempts; i++)
            await _svc.AuthenticateAsync("u@test.dev", "sai");

        var r = await _svc.AuthenticateAsync("u@test.dev", "Secret@1");
        Assert.False(r.Ok);
        Assert.Contains("khoá", r.Reason);
    }

    [Fact]
    public async Task DisabledAccount_Rejected()
    {
        SeedUser(active: false);
        var r = await _svc.AuthenticateAsync("u@test.dev", "Secret@1");
        Assert.False(r.Ok);
        Assert.Contains("vô hiệu", r.Reason);
    }

    [Fact]
    public async Task AdminUnlock_ClearsLockout()
    {
        var u = SeedUser();
        for (var i = 0; i < AccountSecurityService.MaxFailedAttempts; i++)
            await _svc.AuthenticateAsync("u@test.dev", "sai");
        Assert.True(u.IsLockedOut);

        Assert.True(await _svc.UnlockAsync(u.Id));
        Assert.False(u.IsLockedOut);
        Assert.Equal(0, u.FailedLoginCount);

        var r = await _svc.AuthenticateAsync("u@test.dev", "Secret@1");
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task ResetPassword_ChangesHash_AndUnlocks()
    {
        var u = SeedUser();
        for (var i = 0; i < AccountSecurityService.MaxFailedAttempts; i++)
            await _svc.AuthenticateAsync("u@test.dev", "sai");

        Assert.True(await _svc.ResetPasswordAsync(u.Id, "NewPass@9"));
        Assert.False(u.IsLockedOut);
        Assert.True(PasswordHasher.Verify("NewPass@9", u.PasswordHash));
        Assert.False(PasswordHasher.Verify("Secret@1", u.PasswordHash));
    }

    [Fact]
    public async Task LoginAttempts_AreLogged()
    {
        SeedUser();
        await _svc.AuthenticateAsync("u@test.dev", "Secret@1");
        await _svc.AuthenticateAsync("u@test.dev", "sai");
        await _svc.AuthenticateAsync("khong-ton-tai@test.dev", "x");

        var logs = await _svc.RecentAttemptsAsync();
        Assert.Equal(3, logs.Count);
        Assert.Contains(logs, l => l.Success);
        Assert.Contains(logs, l => l.Reason == "bad_password");
        Assert.Contains(logs, l => l.Reason == "user_not_found");
    }
}