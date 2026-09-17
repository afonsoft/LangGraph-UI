using KnowledgeHub.McpEngine;
using KnowledgeHub.Server;
using KnowledgeHub.Server.Api;
using KnowledgeHub.Server.Auth;
using KnowledgeHub.Server.Configuration;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Health;
using KnowledgeHub.Server.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// SPEC-20260914-config-validation: fail fast on invalid config before any work.
ConfigurationValidator.Validate(builder.Configuration);

// SPEC-20260914-graceful-shutdown: bounded drain window for in-flight requests.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout =
    TimeSpan.FromSeconds(builder.Configuration.GetValue("Host:ShutdownTimeoutSeconds", 30)));

// SPEC-20260914-error-handling: RFC 7807 ProblemDetails for unhandled errors.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// SPEC-20260914-health-checks: /health/live + /health/ready.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<EmbeddingHealthCheck>("embeddings", tags: ["ready"])
    .AddCheck<IngestionHealthCheck>("ingestion", tags: ["ready"]);

builder.Services.AddKnowledgeHubServer(builder.Configuration);
builder.Services.AddKnowledgeHubMcp(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHostedService<McpActivityBroadcastService>();

// SPEC-20260914-auth-login: cookie session (browser SPA) + aft_* API keys
// (non-browser MCP/API/hub clients). Secure=SameAsRequest keeps dev/test over
// plain http working while production (https) always gets Secure cookies.
builder.Services.AddOptions<AuthOptions>()
    .Configure<IConfiguration>((options, cfg) =>
        cfg.GetSection(AuthOptions.SectionName).Bind(options));
builder.Services.AddSingleton<PasswordService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(
            builder.Configuration.GetValue("Auth:SessionHours", 12));
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(AuthPolicies.Authenticated, p => p
        .AddAuthenticationSchemes(AuthPolicies.AnyScheme)
        .RequireAuthenticatedUser());
    o.AddPolicy(AuthPolicies.Operational, p => p
        .AddAuthenticationSchemes(AuthPolicies.AnyScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new PasswordChangedRequirement()));
    o.AddPolicy(AuthPolicies.CookieSession, p => p
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new PasswordChangedRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, PasswordChangedHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, PasswordGateResultHandler>();

var app = builder.Build();

// SPEC-20260916-redis-exposure-risk RF-002: non-fatal config warnings (e.g.
// Redis without auth) — surfaced once at startup, never block the host.
foreach (var warning in ConfigurationValidator.CollectWarnings(app.Configuration))
    app.Logger.LogWarning("Configuration warning: {Warning}", warning);

// RF-005: apply pending migrations and log the path.
// SPEC-06 RF-002: default location is beside the executable; overridable via
// KnowledgeHub:DatabasePath / Database:Path. Clear error on read-only dirs.
// SPEC-20260914-efcore-migrations: Migrate() + baseline for EnsureCreated-era DBs.
DatabasePath.EnsureDirectory(app.Configuration);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
    await DatabaseMigrator.MigrateAsync(db, app.Logger);
    app.Logger.LogInformation("KnowledgeHub database ready at {Path}",
        DatabasePath.Resolve(app.Configuration));

    // SPEC-20260914-auth-login RF-001: seed admin on empty Users table.
    await AuthSeeder.SeedAsync(
        db, scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>(), app.Logger);

    // SPEC-20260914-embedding-dimension-guard: loud startup warning when the
    // persisted embeddings no longer match the configured provider.
    var embeddingProvider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
    try
    {
        await EmbeddingCompatibilityCheck.RunAsync(db, embeddingProvider, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Embedding compatibility check failed — continuing startup.");
    }
}

// SPEC-05 RF-005: serve the hosted WASM client + deep-link fallback.
// MapStaticAssets resolves the #[.{fingerprint}] tokens in index.html to the
// fingerprinted asset names (UseStaticFiles would serve the literal token).
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// SPEC-20260915-apikey-usage-audit RF-002: audit every request whose principal
// authenticated via an aft_* API key (needs the post-auth claims).
app.UseMiddleware<KnowledgeHub.Server.Auth.ApiKeyUsageMiddleware>();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

// SPEC-20260915-boot-cache-revalidation RF-001: the mutable boot chain
// (index.html fallback, boot.js, the unfingerprinted blazor.webassembly.js /
// dotnet.js / dotnet.boot.js that the loader imports) must revalidate on every
// navigation — otherwise a stale cached copy keeps pointing at immutable-cached
// old fingerprints and a deploy never reaches the browser.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var bootShell = path is "/js/boot.js"
        or "/_framework/blazor.webassembly.js"
        or "/_framework/dotnet.js"
        or "/_framework/dotnet.boot.js"
        || (!Path.HasExtension(path)
            && !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/framework-assets/", StringComparison.OrdinalIgnoreCase));
    if (bootShell)
        context.Response.OnStarting(static state =>
        {
            ((HttpResponse)state).Headers.CacheControl = "no-cache";
            return Task.CompletedTask;
        }, context.Response);
    await next();
});

app.MapStaticAssets();

// SPEC-20260915-wasm-boot-proxy-fix RF-004: extensionless mirror of
// _framework binaries — anonymous static content, same trust level as
// MapStaticAssets, so the WASM boot survives proxies that block by extension.
app.MapFrameworkAssetsApi();

// SPEC-20260914-auth-login RF-006: everything operational requires an
// authenticated principal that has cleared the password-change gate.
// Public: /api/auth/login, /health/*, static assets + SPA fallback.
app.MapAuthApi();
app.MapApiKeysApi();
app.MapSourcesApi().RequireAuthorization(AuthPolicies.Operational);
app.MapSearchApi().RequireAuthorization(AuthPolicies.Operational);
app.MapAskApi().RequireAuthorization(AuthPolicies.Operational);
app.MapAgentApi().RequireAuthorization(AuthPolicies.Operational);
app.MapApprovalsApi().RequireAuthorization(AuthPolicies.Operational);
app.MapThreadsApi().RequireAuthorization(AuthPolicies.Operational);
app.MapStreamingApi(); // RequireAuthorization applied per-endpoint inside (returns void)
app.MapToolsApi().RequireAuthorization(AuthPolicies.Operational);
app.MapSettingsApi().RequireAuthorization(AuthPolicies.Operational);
app.MapApiKeySettingsApi().RequireAuthorization(AuthPolicies.Operational);
app.MapKnowledgeHubMcp().RequireAuthorization(AuthPolicies.Operational);
app.MapHub<McpMonitorHub>("/hubs/mcp").RequireAuthorization(AuthPolicies.Operational);
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
