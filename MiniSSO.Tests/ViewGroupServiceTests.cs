using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test nhóm cột hiển thị (port từ iNOS.InBrand:
/// View_GroupView / View_ColumnInGroup / View_ColumnView + ViewColumnInGroupManager.ViewColumnInGroupSaveX).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class ViewGroupServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly ViewGroupService _svc;

    private readonly ViewGroupView _sale, _admin;
    private readonly ViewColumnView _cCode, _cName, _cOrg, _cInactive;

    public ViewGroupServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new ViewGroupService(_db);

        _sale = new ViewGroupView { Code = "GRID_SALE", Name = "Lưới kinh doanh" };
        _admin = new ViewGroupView { Code = "GRID_ADMIN", Name = "Lưới quản trị" };
        _db.ViewGroupViews.AddRange(_sale, _admin);

        _cCode = new ViewColumnView { Code = "col.code", Name = "Mã" };
        _cName = new ViewColumnView { Code = "col.name", Name = "Tên" };
        _cOrg = new ViewColumnView { Code = "col.org", Name = "Đơn vị" };
        _cInactive = new ViewColumnView { Code = "col.old", Name = "Cột cũ", IsActive = false };
        _db.ViewColumnViews.AddRange(_cCode, _cName, _cOrg, _cInactive);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task SetColumns_ReplacesAll()
    {
        await _svc.SetColumnsAsync(_sale.Id, new[] { "col.code", "col.name" });
        Assert.Equal(2, await _db.ViewColumnInGroups.CountAsync(l => l.GroupViewId == _sale.Id));

        await _svc.SetColumnsAsync(_sale.Id, new[] { "col.org" });
        var codes = await _db.ViewColumnInGroups.Where(l => l.GroupViewId == _sale.Id)
            .Join(_db.ViewColumnViews, l => l.ColumnViewId, c => c.Id, (l, c) => c.Code).ToListAsync();
        Assert.Single(codes);
        Assert.Equal("col.org", codes[0]);
    }

    [Fact]
    public async Task SetColumns_RejectsUnknownColumn()
    {
        var res = await _svc.SetColumnsAsync(_sale.Id, new[] { "nope.code" });
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.ViewColumnInGroups.CountAsync(l => l.GroupViewId == _sale.Id));
    }

    [Fact]
    public async Task SetColumns_RejectsInactiveColumn()
    {
        var res = await _svc.SetColumnsAsync(_sale.Id, new[] { "col.code", "col.old" });
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.ViewColumnInGroups.CountAsync(l => l.GroupViewId == _sale.Id));
    }

    [Fact]
    public async Task SetColumns_RejectsEmptyList()
    {
        var res = await _svc.SetColumnsAsync(_sale.Id, Array.Empty<string>());
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task SetColumns_UnknownGroup_Fails()
    {
        var res = await _svc.SetColumnsAsync(Guid.NewGuid(), new[] { "col.code" });
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task SetColumns_InactiveGroup_Fails()
    {
        _sale.IsActive = false;
        await _db.SaveChangesAsync();
        var res = await _svc.SetColumnsAsync(_sale.Id, new[] { "col.code" });
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task AllWithColumns_ReturnsGroupsWithColumns()
    {
        await _svc.SetColumnsAsync(_sale.Id, new[] { "col.code", "col.name" });
        await _svc.SetColumnsAsync(_admin.Id, new[] { "col.code", "col.org" });

        var all = await _svc.AllWithColumnsAsync();
        Assert.Equal(2, all.Count);
        var sale = all.Single(x => x.Group.Code == "GRID_SALE");
        Assert.Equal(2, sale.Columns.Count);
        var admin = all.Single(x => x.Group.Code == "GRID_ADMIN");
        Assert.Equal(2, admin.Columns.Count);
    }

    [Fact]
    public async Task ColumnsOfGroup_ReturnsSortedColumns()
    {
        await _svc.SetColumnsAsync(_sale.Id, new[] { "col.name", "col.code" });
        var cols = await _svc.ColumnsOfGroupAsync(_sale.Id);
        Assert.Equal(2, cols.Count);
        Assert.Equal("col.code", cols[0].Code);
        Assert.Equal("col.name", cols[1].Code);
    }

    [Fact]
    public async Task DeleteGroup_RemovesGroupAndLinks()
    {
        await _svc.SetColumnsAsync(_sale.Id, new[] { "col.code" });
        Assert.True(await _svc.DeleteGroupAsync(_sale.Id));
        Assert.False(await _db.ViewGroupViews.AnyAsync(g => g.Id == _sale.Id));
        Assert.Equal(0, await _db.ViewColumnInGroups.CountAsync(l => l.GroupViewId == _sale.Id));
    }

    [Fact]
    public async Task DeleteGroup_Unknown_ReturnsFalse()
    {
        Assert.False(await _svc.DeleteGroupAsync(Guid.NewGuid()));
    }
}
