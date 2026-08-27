namespace MiniSSO.Domain;

/// <summary>Người dùng định danh (thay cho iNOS user). Mật khẩu băm PBKDF2.</summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Roles { get; set; } = "";      // csv, vd "Admin,Sales"
    public string? Tenant { get; set; }           // tổ chức/đại lý (claim cho app downstream)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string[] RoleList => string.IsNullOrWhiteSpace(Roles) ? [] : Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Ứng dụng client (relying party) — mỗi app trong fleet là 1 client.</summary>
public class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClientId { get; set; } = "";
    public string? ClientSecretHash { get; set; }   // null = public client (PKCE)
    public string Name { get; set; } = "";
    public string RedirectUris { get; set; } = "";  // csv
    public string AllowedGrants { get; set; } = "authorization_code,refresh_token";  // csv
    public string AllowedScopes { get; set; } = "openid,profile,email";              // csv
    public bool RequirePkce { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string[] Redirects => Split(RedirectUris);
    public string[] Grants => Split(AllowedGrants);
    public string[] Scopes => Split(AllowedScopes);
    private static string[] Split(string s) => string.IsNullOrWhiteSpace(s) ? [] : s.Split(new[] { ',', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Refresh token (cấp lại access token không cần đăng nhập lại).</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = "";
    public Guid UserId { get; set; }
    public string ClientId { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>Authorization code (luồng authorization_code + PKCE) — dùng 1 lần.</summary>
public class AuthCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string ClientId { get; set; } = "";
    public Guid UserId { get; set; }
    public string RedirectUri { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Consumed { get; set; }
}

/// <summary>Khóa ký JWT (RSA) — lưu để token sống qua các lần khởi động lại.</summary>
public class SigningKey
{
    public int Id { get; set; }
    public string Kid { get; set; } = "";
    public string PrivateKeyPkcs8 { get; set; } = "";   // base64 PKCS#8
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
