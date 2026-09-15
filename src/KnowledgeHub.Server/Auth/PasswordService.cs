using KnowledgeHub.Server.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// PBKDF2 password hashing via the ASP.NET Core shared framework's
/// <see cref="PasswordHasher{TUser}"/> (SPEC-20260914-auth-login RF-002).
/// Salted hashes; plaintext is never stored or logged.
/// </summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(AppUser user, string password) =>
        _hasher.HashPassword(user, password);

    public bool Verify(AppUser user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}
