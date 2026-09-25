using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Search;

/// <summary>
/// Validated, typed form of <see cref="SearchFilter"/>
/// (SPEC-20260923-retrieval-quality RF-003). Resolution fails fast with a
/// friendly error for invalid enum/date values.
/// </summary>
public sealed record ResolvedSearchFilter(
    SourceType? SourceType,
    string? PathPrefix,
    DateTimeOffset? IndexedAfter,
    string? Language,
    string? Expansion = null,
    string? ContextExpand = null,
    bool? UseGraph = null)
{
    public bool IsEmpty =>
        SourceType is null && PathPrefix is null && IndexedAfter is null && Language is null;

    /// <summary>Stable fingerprint for the v2 result-cache key (RF-005).
    /// Expansion is part of result identity even when other filters are empty.</summary>
    public string Fingerprint() =>
        (IsEmpty ? "-" : $"{SourceType}|{PathPrefix}|{IndexedAfter:O}|{Language}")
        + (Expansion is null ? "" : $"|expand:{Expansion}")
        + (ContextExpand is null or "none" ? "" : $"|ctx:{ContextExpand}")
        + (UseGraph is null ? "" : $"|graph:{(UseGraph.Value ? 1 : 0)}");

    public static bool TryResolve(
        SearchFilter? filter, out ResolvedSearchFilter resolved, out string? error)
    {
        resolved = new ResolvedSearchFilter(null, null, null, null);
        error = null;
        if (filter is null)
            return true;

        SourceType? sourceType = null;
        if (filter.SourceType is { Length: > 0 } st)
        {
            if (!Enum.TryParse<SourceType>(st, ignoreCase: true, out var parsed))
            {
                error = $"invalid sourceType '{st}'";
                return false;
            }
            sourceType = parsed;
        }

        DateTimeOffset? indexedAfter = null;
        if (filter.IndexedAfter is { Length: > 0 } ia)
        {
            if (!DateTimeOffset.TryParse(ia, out var parsedDate))
            {
                error = $"invalid indexedAfter '{ia}' (expected ISO-8601)";
                return false;
            }
            indexedAfter = parsedDate;
        }

        // SPEC-20260924-query-expansion-hyde RF-003: per-call override.
        string? expansion = null;
        if (filter.Expand is { Length: > 0 } ex)
        {
            if (ex.ToLowerInvariant() is not ("off" or "multi" or "hyde" or "both"))
            {
                error = $"invalid expand '{ex}' (expected: off | multi | hyde | both)";
                return false;
            }
            expansion = ex.ToLowerInvariant();
        }

        // SPEC-20260924-hierarchical-retrieval RF-001: per-call hit-context override.
        string? contextExpand = null;
        if (filter.ContextExpand is { Length: > 0 } ce)
        {
            if (ce.ToLowerInvariant() is not ("none" or "window" or "section"))
            {
                error = $"invalid contextExpand '{ce}' (expected: none | window | section)";
                return false;
            }
            contextExpand = ce.ToLowerInvariant();
        }

        resolved = new ResolvedSearchFilter(
            sourceType,
            string.IsNullOrWhiteSpace(filter.PathPrefix) ? null : filter.PathPrefix,
            indexedAfter,
            string.IsNullOrWhiteSpace(filter.Language) ? null : filter.Language,
            expansion,
            contextExpand,
            filter.UseGraph);
        return true;
    }
}
