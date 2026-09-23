using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>
/// Test xuất danh sách nhóm ra file (port từ iNOS.InBrand: SysGroupController.Export + ExportTemplate).
/// Dùng SQLite in-memory (giữ kết nối mở để DB không bị xoá giữa các lệnh).
/// </summary>
public class GroupExportServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GroupExportService _svc;

    public GroupExportServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new GroupExportService(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private static string[] Lines(string csv) =>
        csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Export_EmptyDb_OnlyHeader()
    {
        var csv = await _svc.ExportCsvAsync();
        var lines = Lines(csv);
        Assert.Single(lines);
        Assert.Equal("Code,DLCode,Description,Enable", lines[0]);
    }

    [Fact]
    public async Task Export_HeaderMatchesDataHeaders()
    {
        var csv = await _svc.ExportCsvAsync();
        Assert.Equal(string.Join(',', GroupExportService.DataHeaders), Lines(csv)[0]);
    }

    [Fact]
    public async Task Export_IncludesAllGroupsSortedByCode()
    {
        _db.Groups.Add(new Group { Code = "GRP_B", Name = "B" });
        _db.Groups.Add(new Group { Code = "GRP_A", Name = "A" });
        await _db.SaveChangesAsync();

        var lines = Lines(await _svc.ExportCsvAsync());
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("GRP_A,", lines[1]);
        Assert.StartsWith("GRP_B,", lines[2]);
    }

    [Fact]
    public async Task Export_MapsOrgIdToDlCode()
    {
        var org = new Org { Code = "10", Name = "Miền Bắc" };
        _db.Orgs.Add(org);
        await _db.SaveChangesAsync();
        _db.Groups.Add(new Group { Code = "GRP_HN", Name = "HN", OrgId = org.Id, Description = "Nhóm Hà Nội" });
        await _db.SaveChangesAsync();

        var line = Lines(await _svc.ExportCsvAsync())[1];
        Assert.Equal("GRP_HN,10,Nhóm Hà Nội,1", line);
    }

    [Fact]
    public async Task Export_GlobalGroup_HasEmptyDlCode()
    {
        _db.Groups.Add(new Group { Code = "GRP_G", Name = "G" });
        await _db.SaveChangesAsync();

        var line = Lines(await _svc.ExportCsvAsync())[1];
        Assert.Equal("GRP_G,,,1", line);
    }

    [Fact]
    public async Task Export_InactiveGroup_EnableZero()
    {
        _db.Groups.Add(new Group { Code = "GRP_OFF", Name = "Off", IsActive = false });
        await _db.SaveChangesAsync();

        var line = Lines(await _svc.ExportCsvAsync())[1];
        Assert.EndsWith(",0", line);
    }

    [Fact]
    public async Task Export_DescriptionWithComma_IsQuoted()
    {
        _db.Groups.Add(new Group { Code = "GRP_C", Name = "C", Description = "Nhóm A, B" });
        await _db.SaveChangesAsync();

        var line = Lines(await _svc.ExportCsvAsync())[1];
        Assert.Equal("GRP_C,,\"Nhóm A, B\",1", line);
    }

    [Fact]
    public void ExportTemplate_OnlyHeaderWithoutEnable()
    {
        var csv = _svc.ExportTemplateCsv();
        var lines = Lines(csv);
        Assert.Single(lines);
        Assert.Equal("Code,DLCode,Description", lines[0]);
        Assert.Equal(string.Join(',', GroupExportService.TemplateHeaders), lines[0]);
    }
}
