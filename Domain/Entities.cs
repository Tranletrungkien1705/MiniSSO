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

    // ── Hồ sơ tự đăng ký (port từ iNOS.InBrand SysUser: PhoneNo / Language) ──
    // iNOS lưu số điện thoại (SysUser.PhoneNo) và ngôn ngữ (SysUser.Language, mặc định "vi") khi
    // người dùng tự đăng ký tham gia hệ thống (AccountController.Join → SysUserManager.Register).
    public string? PhoneNo { get; set; }           // SysUser.PhoneNo
    public string Language { get; set; } = "vi";   // SysUser.Language — mặc định "vi" khi đăng ký

    // ── Bảo mật tài khoản (port từ iNOS.InBrand SysUser: Enable / Lockout / LockoutDate / VerificationCode) ──
    // iNOS tách "Enable" (bị vô hiệu hoá) khỏi "Lockout" (bị khoá do đăng nhập sai nhiều lần).
    // MiniSSO trước đây chỉ có IsActive; bổ sung đếm số lần đăng nhập sai + tự khoá tạm thời.
    public int FailedLoginCount { get; set; }              // số lần đăng nhập sai liên tiếp
    public bool IsLockedOut { get; set; }                  // SysUser.Lockout — đang bị khoá
    public DateTime? LockoutDate { get; set; }             // SysUser.LockoutDate — thời điểm bị khoá
    public DateTime? LockoutUntil { get; set; }            // hết hạn khoá tạm thời (null = khoá vĩnh viễn tới khi admin mở)
    public string? VerificationCode { get; set; }          // SysUser.VerificationCode — mã xác thực (đặt lại mật khẩu)
    public DateTime? LastLoginAt { get; set; }             // lần đăng nhập thành công gần nhất

    // ── Phạm vi dữ liệu (port từ iNOS.InBrand SysUser: SysAdmin / DLCode) ──
    // iNOS gắn mỗi người dùng vào 1 đại lý (DLCode) nằm trong cây đại lý; người dùng chỉ thấy dữ liệu
    // thuộc nhánh của mình, trừ khi là SysAdmin hoặc ở nút gốc. MiniSSO trước đây không có khái niệm này.
    public bool IsSysAdmin { get; set; }                   // SysUser.SysAdmin — bỏ qua mọi giới hạn phạm vi
    public Guid? OrgId { get; set; }                       // đơn vị tổ chức người dùng thuộc về (tương ứng SysUser.DLCode)

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
    // ── Đơn vị của nhóm (port từ iNOS.InBrand SysGroup.DLCode) ──
    // iNOS gắn mỗi nhóm vào 1 đại lý (DLCode); thành viên thêm vào nhóm phải cùng đơn vị với nhóm
    // (Sys_UserInGroup_Save_InputTblDtl_InvalidDLCode). null = nhóm toàn cục (không ràng buộc đơn vị).
    public Guid? OrgId { get; set; }
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

// ── Gán module trực tiếp cho nhóm (port từ iNOS.InBrand: Sys_Access = GroupCode + ModuleCode) ──
// iNOS có bảng Sys_Access nối TRỰC TIẾP nhóm ↔ module (GroupCode, ModuleCode). Màn hình
// SysGroupController.GetSysModule liệt kê TẤT CẢ module kèm cờ "đã gán cho nhóm này chưa"
// (SysAccessService.GetAllAccessByGroupCode), và SaveModuleInGroup → SysAccessSave_New20171101
// lưu theo cơ chế "xoá sạch rồi ghi lại" (clear-all → insert-all). MiniSSO trước đây chỉ có
// GroupAccess (nhóm ↔ đối tượng quyền) và suy ra module GIÁN TIẾP qua PermissionObject.Module —
// KHÔNG có liên kết nhóm ↔ module trực tiếp. Bổ sung đúng bảng nối của iNOS.

/// <summary>Gán module trực tiếp cho nhóm (tương ứng Sys_Access): nhóm được cấp 1 module.</summary>
public class GroupModuleAccess
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }              // Sys_Access.GroupCode
    public Guid ModuleId { get; set; }             // Sys_Access.ModuleCode
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

// ── Nhật ký đăng nhập (port từ iNOS.InBrand: Sys_User_Login + TLog) ──
// iNOS ghi log mỗi lần đăng nhập (thành công/thất bại) để truy vết bảo mật.
// MiniSSO bổ sung bảng này để lưu vết đăng nhập phục vụ điều tra + thống kê.

