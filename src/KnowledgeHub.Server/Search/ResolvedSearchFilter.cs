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
    string? Language)
{
    public bool IsEmpty =>
        SourceType is null && PathPrefix is null && IndexedAfter is null && Language is null;

    /// <summary>Stable fingerprint for the v2 result-cache key (RF-005).</summary>
    public string Fingerprint() => IsEmpty
        ? "-"
        : $"{SourceType}|{PathPrefix}|{IndexedAfter:O}|{Language}";

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

        resolved = new ResolvedSearchFilter(
            sourceType,
            string.IsNullOrWhiteSpace(filter.PathPrefix) ? null : filter.PathPrefix,
            indexedAfter,
            string.IsNullOrWhiteSpace(filter.Language) ? null : filter.Language);
        return true;
    }
}
