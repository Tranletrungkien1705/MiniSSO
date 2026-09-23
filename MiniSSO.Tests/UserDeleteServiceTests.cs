using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test xoá người dùng kèm dọn liên kết nhóm (port từ iNOS.InBrand: SysUserManager.Remove →
/// SysUserDeleteX + SysUserInGroupProvider.RemoveByUser). Dùng SQLite in-memory (giữ kết nối mở
/// để DB không bị xoá giữa các lệnh).
/// </summary>
public class UserDeleteServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly UserDeleteService _svc;

    private readonly AppUser _alice, _bob;
    private readonly Group _sales, _dealer;

    public UserDeleteServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new UserDeleteService(_db);

        _alice = new AppUser { Email = "alice@x.dev", FullName = "Alice" };
        _bob = new AppUser { Email = "bob@x.dev", FullName = "Bob" };
        _db.Users.AddRange(_alice, _bob);

        _sales = new Group { Code = "SALES", Name = "Kinh doanh" };
        _dealer = new Group { Code = "DEALER", Name = "Đại lý" };
        _db.Groups.AddRange(_sales, _dealer);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Delete_RemovesUser()
    {
        var res = await _svc.DeleteAsync(_alice.Id);
        Assert.True(res.Ok);
        Assert.False(await _db.Users.AnyAsync(u => u.Id == _alice.Id));
        // Bob vẫn còn.
        Assert.True(await _db.Users.AnyAsync(u => u.Id == _bob.Id));
    }

    [Fact]
    public async Task Delete_RemovesGroupLinks()
    {
        _db.GroupMembers.AddRange(
            new GroupMember { GroupId = _sales.Id, UserId = _alice.Id },
            new GroupMember { GroupId = _dealer.Id, UserId = _alice.Id },
            new GroupMember { GroupId = _sales.Id, UserId = _bob.Id });
        await _db.SaveChangesAsync();

        var res = await _svc.DeleteAsync(_alice.Id);

        Assert.True(res.Ok);
        Assert.Equal(2, res.RemovedGroupLinks);
        Assert.False(await _db.GroupMembers.AnyAsync(m => m.UserId == _alice.Id));
        // Liên kết của Bob không bị ảnh hưởng.
        Assert.True(await _db.GroupMembers.AnyAsync(m => m.UserId == _bob.Id));
    }

    [Fact]
    public async Task Delete_NoGroupLinks_ReturnsZero()
    {
        var res = await _svc.DeleteAsync(_alice.Id);
        Assert.True(res.Ok);
        Assert.Equal(0, res.RemovedGroupLinks);
    }

    [Fact]
    public async Task Delete_UnknownUser_Fails()
    {
        var res = await _svc.DeleteAsync(Guid.NewGuid());
        Assert.False(res.Ok);
        Assert.NotNull(res.Error);
        // Không xoá nhầm ai.
        Assert.Equal(2, await _db.Users.CountAsync());
    }
}