/// <summary>Một lần thử đăng nhập (thành công hoặc thất bại) — phục vụ truy vết bảo mật.</summary>
public class LoginAttempt
{
    public long Id { get; set; }
    public string Email { get; set; } = "";        // email/định danh đã thử
    public Guid? UserId { get; set; }               // null nếu không tìm thấy người dùng
    public bool Success { get; set; }
    public string? Reason { get; set; }             // lý do thất bại (sai mật khẩu / bị khoá / vô hiệu)
    public string? RemoteIp { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}

// ── Phân hệ chức năng: Module → Function (port từ iNOS.InBrand: SysModule / SysFunction / SysFunctionInModule) ──
// iNOS tổ chức menu/chức năng thành CÂY module (SysModule.ParentCode) và mỗi module gồm nhiều
// chức năng (SysFunction) qua bảng nối SysFunctionInModule. Nhóm được cấp quyền tới MODULE (SysAccess),
// và SysModuleManager.GetAllByUser suy ra "menu hiệu lực" của người dùng = các module (kèm chức năng)
// mà mọi nhóm đang hoạt động của họ được cấp. MiniSSO trước đây chỉ có PermissionObject phẳng với
// 1 cột Module dạng chuỗi — bổ sung cây module + chức năng để dựng menu theo người dùng.

/// <summary>Phân hệ chức năng (tương ứng SysModule). Cây phân cấp qua <see cref="ParentId"/>.</summary>
public class Module
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã module, vd "sso" (SysModule.Code)
    public string Title { get; set; } = "";       // tiêu đề hiển thị (SysModule.Title)
    public string? Description { get; set; }        // SysModule.Description
    public string? ModuleType { get; set; }         // SysModule.ModuleType (vd "MENU", "PAGE")
    public Guid? ParentId { get; set; }             // module cha (SysModule.ParentCode); null = gốc
    public int SortOrder { get; set; }              // thứ tự hiển thị trong menu
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Chức năng trong 1 module (tương ứng SysFunction): 1 hành động/màn hình con.</summary>
public class Function
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã chức năng, vd "user.create" (SysFunction.Code)
    public string Description { get; set; } = "";  // SysFunction.Description
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gán chức năng vào module (tương ứng SysFunctionInModule): quan hệ nhiều-nhiều Module ↔ Function.</summary>
public class FunctionInModule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    public Guid FunctionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ── Nhóm cột hiển thị: ViewGroupView → ViewColumnInGroup → ViewColumnView (port từ iNOS.InBrand) ──
// iNOS cho phép cấu hình "cột hiển thị" (View_ColumnView) và gom chúng thành "nhóm cột hiển thị"
// (View_GroupView) qua bảng nối View_ColumnInGroup. Khi lưu 1 nhóm, iNOS dùng cơ chế
// "xoá sạch rồi ghi lại" (ViewColumnInGroupSaveX): xoá hết cột của nhóm rồi ghi lại đúng tập mới,
// đồng thời kiểm tra nhóm tồn tại & đang hoạt động và mỗi cột phải tồn tại & đang hoạt động.
// MiniSSO trước đây không có khái niệm cấu hình cột hiển thị theo nhóm.

/// <summary>Cột hiển thị (tương ứng View_ColumnView): 1 cột dữ liệu có thể bật/tắt theo nhóm.</summary>
public class ViewColumnView
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã cột (View_ColumnView.ColumnViewCode)
    public string Name { get; set; } = "";        // tên hiển thị (View_ColumnView.ColumnViewName)
    public string? Remark { get; set; }
    public bool IsActive { get; set; } = true;     // View_ColumnView.FlagActive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Nhóm cột hiển thị (tương ứng View_GroupView): tập hợp các cột hiển thị.</summary>
public class ViewGroupView
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã nhóm (View_GroupView.GroupViewCode)
    public string Name { get; set; } = "";        // tên hiển thị (View_GroupView.GroupViewName)
    public string? Remark { get; set; }
    public bool IsActive { get; set; } = true;     // View_GroupView.FlagActive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gán cột vào nhóm cột hiển thị (tương ứng View_ColumnInGroup): nhiều-nhiều ViewGroupView ↔ ViewColumnView.</summary>
public class ViewColumnInGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupViewId { get; set; }
    public Guid ColumnViewId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ── Đội người dùng: Sys_UserTeam (port từ iNOS.InBrand) ──
// iNOS có bảng Sys_UserTeam (SysUserTeam: TeamCode PK, DLCode, TeamName, FlagActive) — "đội" thuộc
// một đại lý/đơn vị (DLCode). Đây là khái niệm TỔ CHỨC (đội trong đơn vị), KHÁC với Group (nhóm quyền):
// Group gom người dùng để cấp quyền, còn Team gom người dùng theo đơn vị nghiệp vụ. MiniSSO trước đây
// không có khái niệm "đội".

/// <summary>Đội người dùng (tương ứng Sys_UserTeam): một đội thuộc 1 đơn vị tổ chức.</summary>
public class UserTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";        // mã đội (Sys_UserTeam.TeamCode) — duy nhất
    public string Name { get; set; } = "";        // tên đội (Sys_UserTeam.TeamName)
    public Guid? OrgId { get; set; }               // đơn vị của đội (Sys_UserTeam.DLCode); null = đội toàn cục
    public bool IsActive { get; set; } = true;     // Sys_UserTeam.FlagActive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ── Phiên làm việc hiệu lực: SysSession / GlobSession (port từ iNOS.InBrand) ──
// iNOS tạo 1 "phiên" (GlobSession) khi đăng nhập, rồi GlobSessionManager.GetFullPermission dựng
// SysSession = ảnh chụp quyền hiệu lực của người dùng: IsSysAdmin + SysUser + danh sách Module
// (kèm Function) + bối cảnh đơn vị (DLName) / kho (InvCode). SysSession có HasModule(code) /
// HasFunction(code) để màn hình kiểm tra nhanh quyền. MiniSSO trước đây chỉ có các mảnh rời
// (RbacService, ModuleService, DataScopeService) nhưng KHÔNG có 1 đối tượng "phiên" gộp lại.

/// <summary>Phiên làm việc (tương ứng GlobSession): 1 lần đăng nhập của người dùng, có mã phiên.</summary>
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SessionId { get; set; } = "";    // mã phiên (GlobSession.SessionId) — duy nhất
    public Guid UserId { get; set; }                // người dùng sở hữu phiên (GlobSession.UserId)
    public bool IsSysAdmin { get; set; }            // SysSession.IsSysAdmin — chụp tại thời điểm tạo
    public Guid? OrgId { get; set; }                // bối cảnh đơn vị (SysSession.DLName ↔ SysUser.DLCode)
    public string? OrgName { get; set; }            // tên đơn vị hiển thị (SysSession.DLName)
    public string? InvCode { get; set; }            // bối cảnh kho (SysSession.InvCode)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }        // hết hạn phiên (null = không giới hạn)
    public bool IsActive { get; set; } = true;      // phiên còn hiệu lực (đăng xuất → false)
}
