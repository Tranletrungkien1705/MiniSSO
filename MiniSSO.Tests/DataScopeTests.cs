using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test phạm vi dữ liệu (port từ iNOS.InBrand SysUserProvider "ViewAbility").
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class DataScopeTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly DataScopeService _svc;

    // Cây: 0 (gốc) → 10 (Miền Bắc) → 11 (Hà Nội); 0 → 20 (Miền Nam) → 21 (HCM) → 211 (Đông Đô)
    private readonly Org _root, _north, _hn, _south, _hcm, _dongDo;

    public DataScopeTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new DataScopeService(_db);

        _root = new Org { Code = "0", Name = "Tập đoàn" };
        _north = new Org { Code = "10", Name = "Miền Bắc", ParentId = _root.Id };
        _hn = new Org { Code = "11", Name = "Hà Nội", ParentId = _north.Id };
        _south = new Org { Code = "20", Name = "Miền Nam", ParentId = _root.Id };
        _hcm = new Org { Code = "21", Name = "HCM", ParentId = _south.Id };
        _dongDo = new Org { Code = "211", Name = "Đông Đô", ParentId = _hcm.Id };
        _db.Orgs.AddRange(_root, _north, _hn, _south, _hcm, _dongDo);
        _db.SaveChanges();
        new OrgService(_db).RebuildPathsAsync().GetAwaiter().GetResult();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private AppUser SeedUser(string email, Guid? orgId = null, bool sysAdmin = false)
    {
        var u = new AppUser { Email = email, FullName = email, PasswordHash = PasswordHasher.Hash("x"), OrgId = orgId, IsSysAdmin = sysAdmin };
        _db.Users.Add(u); _db.SaveChanges();
        return u;
    }

    [Fact]
    public async Task SysAdmin_SeesAllOrgs()
    {
        var u = SeedUser("admin@t.dev", sysAdmin: true);
        Assert.Null(await _svc.VisibleOrgIdsAsync(u.Id));   // null = không giới hạn
        Assert.Equal(6, (await _svc.VisibleOrgsAsync(u.Id)).Count);
    }

    [Fact]
    public async Task RootOrgUser_SeesAllOrgs()
    {
        var u = SeedUser("root@t.dev", orgId: _root.Id);
        Assert.Null(await _svc.VisibleOrgIdsAsync(u.Id));
        Assert.True(await _svc.IsRootOrgAsync(u.Id));
    }

    [Fact]
    public async Task BranchUser_SeesOwnSubtreeOnly()
    {
        var u = SeedUser("north@t.dev", orgId: _north.Id);
        var visible = await _svc.VisibleOrgsAsync(u.Id);
        var codes = visible.Select(o => o.Code).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "10", "11" }, codes);   // Miền Bắc + Hà Nội, KHÔNG có Miền Nam
    }

    [Fact]
    public async Task LeafUser_SeesOnlyItself()
    {
        var u = SeedUser("dealer@t.dev", orgId: _dongDo.Id);
        var visible = await _svc.VisibleOrgsAsync(u.Id);
        Assert.Single(visible);
        Assert.Equal("211", visible[0].Code);
    }

    [Fact]
    public async Task UserWithoutOrg_SeesNothing()
    {
        var u = SeedUser("nobody@t.dev");
        Assert.Empty(await _svc.VisibleOrgsAsync(u.Id));
    }

    [Fact]
    public async Task CanAccessOrg_RespectsSubtree()
    {
        var u = SeedUser("north@t.dev", orgId: _north.Id);
        Assert.True(await _svc.CanAccessOrgAsync(u.Id, _hn.Id));      // trong nhánh
        Assert.True(await _svc.CanAccessOrgAsync(u.Id, _north.Id));   // chính nó
        Assert.False(await _svc.CanAccessOrgAsync(u.Id, _hcm.Id));    // nhánh khác
    }

    [Fact]
    public async Task IsExactOrg_And_IsExactUser()
    {
        var u = SeedUser("dealer@t.dev", orgId: _dongDo.Id);
        var other = SeedUser("other@t.dev", orgId: _hcm.Id);
        Assert.True(await _svc.IsExactOrgAsync(u.Id, _dongDo.Id));
        Assert.False(await _svc.IsExactOrgAsync(u.Id, _hcm.Id));
        Assert.True(await _svc.IsExactUserAsync(u.Id, u.Id));
        Assert.False(await _svc.IsExactUserAsync(u.Id, other.Id));
    }

    [Fact]
    public async Task CheckAccess_CombinesFlags()
    {
        var u = SeedUser("north@t.dev", orgId: _north.Id);

        // Trong nhánh → cho phép.
        Assert.True((await _svc.CheckAccessAsync(u.Id, targetOrgId: _hn.Id, checkOrgAccess: true)).Allowed);

        // Ngoài nhánh → từ chối kèm lý do.
        var denied = await _svc.CheckAccessAsync(u.Id, targetOrgId: _hcm.Id, checkOrgAccess: true);
        Assert.False(denied.Allowed);
        Assert.NotNull(denied.Reason);

        // checkRoot: người dùng nhánh không phải gốc → từ chối.
        Assert.False((await _svc.CheckAccessAsync(u.Id, checkRoot: true)).Allowed);
    }
}
