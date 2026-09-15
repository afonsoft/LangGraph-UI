using KnowledgeHub.Server.Domain.Entities;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// Lockout transitions and password policy (SPEC-20260914-auth-login
/// RF-007/RF-009) — pure functions over <see cref="AppUser"/> so the rules are
/// unit-testable without HTTP.
/// </summary>
public static class AuthRules
{
    public static bool IsLockedOut(AppUser user, DateTimeOffset now) =>
        user.LockoutUntil is { } until && until > now;

    /// <summary>Increments failures; at the threshold the counter resets and the lock window opens.</summary>
    public static void RegisterFailure(AppUser user, DateTimeOffset now, AuthOptions options)
    {
        user.FailedAttempts++;
        if (user.FailedAttempts >= options.LockoutThreshold)
        {
            user.FailedAttempts = 0;
            user.LockoutUntil = now.AddMinutes(options.LockoutMinutes);
        }
    }

    public static void RegisterSuccess(AppUser user)
    {
        user.FailedAttempts = 0;
        user.LockoutUntil = null;
    }

    /// <summary>Returns the violated rule, or null when the new password is acceptable.</summary>
    public static string? ValidateNewPassword(string currentPassword, string newPassword, AuthOptions options)
    {
        if (newPassword.Length < options.MinPasswordLength)
            return $"senha deve ter ≥{options.MinPasswordLength} caracteres";
        if (newPassword == currentPassword)
            return "nova senha igual à atual";
        return null;
    }
}
