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

/// <summary>Bản quyền phần mềm cấp cho 1 chủ sở hữu — mỗi app trong fleet tự gọi /api/v1/license/check khi khởi động (công khai trong Program.cs, không ẩn giấu) để xác thực + ghi log ai đang chạy.</summary>
public class AppLicense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LicenseKey { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Lịch sử mỗi lần 1 instance app gọi về xác thực license — cho biết ai/ở đâu đang chạy source code.</summary>
public class LicenseCheckLog
{
    public long Id { get; set; }
    public string LicenseKey { get; set; } = "";
    public string AppSlug { get; set; } = "";
    public string? InstanceHost { get; set; }
    public string? RemoteIp { get; set; }
    public bool Result { get; set; }
    public string? Message { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

// ── RBAC theo nhóm (port từ iNOS.InBrand: Sys_Group / Sys_UserInGroup / Sys_Access / Sys_Object) ──
// iNOS gán quyền qua chuỗi: User → UserInGroup → Group → Access → Object (chức năng/module).
// MiniSSO trước đây chỉ có Roles csv phẳng trên AppUser; bổ sung mô hình nhóm + đối tượng quyền
// để suy ra "quyền hiệu lực" (effective permissions) của 1 người dùng.

/// <summary>Nhóm quyền (tương ứng Sys_Group). Người dùng thuộc nhiều nhóm; nhóm được cấp nhiều đối tượng quyền.</summary>
public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã nhóm, vd "SYSADMIN" (Sys_Group.GroupCode)
    public string Name { get; set; } = "";        // tên hiển thị (Sys_Group.GroupName)
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;    // Sys_Group.FlagActive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Thành viên nhóm (tương ứng Sys_UserInGroup): quan hệ nhiều-nhiều User ↔ Group.</summary>
public class GroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Đối tượng quyền (tương ứng Sys_Object): 1 chức năng/màn hình/module được bảo vệ.</summary>
public class PermissionObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã đối tượng, vd "user.manage" (Sys_Object.ObjectCode)
    public string Name { get; set; } = "";        // tên hiển thị (Sys_Object.ObjectName)
    public string? Module { get; set; }           // nhóm chức năng (Sys_Object.ServiceCode)
    public bool IsActive { get; set; } = true;    // Sys_Object.FlagActive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Cấp quyền (tương ứng Sys_Access): nhóm được phép truy cập 1 đối tượng quyền.</summary>
public class GroupAccess
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid ObjectId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ── Tổ chức theo cây (port từ iNOS.InBrand: Mst_Org) ──
// iNOS lưu cây tổ chức dạng "materialized path": mỗi nút có OrgParent (cha) và 3 cột dẫn xuất
// OrgBUCode (đường dẫn mã, vd "0.10.20"), OrgBUPattern (tiền tố để truy vấn cả nhánh, vd "0.10.20%"),
// OrgLevel (độ sâu). MiniSSO trước đây chỉ có chuỗi Tenant phẳng trên AppUser; bổ sung cây tổ chức
// để biết 1 đơn vị nằm ở đâu trong cấu trúc và lấy được toàn bộ nhánh con của nó.

/// <summary>Đơn vị tổ chức (tương ứng Mst_Org). Cây phân cấp qua <see cref="ParentId"/>.</summary>
public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";          // mã đơn vị (Mst_Org.OrgID)
    public string Name { get; set; } = "";          // tên hiển thị
    public Guid? ParentId { get; set; }              // đơn vị cha (Mst_Org.OrgParent); null = gốc
    public string BuCode { get; set; } = "";        // đường dẫn mã (Mst_Org.OrgBUCode), vd "0.10.20"
    public string BuPattern { get; set; } = "";     // tiền tố nhánh (Mst_Org.OrgBUPattern), vd "0.10.20%"
    public int Level { get; set; } = 1;              // độ sâu (Mst_Org.OrgLevel)
    public string? Remark { get; set; }
    public bool IsActive { get; set; } = true;       // Mst_Org.FlagActive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
