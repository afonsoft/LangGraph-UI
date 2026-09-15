using KnowledgeHub.Server.Auth;
using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Mcp.Upstream;

namespace KnowledgeHub.Server.Configuration;

/// <summary>
/// SPEC-20260914-config-validation: fail-fast validation of the required config
/// blocks (<c>Embeddings</c>, <c>VectorStore</c>, <c>DeepWiki</c>) at startup —
/// before <c>Migrate()</c> — so operators get a clear error instead of deferred
/// "weird behavior".
/// </summary>
public static class ConfigurationValidator
{
    private static readonly HashSet<string> EmbeddingProviders = new(StringComparer.OrdinalIgnoreCase)
        { "deterministic", "ollama", "openai" };

    private static readonly HashSet<string> VectorStoreProviders = new(StringComparer.OrdinalIgnoreCase)
        { "sqlite", "postgres" };

    private static readonly HashSet<string> ChatProviders = new(StringComparer.OrdinalIgnoreCase)
        { "none", "ollama", "openai" };

    /// <summary>Throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static void Validate(IConfiguration configuration)
    {
        var problems = new List<string>();
        ValidateEmbeddings(configuration, problems);
        ValidateVectorStore(configuration, problems);
        ValidateDeepWiki(configuration, problems);
        ValidateChat(configuration, problems);
        ValidateAuth(configuration, problems);

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Invalid KnowledgeHub configuration:\n - " + string.Join("\n - ", problems));
    }

    private static void ValidateEmbeddings(IConfiguration cfg, List<string> problems)
    {
        var section = cfg.GetSection(EmbeddingOptions.SectionName);
        var provider = section["Provider"];
        if (!string.IsNullOrWhiteSpace(provider) && !EmbeddingProviders.Contains(provider))
            problems.Add($"Embeddings:Provider '{provider}' is invalid (expected: deterministic | ollama | openai)");

        if (provider is not null && !provider.Equals("deterministic", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = section["Endpoint"];
            if (!IsHttpUri(endpoint))
                problems.Add($"Embeddings:Endpoint '{endpoint}' is required and must be an absolute http(s) URI when Provider={provider}");
            if (string.IsNullOrWhiteSpace(section["Model"]))
                problems.Add($"Embeddings:Model is required when Provider={provider}");
        }

        if (section["Dimensions"] is { } dims && (!int.TryParse(dims, out var d) || d <= 0))
            problems.Add($"Embeddings:Dimensions '{dims}' must be a positive integer");
    }

    private static void ValidateVectorStore(IConfiguration cfg, List<string> problems)
    {
        var section = cfg.GetSection("VectorStore");
        var provider = section["Provider"];
        if (!string.IsNullOrWhiteSpace(provider) && !VectorStoreProviders.Contains(provider))
            problems.Add($"VectorStore:Provider '{provider}' is invalid (expected: sqlite | postgres)");

        if (provider?.Equals("postgres", StringComparison.OrdinalIgnoreCase) == true
            && string.IsNullOrWhiteSpace(section["ConnectionString"]))
            problems.Add("VectorStore:ConnectionString is required when VectorStore:Provider=postgres");
    }

    private static void ValidateDeepWiki(IConfiguration cfg, List<string> problems)
    {
        var section = cfg.GetSection(DeepWikiOptions.SectionName);
        if (section["Enabled"]?.Equals("false", StringComparison.OrdinalIgnoreCase) == true)
            return;

        if (section["Endpoint"] is { } endpoint && !IsHttpUri(endpoint))
            problems.Add($"DeepWiki:Endpoint '{endpoint}' must be an absolute http(s) URI");

        if (section["TimeoutSeconds"] is { } t && (!int.TryParse(t, out var ts) || ts <= 0))
            problems.Add($"DeepWiki:TimeoutSeconds '{t}' must be a positive integer");
    }

    private static void ValidateChat(IConfiguration cfg, List<string> problems)
    {
        var section = cfg.GetSection(ChatProviderOptions.SectionName);
        var provider = section["Provider"];
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("none", StringComparison.OrdinalIgnoreCase))
            return;
        if (!ChatProviders.Contains(provider))
        {
            problems.Add($"Chat:Provider '{provider}' is invalid (expected: none | ollama | openai)");
            return;
        }

        if (!IsHttpUri(section["Endpoint"]))
            problems.Add($"Chat:Endpoint '{section["Endpoint"]}' is required and must be an absolute http(s) URI when Provider={provider}");
        if (string.IsNullOrWhiteSpace(section["Model"]))
            problems.Add($"Chat:Model is required when Provider={provider}");

        if (section["TimeoutSeconds"] is { } t && (!int.TryParse(t, out var ts) || ts <= 0))
            problems.Add($"Chat:TimeoutSeconds '{t}' must be a positive integer");
        if (section["Temperature"] is { } temp && !double.TryParse(temp, out _))
            problems.Add($"Chat:Temperature '{temp}' must be a number");
        if (section["MaxTokens"] is { } mt && (!int.TryParse(mt, out var m) || m <= 0))
            problems.Add($"Chat:MaxTokens '{mt}' must be a positive integer");
    }

    private static void ValidateAuth(IConfiguration cfg, List<string> problems)
    {
        var section = cfg.GetSection(AuthOptions.SectionName);
        if (section["AdminInitialPassword"] is { } pw && string.IsNullOrWhiteSpace(pw))
            problems.Add("Auth:AdminInitialPassword must be non-empty when set");
        if (section["LockoutThreshold"] is { } t && (!int.TryParse(t, out var n) || n <= 0))
            problems.Add($"Auth:LockoutThreshold '{t}' must be a positive integer");
        if (section["LockoutMinutes"] is { } m && (!int.TryParse(m, out var mm) || mm <= 0))
            problems.Add($"Auth:LockoutMinutes '{m}' must be a positive integer");
        if (section["MinPasswordLength"] is { } l && (!int.TryParse(l, out var ll) || ll <= 0))
            problems.Add($"Auth:MinPasswordLength '{l}' must be a positive integer");
        if (section["SessionHours"] is { } h && (!int.TryParse(h, out var hh) || hh <= 0))
            problems.Add($"Auth:SessionHours '{h}' must be a positive integer");
    }

    private static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
