using System.Text.RegularExpressions;

namespace KnowledgeHub.Server.Security;

/// <summary>Suspicion categories reported by <see cref="IContentSanitizer"/>
/// (SPEC-20260923-prompt-injection-guard RF-002). Stored comma-separated on
/// <c>DocumentChunk.SuspicionFlags</c> — keep names stable, they persist.</summary>
public static class SuspicionFlag
{
    public const string InstructionOverride = "InstructionOverride";
    public const string RolePlayMarker = "RolePlayMarker";
    public const string FakeBoundaryTag = "FakeBoundaryTag";
    public const string EncodedPayload = "EncodedPayload";
    public const string ExcessiveMarkup = "ExcessiveMarkup";
}

/// <summary>Deterministic, offline heuristic detector for document-borne prompt
/// injection. Flag-only: never rewrites or deletes content.</summary>
public interface IContentSanitizer
{
    /// <summary>Returns the distinct flags that match <paramref name="text"/>;
    /// empty for clean content. Pure — no I/O, no LLM.</summary>
    IReadOnlyList<string> Scan(string text);
}

/// <summary>
/// Regex heuristics. Documented false-positive tolerance:
/// <list type="bullet">
/// <item><see cref="SuspicionFlag.InstructionOverride"/> — security runbooks that
///   *discuss* injection phrases will flag; exclusion is opt-out via config.</item>
/// <item><see cref="SuspicionFlag.EncodedPayload"/> — SHA-512 hashes, JWTs and
///   minified blobs ≥120 chars can flag.</item>
/// <item><see cref="SuspicionFlag.ExcessiveMarkup"/> — HTML/XML-heavy documents
///   with ≥30 tags can flag.</item>
/// </list>
/// </summary>
public sealed partial class ContentSanitizer : IContentSanitizer
{
    public IReadOnlyList<string> Scan(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var flags = new List<string>(5);
        if (InstructionOverrideRegex().IsMatch(text))
            flags.Add(SuspicionFlag.InstructionOverride);
        if (RolePlayRegex().IsMatch(text))
            flags.Add(SuspicionFlag.RolePlayMarker);
        if (FakeBoundaryRegex().IsMatch(text))
            flags.Add(SuspicionFlag.FakeBoundaryTag);
        if (EncodedPayloadRegex().IsMatch(text))
            flags.Add(SuspicionFlag.EncodedPayload);
        if (MarkupTagRegex().Matches(text).Count >= 30)
            flags.Add(SuspicionFlag.ExcessiveMarkup);
        return flags;
    }

    // "ignore/disregard/forget all previous instructions", "new instructions:",
    // "system:" at line start, "print/reveal your (system|initial) prompt",
    // pt-BR "ignore (todas )?as instruções anteriores".
    [GeneratedRegex(
        @"(?i)(ignore|disregard|forget|override)\s+(all\s+|any\s+|the\s+|as\s+|todas\s+as?\s+)?(previous|prior|above|earlier|anteriores)\s+(instructions?|directives?|rules?|prompts?|instruções)"
        + @"|(?i)new\s+instructions?\s*:|(?im)^\s*system\s*:"
        + @"|(?i)(print|reveal|output|repeat|show|include|attach|send|exfiltrate)\s+(your\s+|the\s+|as\s+the\s+)?(system|initial|full)\s+(prompt|instructions?)"
        + @"|(?i)ignore\s+(todas\s+)?as\s+instruções")]
    private static partial Regex InstructionOverrideRegex();

    // Persona hijack: "you are now", "act as", "pretend you are", DAN/jailbreak.
    [GeneratedRegex(
        @"(?i)\byou\s+are\s+now\b|\bact\s+as\s+(a|an|the|if)\b|\bpretend\s+(to\s+be|you\s+are|you're)\b"
        + @"|\bfrom\s+now\s+on\s+you\b|\bDAN\b|\bjailbreak\b")]
    private static partial Regex RolePlayRegex();

    // Forged boundary/system tags: our own delimiters plus common LLM control tokens.
    [GeneratedRegex(
        @"(?i)</?knowledge_chunk\b|</?tool_result\b|</?system\b|<<\s*SYS\s*>>|\[/?INST\]|<\|im_start\|>|<\|endoftext\|>")]
    private static partial Regex FakeBoundaryRegex();

    // Long opaque payloads: base64 runs ≥120 chars or hex runs ≥128.
    [GeneratedRegex(@"[A-Za-z0-9+/]{120,}={0,2}|\b[0-9a-fA-F]{128,}\b")]
    private static partial Regex EncodedPayloadRegex();

    [GeneratedRegex(@"</?[a-zA-Z][^>]{0,80}>")]
    private static partial Regex MarkupTagRegex();
}

/// <summary>
/// Delimiter helpers for untrusted content in prompts
/// (SPEC-20260923-prompt-injection-guard RF-001): chunk text and tool results
/// are wrapped in explicit boundary tags; any occurrence of those tags inside
/// the payload is escaped so boundaries cannot be forged.
/// </summary>
public static partial class PromptBoundary
{
    /// <summary>Escapes every boundary-tag open/close sequence so injected
    /// content cannot terminate or fake a boundary.</summary>
    public static string Escape(string text) =>
        BoundaryTagRegex().Replace(text, m => m.Value.Insert(1, " "));

    /// <summary>Wraps one retrieved chunk: <c>&lt;knowledge_chunk index="n"
    /// source="…" trust="untrusted" [flagged]&gt;…&lt;/knowledge_chunk&gt;</c>.</summary>
    public static string WrapChunk(int index, string source, string text, bool flagged)
    {
        var flag = flagged ? " flagged=\"true\"" : "";
        return $"<knowledge_chunk index=\"{index}\" source=\"{EscapeAttribute(source)}\" trust=\"untrusted\"{flag}>\n{Escape(text)}\n</knowledge_chunk>";
    }

    /// <summary>Wraps a tool result: <c>&lt;tool_result name="…"
    /// trust="untrusted"&gt;…&lt;/tool_result&gt;</c>.</summary>
    public static string WrapToolResult(string name, string text) =>
        $"<tool_result name=\"{EscapeAttribute(name)}\" trust=\"untrusted\">\n{Escape(text)}\n</tool_result>";

    private static string EscapeAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    [GeneratedRegex(@"(?i)</?(knowledge_chunk|tool_result)\b[^>]*>")]
    private static partial Regex BoundaryTagRegex();
}
