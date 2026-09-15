using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.Client.Services;

/// <summary>
/// DelegatingHandler on the shared HttpClient (SPEC-20260914-auth-login
/// RF-011): a 401 on a non-auth endpoint invalidates the cached identity and
/// routes to /login; a 403 `password_change_required` routes to
/// /change-password. The state provider is resolved lazily — it depends on the
/// HttpClient this handler wraps, so constructor injection would be a cycle.
/// </summary>
public sealed class AuthRedirectHandler(NavigationManager nav, IServiceProvider services) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.StartsWith("/api/auth", StringComparison.Ordinal))
            return response;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            services.GetRequiredService<KhAuthenticationStateProvider>().Invalidate();
            if (!nav.Uri.Contains("/login", StringComparison.Ordinal))
                nav.NavigateTo("/login");
        }
        else if (response.StatusCode == HttpStatusCode.Forbidden
                 && (await response.Content.ReadAsStringAsync(cancellationToken))
                     .Contains("password_change_required", StringComparison.Ordinal)
                 && !nav.Uri.Contains("/change-password", StringComparison.Ordinal))
        {
            nav.NavigateTo("/change-password");
        }

        return response;
    }
}
