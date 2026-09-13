using System.Text;

namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// Turns source names into stable MCP-safe slugs (SPEC-04 edge cases):
/// lowercase, non-alphanumeric → '_', collisions get '_2', '_3', ...
/// </summary>
public static class ToolSlugger
{
    public static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');

        var slug = sb.ToString();
        while (slug.Contains("__", StringComparison.Ordinal))
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        slug = slug.Trim('_');
        return slug.Length == 0 ? "source" : slug;
    }

    /// <summary>Assign unique slugs preserving source order; returns sourceId → slug.</summary>
    public static IReadOnlyDictionary<Guid, string> Assign(IEnumerable<(Guid Id, string Name)> sources)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<Guid, string>();
        foreach (var (id, name) in sources)
        {
            var slug = Slugify(name);
            if (used.TryGetValue(slug, out var count))
            {
                used[slug] = count + 1;
                slug = $"{slug}_{count + 1}";
            }
            used[slug] = 1;
            result[id] = slug;
        }
        return result;
    }
}
