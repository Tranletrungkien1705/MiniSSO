using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test nhập hàng loạt nhóm từ file (port từ iNOS.InBrand: SysGroupController.Import).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class GroupImportServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GroupImportService _svc;

    public GroupImportServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new GroupImportService(_db);

        _db.Orgs.Add(new Org { Code = "10", Name = "Miền Bắc" });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private const string Header = "Code,DLCode,Description\n";

    [Fact]
    public async Task Import_AddsAllRows()
    {
        var res = await _svc.ImportCsvAsync(Header + "GRP_HN1,10,Nhóm Hà Nội\nGRP_HCM,21,Nhóm HCM");
        Assert.True(res.Ok);
        Assert.Equal(2, res.Imported);
        Assert.Equal(2, await _db.Groups.CountAsync());
        var hn = await _db.Groups.SingleAsync(g => g.Code == "GRP_HN1");
        Assert.Equal("Nhóm Hà Nội", hn.Description);
        Assert.True(hn.IsActive);
    }

    [Fact]
    public async Task Import_MapsDlCodeToOrg()
    {
        await _svc.ImportCsvAsync(Header + "GRP_HN1,10,Nhóm Hà Nội");
        var org = await _db.Orgs.SingleAsync(o => o.Code == "10");
        var g = await _db.Groups.SingleAsync();
        Assert.Equal(org.Id, g.OrgId);
    }

    [Fact]
    public async Task Import_UnknownDlCode_LeavesOrgNull()
    {
        await _svc.ImportCsvAsync(Header + "GRP_X,999,Nhóm lạ");
        var g = await _db.Groups.SingleAsync();
        Assert.Null(g.OrgId);
    }

    [Fact]
    public async Task Import_EmptyContent_Fails()
    {
        var res = await _svc.ImportCsvAsync("");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_HeaderOnly_Fails()
    {
        var res = await _svc.ImportCsvAsync(Header);
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_WrongColumnCount_Fails()
    {
        var res = await _svc.ImportCsvAsync(Header + "GRP_HN1,10");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_EmptyCode_Fails()
    {
        var res = await _svc.ImportCsvAsync(Header + ",10,Nhóm Hà Nội");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_EmptyDlCode_Fails()
    {
        var res = await _svc.ImportCsvAsync(Header + "GRP_HN1,,Nhóm Hà Nội");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_EmptyDescription_Fails()
    {
        var res = await _svc.ImportCsvAsync(Header + "GRP_HN1,10,");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_DescriptionTooLong_Fails()
    {
        var longDesc = new string('x', GroupImportService.RemarkLength + 1);
        var res = await _svc.ImportCsvAsync(Header + $"GRP_HN1,10,{longDesc}");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_DescriptionAtLimit_Ok()
    {
        var desc = new string('x', GroupImportService.RemarkLength);
        var res = await _svc.ImportCsvAsync(Header + $"GRP_HN1,10,{desc}");
        Assert.True(res.Ok);
        Assert.Equal(1, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_DuplicateCodeInFile_Fails()
    {
        var res = await _svc.ImportCsvAsync(Header + "GRP_HN1,10,Nhóm 1\nGRP_HN1,10,Nhóm 2");
        Assert.False(res.Ok);
        Assert.Equal(0, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_CodeAlreadyInDb_Fails()
    {
        _db.Groups.Add(new Group { Code = "GRP_HN1", Name = "GRP_HN1" });
        await _db.SaveChangesAsync();
        var res = await _svc.ImportCsvAsync(Header + "GRP_HN1,10,Nhóm Hà Nội");
        Assert.False(res.Ok);
        Assert.Equal(1, await _db.Groups.CountAsync());
    }

    [Fact]
    public async Task Import_NormalizesCodeToUpper()
    {
        await _svc.ImportCsvAsync(Header + "grp_hn1,10,Nhóm Hà Nội");
        var g = await _db.Groups.SingleAsync();
        Assert.Equal("GRP_HN1", g.Code);
    }
}
