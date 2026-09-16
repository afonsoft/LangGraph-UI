namespace KnowledgeHub.Server.Settings;

/// <summary>Well-known provider slugs for the integration secret store.</summary>
public static class IntegrationProviders
{
    public const string Firecrawl = "firecrawl";
    public const string DeepWiki = "deepwiki";
    public const string Tavily = "tavily";
    /// <summary>Chat API key slug (SPEC-20260916-settings-chat-config) — deliberately
    /// kept out of <see cref="All"/>: it is managed by the /api/settings/chat
    /// endpoints, never listed in the integrations grid.</summary>
    public const string Chat = "chat";

    public static readonly IReadOnlyList<string> All = [Firecrawl, DeepWiki, Tavily];
}

/// <summary>
/// Reversible, encrypted-at-rest store for upstream integration credentials
/// (SPEC-20260916-firecrawl-mcp-proxy RF-004). Secrets are protected with
/// ASP.NET Core Data Protection before hitting SQLite; reads never expose
/// more than the stored last-4 hint.
/// </summary>
public interface IIntegrationSecretStore
{
    /// <summary>Decrypted secret for <paramref name="provider"/>, or null when
    /// not stored. Returns null (with a logged warning) if the DB is
    /// unavailable, so env/config fallbacks still apply.</summary>
    Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>Display metadata — never the secret itself.</summary>
    Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>Encrypts and upserts the secret; records the last-4 hint.</summary>
    Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored secret. Returns true when one existed.</summary>
    Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default);
}

/// <summary>Masked view of a stored integration secret.</summary>
public sealed record IntegrationSecretInfo(string Provider, string KeyHint, DateTimeOffset UpdatedAt);
