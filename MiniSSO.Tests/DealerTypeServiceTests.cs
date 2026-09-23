using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test loại đại lý (port từ iNOS.InBrand: Mst_DealerType + MstDealerTypeManager).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class DealerTypeServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly DealerTypeService _svc;

    public DealerTypeServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new DealerTypeService(_db, new CheckDbService(_db));
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Create_AddsDealerType()
    {
        var res = await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        Assert.True(res.Ok);
        var t = await _db.DealerTypes.SingleAsync();
        Assert.Equal("DL_TYPE_1", t.Code);
        Assert.Equal("Đại lý cấp 1", t.Name);
        Assert.True(t.IsActive);
    }

    [Fact]
    public async Task Create_RequiresCode()
    {
        var res = await _svc.CreateAsync("  ", "Đại lý cấp 1");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.DealerTypes.CountAsync());
    }

    [Fact]
    public async Task Create_RequiresName()
    {
        var res = await _svc.CreateAsync("DL_TYPE_1", "  ");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.DealerTypes.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsDuplicateCode()
    {
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        var res = await _svc.CreateAsync("DL_TYPE_1", "Loại khác");
        Assert.False(res.Ok);
        Assert.Equal(1, await _db.DealerTypes.CountAsync());
    }

    [Fact]
    public async Task Create_TrimsCodeAndName()
    {
        await _svc.CreateAsync("  DL_TYPE_1  ", "  Đại lý cấp 1  ");
        var t = await _db.DealerTypes.SingleAsync();
        Assert.Equal("DL_TYPE_1", t.Code);
        Assert.Equal("Đại lý cấp 1", t.Name);
    }

    [Fact]
    public async Task Update_ChangesName()
    {
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        var t = await _db.DealerTypes.SingleAsync();
        var res = await _svc.UpdateAsync(t.Id, "Đại lý cấp 1 (mới)");
        Assert.True(res.Ok);
        Assert.Equal("Đại lý cấp 1 (mới)", (await _db.DealerTypes.SingleAsync()).Name);
    }

    [Fact]
    public async Task Update_UnknownType_Fails()
    {
        var res = await _svc.UpdateAsync(Guid.NewGuid(), "x");
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Update_RequiresName()
    {
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        var t = await _db.DealerTypes.SingleAsync();
        var res = await _svc.UpdateAsync(t.Id, "  ");
        Assert.False(res.Ok);
        Assert.Equal("Đại lý cấp 1", (await _db.DealerTypes.SingleAsync()).Name);
    }

    [Fact]
    public async Task Toggle_FlipsActive()
    {
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        var t = await _db.DealerTypes.SingleAsync();
        Assert.True(await _svc.ToggleAsync(t.Id));
        Assert.False((await _db.DealerTypes.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Delete_RemovesType()
    {
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        var t = await _db.DealerTypes.SingleAsync();
        Assert.True(await _svc.DeleteAsync(t.Id));
        Assert.Equal(0, await _db.DealerTypes.CountAsync());
    }

    [Fact]
    public async Task Active_FiltersInactive()
    {
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        await _svc.CreateAsync("DL_TYPE_2", "Đại lý cấp 2");
        var t2 = await _db.DealerTypes.SingleAsync(x => x.Code == "DL_TYPE_2");
        await _svc.ToggleAsync(t2.Id);

        var active = await _svc.ActiveAsync();
        Assert.Single(active);
        Assert.Equal("DL_TYPE_1", active[0].Code);
    }

    [Fact]
    public async Task All_SortedByCode()
    {
        await _svc.CreateAsync("DL_TYPE_2", "Đại lý cấp 2");
        await _svc.CreateAsync("DL_TYPE_1", "Đại lý cấp 1");
        var all = await _svc.AllAsync();
        Assert.Equal(new[] { "DL_TYPE_1", "DL_TYPE_2" }, all.Select(t => t.Code).ToArray());
    }
}
