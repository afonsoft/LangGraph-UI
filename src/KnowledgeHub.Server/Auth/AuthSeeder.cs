using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// Idempotent admin seed (SPEC-20260914-auth-login RF-001): inserts `admin`
/// with the configured initial password only when the Users table is empty —
/// never overwrites an existing account. The password is hashed and never logged.
/// </summary>
public static class AuthSeeder
{
    public const string AdminUsername = "admin";

    public static async Task SeedAsync(
        KnowledgeHubDbContext db,
        IOptions<AuthOptions> options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
            return;

        var user = new AppUser
        {
            Username = AdminUsername,
            PasswordHash = "",
            MustChangePassword = true
        };
        user.PasswordHash = new PasswordService().Hash(user, options.Value.EffectiveAdminPassword);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded admin user — password change required on first login.");
    }
}
