using Microsoft.EntityFrameworkCore;
using MiniSSO.Domain;
using MiniSSO.Services;

namespace MiniSSO.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync())
        {
            db.Users.AddRange(
                new AppUser { Email = "admin@minisso.dev", FullName = "Quản trị hệ thống", Roles = "Admin", PasswordHash = PasswordHasher.Hash("Admin@123") },
                new AppUser { Email = "sales@minisso.dev", FullName = "Nhân viên Kinh doanh", Roles = "Sales", Tenant = "Demo", PasswordHash = PasswordHasher.Hash("Sales@123") },
                new AppUser { Email = "dealer@minisso.dev", FullName = "Đại lý Đông Đô", Roles = "Dealer", Tenant = "DongDo", PasswordHash = PasswordHasher.Hash("Dealer@123") });
            await db.SaveChangesAsync();
        }
        if (!await db.Clients.AnyAsync())
        {
            db.Clients.AddRange(
                // Client web (SPA/MVC): authorization_code + PKCE, không secret
                new Client { ClientId = "fleet-web", Name = "Fleet Web App", RequirePkce = true, ClientSecretHash = null,
                    RedirectUris = "http://localhost:5000/callback,https://oidcdebugger.com/debug",
                    AllowedGrants = "authorization_code,refresh_token", AllowedScopes = "openid,profile,email,roles" },
                // Client dịch vụ (server-to-server): password + client_credentials, có secret
                new Client { ClientId = "fleet-service", Name = "Fleet Service (M2M)", RequirePkce = false,
                    ClientSecretHash = PasswordHasher.Hash("service-secret-demo"),
                    RedirectUris = "", AllowedGrants = "password,client_credentials,refresh_token", AllowedScopes = "openid,profile,email,roles,api" });
            await db.SaveChangesAsync();
        }
    }
}
