namespace KnowledgeHub.Shared.Contracts;

/// <summary>Auth surface DTOs (SPEC-20260914-auth-login §5).</summary>
public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Username, bool MustChangePassword);

public sealed record MeResponse(string Username, bool MustChangePassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CreateApiKeyRequest(string Name);

/// <summary>Listed key — never carries the secret.</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>Creation response — the only place the full `aft_*` secret appears.</summary>
public sealed record ApiKeyCreatedDto(Guid Id, string Name, string Prefix, string Key);

/// <summary>One audited request made with an `aft_*` key (SPEC-20260915-apikey-usage-audit §5).</summary>
public sealed record ApiKeyUsageEventDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string HttpMethod,
    string Path,
    int StatusCode,
    double DurationMs,
    string? UserAgent);

/// <summary>Per-key usage summary + recent audit events.</summary>
public sealed record ApiKeyUsageDto(
    int TotalCalls,
    int CallsLast24h,
    int CallsLast7d,
    double AvgDurationMs,
    int ErrorCount,
    double ErrorRate,
    List<ApiKeyUsageEventDto> RecentEvents);
