using System.Security.Cryptography;

namespace MiniSSO.Services;

/// <summary>Băm mật khẩu PBKDF2-SHA256 (100k vòng, salt ngẫu nhiên) — chuẩn production.</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000, SaltSize = 16, KeySize = 32;

    /// <summary>
    /// Chuẩn hóa mật khẩu đầu vào TRƯỚC khi băm/so khớp: bỏ khoảng trắng thừa 2 đầu
    /// (tránh lỗi copy-paste dính dấu cách) và chuẩn hóa Unicode NFC (đồng nhất ký tự đặc biệt/dấu).
    /// Băm và verify DÙNG CHUNG hàm này nên luôn nhất quán.
    /// </summary>
    public static string Normalize(string? password) =>
        (password ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormC);

    public static string Hash(string password)
    {
        var norm = Normalize(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(norm, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var norm = Normalize(password);
        var parts = (stored ?? "").Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iters)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(norm, salt, iters, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
