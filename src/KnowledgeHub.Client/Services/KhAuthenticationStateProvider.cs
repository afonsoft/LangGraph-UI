using System.Security.Claims;
using KnowledgeHub.Shared.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace KnowledgeHub.Client.Services;

/// <summary>
/// Authentication state backed by /api/auth/me (SPEC-20260914-auth-login
/// RF-011). The result is cached; login/logout/password flows call
/// <see cref="Invalidate"/> or <see cref="SetUser"/> to refresh consumers.
/// </summary>
public sealed class KhAuthenticationStateProvider(AuthApiClient auth) : AuthenticationStateProvider
{
    private MeResponse? _user;
    private bool _loaded;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var me = await GetUserAsync();
        var principal = me is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, me.Username)], authenticationType: "cookie"));
        return new AuthenticationState(principal);
    }

    /// <summary>Current user, or null when anonymous. Cached after the first call.</summary>
    public async Task<MeResponse?> GetUserAsync()
    {
        if (!_loaded)
        {
            _user = await auth.MeAsync();
            _loaded = true;
        }
        return _user;
    }

    /// <summary>Drop the cache and re-notify consumers (e.g., after logout or a 401).</summary>
    public void Invalidate()
    {
        _user = null;
        _loaded = false;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>Store a fresh identity (post-login / post-password-change) and notify.</summary>
    public void SetUser(MeResponse user)
    {
        _user = user;
        _loaded = true;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
