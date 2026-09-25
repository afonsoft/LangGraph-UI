namespace KnowledgeHub.Server.Ingestion;

/// <summary>
/// SPEC-20260926-sync-error-diagnostics RF-001: compact "{Type}: {base message}"
/// digest for persisted error fields (LastError/job.Error/Reason) — unwraps to
/// the innermost exception so a <c>DbUpdateException</c> surfaces the real cause
/// (e.g. the UNIQUE violation) instead of the generic EF wrapper text.
/// </summary>
internal static class ExceptionDigest
{
    private const int MaxLength = 500;

    internal static string Describe(Exception ex)
    {
        var b = ex.GetBaseException();
        var m = $"{b.GetType().Name}: {b.Message}"
            .Replace('\n', ' ')
            .Replace('\r', ' ');
        return m.Length > MaxLength ? m[..MaxLength] : m;
    }
}
