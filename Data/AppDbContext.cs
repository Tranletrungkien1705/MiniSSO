using Microsoft.EntityFrameworkCore;
using MiniSSO.Domain;

namespace MiniSSO.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuthCode> AuthCodes => Set<AuthCode>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<AppLicense> Licenses => Set<AppLicense>();
    public DbSet<LicenseCheckLog> LicenseCheckLogs => Set<LicenseCheckLog>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<PermissionObject> PermissionObjects => Set<PermissionObject>();
    public DbSet<GroupAccess> GroupAccesses => Set<GroupAccess>();
    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Function> Functions => Set<Function>();
    public DbSet<FunctionInModule> FunctionInModules => Set<FunctionInModule>();
    public DbSet<ViewColumnView> ViewColumnViews => Set<ViewColumnView>();
    public DbSet<ViewGroupView> ViewGroupViews => Set<ViewGroupView>();
    public DbSet<ViewColumnInGroup> ViewColumnInGroups => Set<ViewColumnInGroup>();
    public DbSet<UserTeam> UserTeams => Set<UserTeam>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("minisso");
        b.Entity<AppUser>(e => { e.HasIndex(x => x.Email).IsUnique(); e.HasIndex(x => x.OrgId); e.Ignore(x => x.RoleList); });
        b.Entity<Client>(e => { e.HasIndex(x => x.ClientId).IsUnique(); e.Ignore(x => x.Redirects); e.Ignore(x => x.Grants); e.Ignore(x => x.Scopes); });
        b.Entity<RefreshToken>().HasIndex(x => x.Token).IsUnique();
        b.Entity<AuthCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<AppLicense>().HasIndex(x => x.LicenseKey).IsUnique();
        b.Entity<Group>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.OrgId); });
        b.Entity<GroupMember>(e => e.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique());
        b.Entity<PermissionObject>(e => e.HasIndex(x => x.Code).IsUnique());
        b.Entity<GroupAccess>(e => e.HasIndex(x => new { x.GroupId, x.ObjectId }).IsUnique());
        b.Entity<Org>(e => e.HasIndex(x => x.Code).IsUnique());
        b.Entity<LoginAttempt>(e => e.HasIndex(x => x.AttemptedAt));
        b.Entity<Module>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.ParentId); });
        b.Entity<Function>(e => e.HasIndex(x => x.Code).IsUnique());
        b.Entity<FunctionInModule>(e => e.HasIndex(x => new { x.ModuleId, x.FunctionId }).IsUnique());
        b.Entity<ViewColumnView>(e => e.HasIndex(x => x.Code).IsUnique());
        b.Entity<ViewGroupView>(e => e.HasIndex(x => x.Code).IsUnique());
        b.Entity<ViewColumnInGroup>(e => e.HasIndex(x => new { x.GroupViewId, x.ColumnViewId }).IsUnique());
        b.Entity<UserTeam>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.OrgId); });
        b.Entity<UserSession>(e => { e.HasIndex(x => x.SessionId).IsUnique(); e.HasIndex(x => x.UserId); });
    }
}
