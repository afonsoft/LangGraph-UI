namespace KnowledgeHub.Server.Search;

/// <summary>
/// Turns free-text queries into safe FTS5 MATCH expressions
/// (SPEC-20260914-hybrid-retrieval NFR): every term becomes a double-quoted
/// phrase joined by OR — operators, wildcards and unbalanced quotes from the
/// caller can never reach the FTS parser as syntax.
/// </summary>
public static class FtsQuerySanitizer
{
    /// <summary>Returns a MATCH expression, or null when the query has no searchable terms.</summary>
    public static string? ToMatchExpression(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => $"\"{t.Replace("\"", "\"\"")}\"")
            .ToList();

        return terms.Count == 0 ? null : string.Join(" OR ", terms);
    }
}
