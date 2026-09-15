using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// Path-safe vault file access shared by read_document / write_note / write_knowledge
/// (SPEC-04 RF-003). All paths are confined to the vault root: relative only, no
/// <c>..</c> traversal, canonical path must stay under the root.
/// </summary>
public static class ObsidianNoteWriter
{
    /// <summary>Resolve an active ObsidianVault source by slug, or the first one.</summary>
    public static async Task<KnowledgeHub.Server.Domain.Entities.KnowledgeSource?> ResolveVaultAsync(
        KnowledgeHubDbContext db, string? sourceSlug, CancellationToken ct)
    {
        var vaults = await db.Sources
            .Where(s => s.IsActive && s.SourceType == SourceType.ObsidianVault)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        if (vaults.Count == 0)
            return null;
        if (sourceSlug is null)
            return vaults[0];

        var slugs = ToolSlugger.Assign(vaults.Select(v => (v.Id, v.Name)));
        var match = vaults.FirstOrDefault(v => slugs[v.Id] == sourceSlug);
        return match;
    }

    /// <summary>
    /// SPEC-20260915-sources-edit-dialog RF-002: per-source write guard —
    /// true when the source configuration carries <c>"readOnly": true</c>.
    /// Absent/malformed flag means writable.
    /// </summary>
    public static bool IsReadOnly(Domain.Entities.KnowledgeSource source)
    {
        if (string.IsNullOrEmpty(source.ConfigurationJson))
            return false;
        try
        {
            return JsonNode.Parse(source.ConfigurationJson)?["readOnly"] is JsonValue value
                && value.TryGetValue<bool>(out var readOnly) && readOnly;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Map a relative vault path to a canonical full path, or throw InvalidParams.</summary>
    public static string SafePath(string vaultRoot, string relativePath, bool forWrite)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new McpProtocolException("argument 'path' must not be empty", McpErrorCode.InvalidParams);

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(s => s == ".."))
            throw new McpProtocolException("path traversal ('..') is not allowed", McpErrorCode.InvalidParams);

        if (forWrite && !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        var full = Path.GetFullPath(Path.Combine(vaultRoot, normalized));
        var rootWithSep = vaultRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, vaultRoot, StringComparison.OrdinalIgnoreCase))
            throw new McpProtocolException("path escapes the vault root", McpErrorCode.InvalidParams);

        return full;
    }

    /// <summary>Slug a note title into a safe filename (no extension).</summary>
    public static string TitleToFileName(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim())
            sb.Append(char.IsLetterOrDigit(ch) || ch is ' ' or '-' ? ch : '-');
        var name = sb.ToString().Trim().Replace(' ', '-');
        while (name.Contains("--", StringComparison.Ordinal))
            name = name.Replace("--", "-", StringComparison.Ordinal);
        return name.Length == 0 ? "untitled" : name;
    }

    /// <summary>Build note content with YAML frontmatter when tags are provided.</summary>
    public static string WithFrontmatter(string content, string[]? tags)
    {
        if (tags is not { Length: > 0 })
            return content;
        var fm = new StringBuilder("---\ntags:");
        foreach (var tag in tags)
            fm.Append("\n  - ").Append(tag.Replace('"', ' ').Trim());
        fm.Append("\n---\n\n");
        return fm + content;
    }
}
