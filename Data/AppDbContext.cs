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

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("minisso");
        b.Entity<AppUser>(e => { e.HasIndex(x => x.Email).IsUnique(); e.Ignore(x => x.RoleList); });
        b.Entity<Client>(e => { e.HasIndex(x => x.ClientId).IsUnique(); e.Ignore(x => x.Redirects); e.Ignore(x => x.Grants); e.Ignore(x => x.Scopes); });
        b.Entity<RefreshToken>().HasIndex(x => x.Token).IsUnique();
        b.Entity<AuthCode>().HasIndex(x => x.Code).IsUnique();
    }
}
