namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Local user account (SPEC-20260914-auth-login RF-001). Single seeded `admin`
/// today; the table is generic for future multi-user support. PasswordHash is a
/// PBKDF2 salted hash (PasswordService) — plaintext never persists.
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    /// <summary>First-access gate: only /api/auth/{me,logout,change-password} pass while true.</summary>
    public bool MustChangePassword { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ApiKey> ApiKeys { get; set; } = [];
}
