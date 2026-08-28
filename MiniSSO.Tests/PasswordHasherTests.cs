using MiniSSO.Domain;
using MiniSSO.Services;
using Xunit;

namespace MiniSSO.Tests;

/// <summary>Test PBKDF2 + fix bug copy dấu cách mật khẩu (Normalize trim + Unicode NFC); helper entity.</summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_Then_Verify_Ok()
    {
        var h = PasswordHasher.Hash("Admin@123");
        Assert.True(PasswordHasher.Verify("Admin@123", h));
        Assert.False(PasswordHasher.Verify("sai-mat-khau", h));
    }

    [Fact]
    public void TrailingSpaces_Trimmed_StillMatches()
    {
        // Bug đã báo: copy dấu cách ở mật khẩu → phải bỏ (Trim).
        var h = PasswordHasher.Hash("Admin@123");
        Assert.True(PasswordHasher.Verify("  Admin@123  ", h));
        Assert.True(PasswordHasher.Verify("Admin@123 ", h));
    }

    [Fact]
    public void Normalize_TrimsAndNfc()
    {
        Assert.Equal("abc", PasswordHasher.Normalize("  abc  "));
        Assert.Equal("", PasswordHasher.Normalize(null));
    }

    [Fact]
    public void UnicodeNfc_EquivalentForms_Match()
    {
        // "é" dạng tổ hợp (e + U+0301) vs dạng dựng sẵn (U+00E9) → NFC chuẩn hóa như nhau.
        var composed = "café";       // café (combining)
        var precomposed = "café";     // café (precomposed)
        var h = PasswordHasher.Hash(composed);
        Assert.True(PasswordHasher.Verify(precomposed, h));
    }

    [Fact]
    public void Hashes_AreSalted_Different()
    {
        Assert.NotEqual(PasswordHasher.Hash("x"), PasswordHasher.Hash("x"));  // salt ngẫu nhiên
    }

    [Fact]
    public void AppUser_RoleList_ParsesCsv()
    {
        var u = new AppUser { Roles = "Admin, Sales ,, Dealer" };
        Assert.Equal(new[] { "Admin", "Sales", "Dealer" }, u.RoleList);
    }

    [Fact]
    public void Client_Grants_ParsesCsv()
    {
        var c = new Client { AllowedGrants = "authorization_code,refresh_token" };
        Assert.Contains("refresh_token", c.Grants);
    }
}
