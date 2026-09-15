using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using KnowledgeHub.Shared.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnowledgeHub.Tests.Integration;

/// <summary>
/// Authenticated-client helper (SPEC-20260914-auth-login RF-013): logs in as the
/// seeded admin and clears the first-access password gate, returning a cookie
/// client whose requests pass the Operational policy.
///
/// The password rotation happens once per fixture DB — subsequent calls log in
/// with the rotated password so repeated logins never accumulate lockout
/// failures.
/// </summary>
public static class TestAuth
{
    public const string AdminUsername = "admin";
    public const string AdminInitialPassword = "123qwe";
    public const string NewPassword = "newpass-123";

    private static readonly ConcurrentDictionary<WebApplicationFactory<Program>, byte> Rotated = new();

    /// <summary>Sync variant for fixture constructors.</summary>
    public static HttpClient Login(WebApplicationFactory<Program> factory) =>
        LoginAsync(factory).GetAwaiter().GetResult();

    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(); // HandleCookies=true by default

        if (Rotated.ContainsKey(factory))
        {
            (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(AdminUsername, NewPassword))).EnsureSuccessStatusCode();
            return client;
        }

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(AdminUsername, AdminInitialPassword));
        if (login.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The password was already rotated for this fixture DB.
            Rotated[factory] = 1;
            (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(AdminUsername, NewPassword))).EnsureSuccessStatusCode();
            return client;
        }

        login.EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(AdminInitialPassword, NewPassword))).EnsureSuccessStatusCode();
        Rotated[factory] = 1;
        return client;
    }

    /// <summary>Creates an API key through the management endpoint; returns the secret.</summary>
    public static async Task<string> CreateApiKeyAsync(HttpClient cookieClient, string name = "test")
    {
        var response = await cookieClient.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest(name));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();
        return dto!.Key;
    }
}
