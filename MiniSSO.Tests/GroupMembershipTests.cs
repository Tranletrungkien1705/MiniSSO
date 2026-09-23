using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test truy vấn thành viên nhóm (port từ iNOS.InBrand: SysUserProvider.GetAllUserByGroupCode /
/// GetAllUserNotInGroup + SysUserInGroupProvider.RemoveByUser).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class GroupMembershipTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GroupService _svc;

    private readonly AppUser _alice, _bob, _carol;
    private readonly Group _sales, _dealer;

    public GroupMembershipTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new GroupService(_db);

        _alice = new AppUser { Email = "alice@x.dev", FullName = "Alice" };
        _bob = new AppUser { Email = "bob@x.dev", FullName = "Bob" };
        _carol = new AppUser { Email = "carol@x.dev", FullName = "Carol" };
        _db.Users.AddRange(_alice, _bob, _carol);

        _sales = new Group { Code = "SALES", Name = "Kinh doanh" };
        _dealer = new Group { Code = "DEALER", Name = "Đại lý" };
        _db.Groups.AddRange(_sales, _dealer);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task MembersOfGroup_ReturnsOnlyMembers()
    {
        await _svc.SetMembersAsync(_sales.Id, new[] { _alice.Id, _bob.Id });
        var members = await _svc.MembersOfGroupAsync(_sales.Id);
        Assert.Equal(2, members.Count);
        Assert.Contains(members, u => u.Email == "alice@x.dev");
        Assert.Contains(members, u => u.Email == "bob@x.dev");
        Assert.DoesNotContain(members, u => u.Email == "carol@x.dev");
    }

    [Fact]
    public async Task MembersOfGroup_UnknownGroup_ReturnsEmpty()
    {
        var members = await _svc.MembersOfGroupAsync(Guid.NewGuid());
        Assert.Empty(members);
    }

    [Fact]
    public async Task MembersOfGroup_OrderedByEmail()
    {
        await _svc.SetMembersAsync(_sales.Id, new[] { _bob.Id, _alice.Id });
        var members = await _svc.MembersOfGroupAsync(_sales.Id);
        Assert.Equal(new[] { "alice@x.dev", "bob@x.dev" }, members.Select(u => u.Email).ToArray());
    }

    [Fact]
    public async Task UsersNotInAnyGroup_ReturnsUnassigned()
    {
        await _svc.SetMembersAsync(_sales.Id, new[] { _alice.Id });
        var notIn = await _svc.UsersNotInAnyGroupAsync();
        Assert.Equal(2, notIn.Count);
        Assert.DoesNotContain(notIn, u => u.Email == "alice@x.dev");
        Assert.Contains(notIn, u => u.Email == "bob@x.dev");
        Assert.Contains(notIn, u => u.Email == "carol@x.dev");
    }

    [Fact]
    public async Task UsersNotInAnyGroup_ExcludesUsersInAnyGroup()
    {
        await _svc.SetMembersAsync(_sales.Id, new[] { _alice.Id });
        await _svc.SetMembersAsync(_dealer.Id, new[] { _bob.Id });
        var notIn = await _svc.UsersNotInAnyGroupAsync();
        Assert.Single(notIn);
        Assert.Equal("carol@x.dev", notIn[0].Email);
    }

    [Fact]
    public async Task UsersNotInAnyGroup_AllAssigned_ReturnsEmpty()
    {
        await _svc.SetMembersAsync(_sales.Id, new[] { _alice.Id, _bob.Id, _carol.Id });
        var notIn = await _svc.UsersNotInAnyGroupAsync();
        Assert.Empty(notIn);
    }

    [Fact]
    public async Task RemoveUserFromAllGroups_RemovesEveryLink()
    {
        await _svc.SetMembersAsync(_sales.Id, new[] { _alice.Id, _bob.Id });
        await _svc.SetMembersAsync(_dealer.Id, new[] { _alice.Id });

        var removed = await _svc.RemoveUserFromAllGroupsAsync(_alice.Id);

        Assert.Equal(2, removed);
        Assert.DoesNotContain(await _svc.MembersOfGroupAsync(_sales.Id), u => u.Id == _alice.Id);
        Assert.DoesNotContain(await _svc.MembersOfGroupAsync(_dealer.Id), u => u.Id == _alice.Id);
        // Bob vẫn còn trong SALES.
        Assert.Contains(await _svc.MembersOfGroupAsync(_sales.Id), u => u.Id == _bob.Id);
    }

    [Fact]
    public async Task RemoveUserFromAllGroups_NoLinks_ReturnsZero()
    {
        var removed = await _svc.RemoveUserFromAllGroupsAsync(_carol.Id);
        Assert.Equal(0, removed);
    }
}
