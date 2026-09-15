using System.Security.Claims;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// /api/auth surface (SPEC-20260914-auth-login RF-007/RF-008/RF-009).
/// `login` is anonymous; `me`/`logout`/`change-password` require an
/// authenticated principal and are exempt from the password-change gate.
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapGet("/me", MeAsync).RequireAuthorization(AuthPolicies.Authenticated);
        group.MapPost("/logout", (Delegate)LogoutAsync).RequireAuthorization(AuthPolicies.Authenticated);
        group.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization(AuthPolicies.Authenticated);

        return group;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        KnowledgeHubDbContext db,
        PasswordService passwords,
        IOptions<AuthOptions> options,
        CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

        // Same code path for unknown users — verify a fixed dummy hash so the
        // response time doesn't reveal whether the username exists.
        var passwordOk = user is not null
            ? passwords.Verify(user, request.Password)
            : passwords.Verify(DummyUser, request.Password);

        if (user is not null && AuthRules.IsLockedOut(user, DateTimeOffset.UtcNow))
        {
            var retry = Math.Max(1,
                (int)(user.LockoutUntil!.Value - DateTimeOffset.UtcNow).TotalSeconds);
            return Results.Json(
                new { error = "conta bloqueada", retryAfterSeconds = retry },
                statusCode: StatusCodes.Status423Locked);
        }

        if (user is null || !passwordOk)
        {
            if (user is not null)
            {
                AuthRules.RegisterFailure(user, DateTimeOffset.UtcNow, options.Value);
                await db.SaveChangesAsync(ct);
            }
            return Results.Json(
                new { error = "credenciais inválidas" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        AuthRules.RegisterSuccess(user);
        await db.SaveChangesAsync(ct);
        await SignInAsync(http, user);

        return Results.Ok(new LoginResponse(user.Username, user.MustChangePassword));
    }

    private static IResult MeAsync(HttpContext http) =>
        Results.Ok(new MeResponse(
            http.User.FindFirst(ClaimTypes.Name)?.Value ?? "",
            http.User.FindFirst(ApiKeyAuthenticationHandler.PasswordChangedClaim)?.Value != "true"));

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext http,
        KnowledgeHubDbContext db,
        PasswordService passwords,
        IOptions<AuthOptions> options,
        CancellationToken ct)
    {
        // Cookie sessions only — a leaked API key must not rotate the password.
        if (http.User.FindFirst(ApiKeyAuthenticationHandler.AuthMethodClaim)?.Value == "apikey")
            return Results.Json(new { error = "senha só pode ser alterada numa sessão de navegador" },
                statusCode: StatusCodes.Status403Forbidden);

        var id = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = Guid.TryParse(id, out var guid)
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == guid, ct)
            : null;
        if (user is null)
            return Results.Unauthorized();

        if (!passwords.Verify(user, request.CurrentPassword))
            return Results.BadRequest(new { error = "senha atual incorreta" });

        if (AuthRules.ValidateNewPassword(request.CurrentPassword, request.NewPassword, options.Value)
                is { } violation)
            return Results.BadRequest(new { error = violation });

        user.PasswordHash = passwords.Hash(user, request.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        // Re-issue the cookie with pwd_changed=true so the gate opens.
        await SignInAsync(http, user);
        return Results.NoContent();
    }

    private static Task SignInAsync(HttpContext http, AppUser user) =>
        http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ApiKeyAuthenticationHandler.PasswordChangedClaim,
                    user.MustChangePassword ? "false" : "true")
            ], CookieAuthenticationDefaults.AuthenticationScheme)));

    // Fixed account used only to equalize verify timing on unknown usernames.
    private static readonly AppUser DummyUser = CreateDummy();

    private static AppUser CreateDummy()
    {
        var user = new AppUser { Username = "dummy", PasswordHash = "" };
        user.PasswordHash = new PasswordService().Hash(user, "dummy-password");
        return user;
    }
}
