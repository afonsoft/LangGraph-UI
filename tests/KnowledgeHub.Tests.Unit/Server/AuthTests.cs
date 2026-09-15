using KnowledgeHub.Server.Auth;
using KnowledgeHub.Server.Domain.Entities;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260914-auth-login RF-001/RF-002/RF-007/RF-009 (hashing, lockout
// transitions, password policy) and RF-004 (aft_* key format + hash lookup).
public class AuthTests
{
    private static AppUser NewUser(string password = "correct-horse") => new()
    {
        Username = "admin",
        PasswordHash = new PasswordService().Hash(new AppUser { Username = "admin", PasswordHash = "" }, password)
    };

    // --- RF-002: hashing ---------------------------------------------------

    [Fact]
    public void Hash_ProducesSaltedHash_Verifiable()
    {
        var service = new PasswordService();
        var user = new AppUser { Username = "admin", PasswordHash = "" };

        var hash = service.Hash(user, "123qwe");

        Assert.NotEqual("123qwe", hash);
        Assert.True(service.Verify(new AppUser { Username = "admin", PasswordHash = hash }, "123qwe"));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var user = NewUser();
        Assert.False(new PasswordService().Verify(user, "wrong-password"));
    }

    [Fact]
    public void Hash_SamePasswordTwice_DifferentSalts()
    {
        var service = new PasswordService();
        var user = new AppUser { Username = "admin", PasswordHash = "" };
        Assert.NotEqual(service.Hash(user, "123qwe"), service.Hash(user, "123qwe"));
    }

    // --- RF-007: lockout transitions ---------------------------------------

    [Fact]
    public void RegisterFailure_FifthFailure_LocksForFiveMinutes()
    {
        var user = NewUser();
        var options = new AuthOptions();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < options.LockoutThreshold; i++)
            AuthRules.RegisterFailure(user, now, options);

        Assert.Equal(0, user.FailedAttempts); // counter resets once locked
        Assert.Equal(now + TimeSpan.FromMinutes(options.LockoutMinutes), user.LockoutUntil);
        Assert.True(AuthRules.IsLockedOut(user, now.AddMinutes(4)));
        Assert.False(AuthRules.IsLockedOut(user, now.AddMinutes(6)));
    }

    [Fact]
    public void RegisterFailure_BelowThreshold_DoesNotLock()
    {
        var user = NewUser();
        var options = new AuthOptions();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < options.LockoutThreshold - 1; i++)
            AuthRules.RegisterFailure(user, now, options);

        Assert.Null(user.LockoutUntil);
        Assert.Equal(options.LockoutThreshold - 1, user.FailedAttempts);
    }

    [Fact]
    public void RegisterSuccess_ClearsFailuresAndLockout()
    {
        var user = NewUser();
        user.FailedAttempts = 4;
        user.LockoutUntil = DateTimeOffset.UtcNow.AddMinutes(5);

        AuthRules.RegisterSuccess(user);

        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockoutUntil);
    }

    // --- RF-009: password policy -------------------------------------------

    [Fact]
    public void ValidateNewPassword_ShorterThanMinimum_Fails()
        => Assert.NotNull(AuthRules.ValidateNewPassword("old-password", "short", new AuthOptions()));

    [Fact]
    public void ValidateNewPassword_SameAsCurrent_Fails()
        => Assert.NotNull(AuthRules.ValidateNewPassword("old-password", "old-password", new AuthOptions()));

    [Fact]
    public void ValidateNewPassword_Valid_ReturnsNull()
        => Assert.Null(AuthRules.ValidateNewPassword("old-password", "brand-new-1", new AuthOptions()));

    // --- RF-004: aft_* keys -------------------------------------------------

    [Fact]
    public void GenerateKey_Format_IsAftPlus32Hex()
    {
        var key = ApiKeyService.GenerateKey();
        Assert.StartsWith("aft_", key);
        Assert.Equal(36, key.Length);
        Assert.Matches("^aft_[0-9a-f]{32}$", key);
    }

    [Fact]
    public void HashKey_IsDeterministicSha256Hex()
    {
        var a = ApiKeyService.HashKey("aft_" + new string('a', 32));
        Assert.Equal(a, ApiKeyService.HashKey("aft_" + new string('a', 32)));
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Prefix_IsFirstTwelveChars()
        => Assert.Equal(12, ApiKeyService.PrefixOf(ApiKeyService.GenerateKey()).Length);
}
