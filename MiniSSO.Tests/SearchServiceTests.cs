using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test tìm kiếm có phân trang + lọc phạm vi dữ liệu (port từ iNOS.InBrand:
/// SysUserManager.Search + SysGroupManager.Search). Dùng SQLite in-memory (giữ kết nối mở).
/// </summary>
public class SearchServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly SearchService _svc;

    private readonly AppUser _admin, _sales, _dealer, _northUser, _southUser;
    private readonly Org _root, _north, _south, _dongDo;
    private readonly Group _sysadmin, _salesGrp, _globalGrp;

    public SearchServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new SearchService(_db, new DataScopeService(_db));

        // Cây tổ chức: 0 → 10 (Miền Bắc) / 20 (Miền Nam) → 211 (Đại lý Đông Đô).
        _root = new Org { Code = "0", Name = "Tập đoàn" };
        _north = new Org { Code = "10", Name = "Miền Bắc", ParentId = _root.Id };
        _south = new Org { Code = "20", Name = "Miền Nam", ParentId = _root.Id };
        _dongDo = new Org { Code = "211", Name = "Đại lý Đông Đô", ParentId = _south.Id };
        _db.Orgs.AddRange(_root, _north, _south, _dongDo);
        _db.SaveChanges();
        new OrgService(_db).RebuildPathsAsync().GetAwaiter().GetResult();

        _admin = new AppUser { Email = "admin@x.dev", FullName = "Quản trị", IsSysAdmin = true };
        _sales = new AppUser { Email = "sales@x.dev", FullName = "Kinh doanh", OrgId = _north.Id };
        _dealer = new AppUser { Email = "dealer@x.dev", FullName = "Đại lý", OrgId = _dongDo.Id };
        _northUser = new AppUser { Email = "north@x.dev", FullName = "Nhân viên Bắc", OrgId = _north.Id };
        _southUser = new AppUser { Email = "south@x.dev", FullName = "Nhân viên Nam", OrgId = _south.Id };
        _db.Users.AddRange(_admin, _sales, _dealer, _northUser, _southUser);

        _sysadmin = new Group { Code = "SYSADMIN", Name = "Quản trị" };
        _salesGrp = new Group { Code = "SALES", Name = "Kinh doanh", OrgId = _north.Id };
        _globalGrp = new Group { Code = "GLOBAL", Name = "Toàn cục" };
        _db.Groups.AddRange(_sysadmin, _salesGrp, _globalGrp);
        _db.SaveChanges();

        // admin thuộc SYSADMIN; sales thuộc SALES.
        _db.GroupMembers.AddRange(
            new GroupMember { UserId = _admin.Id, GroupId = _sysadmin.Id },
            new GroupMember { UserId = _sales.Id, GroupId = _salesGrp.Id });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    // ── Phân trang ──

    [Fact]
    public async Task SearchUsers_SysAdmin_SeesAll()
    {
        var res = await _svc.SearchUsersAsync(_admin.Id);
        Assert.Equal(5, res.Total);
        Assert.Equal(5, res.Items.Count);
    }

    [Fact]
    public async Task SearchUsers_Paging_ReturnsRequestedPage()
    {
        var page0 = await _svc.SearchUsersAsync(_admin.Id, pageIndex: 0, pageSize: 2);
        var page1 = await _svc.SearchUsersAsync(_admin.Id, pageIndex: 1, pageSize: 2);
        Assert.Equal(5, page0.Total);
        Assert.Equal(2, page0.Items.Count);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(3, page0.PageCount);
        // Không trùng giữa 2 trang.
        Assert.Empty(page0.Items.Select(r => r.User.Id).Intersect(page1.Items.Select(r => r.User.Id)));
    }

    [Fact]
    public async Task SearchUsers_OrderedByEmail()
    {
        var res = await _svc.SearchUsersAsync(_admin.Id);
        var emails = res.Items.Select(r => r.User.Email).ToArray();
        Assert.Equal(emails.OrderBy(e => e).ToArray(), emails);
    }

    // ── Lọc phạm vi dữ liệu ──

    [Fact]
    public async Task SearchUsers_ScopedCaller_SeesOnlyOwnSubtree()
    {
        // sales ở Miền Bắc → chỉ thấy chính mình + northUser (cùng nhánh Miền Bắc).
        var res = await _svc.SearchUsersAsync(_sales.Id);
        Assert.Equal(2, res.Total);
        Assert.Contains(res.Items, r => r.User.Email == "sales@x.dev");
        Assert.Contains(res.Items, r => r.User.Email == "north@x.dev");
        Assert.DoesNotContain(res.Items, r => r.User.Email == "south@x.dev");
        Assert.DoesNotContain(res.Items, r => r.User.Email == "dealer@x.dev");
    }

    [Fact]
    public async Task SearchUsers_LeafCaller_SeesOnlySelf()
    {
        // dealer ở Đại lý Đông Đô (nút lá) → chỉ thấy chính mình.
        var res = await _svc.SearchUsersAsync(_dealer.Id);
        Assert.Single(res.Items);
        Assert.Equal("dealer@x.dev", res.Items[0].User.Email);
    }

    [Fact]
    public async Task SearchUsers_RootCaller_SeesAll()
    {
        var rootUser = new AppUser { Email = "root@x.dev", OrgId = _root.Id };
        _db.Users.Add(rootUser);
        await _db.SaveChangesAsync();

        var res = await _svc.SearchUsersAsync(rootUser.Id);
        Assert.Equal(6, res.Total);
    }

    [Fact]
    public async Task SearchUsers_NoOrgCaller_SeesNothing()
    {
        var orphan = new AppUser { Email = "orphan@x.dev" };
        _db.Users.Add(orphan);
        await _db.SaveChangesAsync();

        var res = await _svc.SearchUsersAsync(orphan.Id);
        Assert.Equal(0, res.Total);
    }

    // ── Từ khoá + trạng thái ──

    [Fact]
    public async Task SearchUsers_Keyword_MatchesEmailOrName()
    {
        var byEmail = await _svc.SearchUsersAsync(_admin.Id, keyword: "north");
        Assert.Single(byEmail.Items);
        Assert.Equal("north@x.dev", byEmail.Items[0].User.Email);

        var byName = await _svc.SearchUsersAsync(_admin.Id, keyword: "Kinh doanh");
        Assert.Single(byName.Items);
        Assert.Equal("sales@x.dev", byName.Items[0].User.Email);
    }

    [Fact]
    public async Task SearchUsers_Keyword_CaseInsensitive()
    {
        var res = await _svc.SearchUsersAsync(_admin.Id, keyword: "NORTH");
        Assert.Single(res.Items);
    }

    [Fact]
    public async Task SearchUsers_ActiveFilter()
    {
        _sales.IsActive = false;
        await _db.SaveChangesAsync();

        var active = await _svc.SearchUsersAsync(_admin.Id, isActive: true);
        Assert.DoesNotContain(active.Items, r => r.User.Email == "sales@x.dev");

        var inactive = await _svc.SearchUsersAsync(_admin.Id, isActive: false);
        Assert.Single(inactive.Items);
        Assert.Equal("sales@x.dev", inactive.Items[0].User.Email);
    }

    // ── Gắn nhóm ──

    [Fact]
    public async Task SearchUsers_AttachesGroups()
    {
        var res = await _svc.SearchUsersAsync(_admin.Id, keyword: "sales");
        var row = Assert.Single(res.Items);
        Assert.Single(row.Groups);
        Assert.Equal("SALES", row.Groups[0].Code);
    }

    [Fact]
    public async Task SearchUsers_NoGroups_EmptyList()
    {
        var res = await _svc.SearchUsersAsync(_admin.Id, keyword: "north");
        var row = Assert.Single(res.Items);
        Assert.Empty(row.Groups);
    }

    // ── Tìm nhóm ──

    [Fact]
    public async Task SearchGroups_SysAdmin_SeesAll()
    {
        var res = await _svc.SearchGroupsAsync(_admin.Id);
        Assert.Equal(3, res.Total);
    }

    [Fact]
    public async Task SearchGroups_ScopedCaller_SeesOwnOrgAndGlobal()
    {
        // sales ở Miền Bắc → thấy SALES (cùng đơn vị) + GLOBAL (toàn cục), không thấy SYSADMIN (không đơn vị? SYSADMIN OrgId null → toàn cục nên vẫn thấy).
        var res = await _svc.SearchGroupsAsync(_sales.Id);
        Assert.Contains(res.Items, g => g.Code == "SALES");
        Assert.Contains(res.Items, g => g.Code == "GLOBAL");
        // SYSADMIN cũng OrgId = null (toàn cục) nên được thấy.
        Assert.Contains(res.Items, g => g.Code == "SYSADMIN");
    }

    [Fact]
    public async Task SearchGroups_ScopedCaller_ExcludesOtherOrgGroup()
    {
        var southGrp = new Group { Code = "SOUTH", Name = "Nhóm Nam", OrgId = _south.Id };
        _db.Groups.Add(southGrp);
        await _db.SaveChangesAsync();

        var res = await _svc.SearchGroupsAsync(_sales.Id);
        Assert.DoesNotContain(res.Items, g => g.Code == "SOUTH");
    }

    [Fact]
    public async Task SearchGroups_Keyword_MatchesCodeOrName()
    {
        var byCode = await _svc.SearchGroupsAsync(_admin.Id, keyword: "sales");
        Assert.Single(byCode.Items);
        Assert.Equal("SALES", byCode.Items[0].Code);

        var byName = await _svc.SearchGroupsAsync(_admin.Id, keyword: "Toàn cục");
        Assert.Single(byName.Items);
        Assert.Equal("GLOBAL", byName.Items[0].Code);
    }

    [Fact]
    public async Task SearchGroups_Paging()
    {
        var res = await _svc.SearchGroupsAsync(_admin.Id, pageIndex: 0, pageSize: 2);
        Assert.Equal(3, res.Total);
        Assert.Equal(2, res.Items.Count);
        Assert.Equal(2, res.PageCount);
    }

    [Fact]
    public async Task SearchUsers_PageSizeClamped()
    {
        var res = await _svc.SearchUsersAsync(_admin.Id, pageSize: 0);
        Assert.Equal(SearchService.DefaultPageSize, res.PageSize);
    }
}
