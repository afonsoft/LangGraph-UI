using System.Text.Json;
using KnowledgeHub.McpEngine.Activity;
using KnowledgeHub.Server.Api;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// Resolve/expire gated tool approvals (SPEC-20260914-hitl-tool-approval RF-001).
/// Emits ApprovalResolved activity events so the monitor feed reflects decisions.
/// </summary>
public sealed class ApprovalService(
    KnowledgeHubDbContext db,
    TimeSpan approvalTimeout,
    IMcpActivityFeed? feed = null) : IApprovalService
{
    public async Task<IReadOnlyList<ApprovalDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);
        var query = db.Approvals.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);
        // DateTimeOffset ordering does not translate on SQLite — sort in memory.
        var rows = (await query.ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAt).Take(200);
        return rows.Select(ToDto).ToList();
    }

    public Task<ApprovalDto> ApproveAsync(Guid id, JsonElement? approvedArgs, CancellationToken ct = default) =>
        ResolveAsync(id, "approved", approvedArgs, ct);

    public Task<ApprovalDto> DenyAsync(Guid id, CancellationToken ct = default) =>
        ResolveAsync(id, "denied", null, ct);

    private async Task<ApprovalDto> ResolveAsync(
        Guid id, string resolution, JsonElement? approvedArgs, CancellationToken ct)
    {
        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException($"approval '{id}' not found");

        ExpireIfOverdue(approval);
        if (approval.Status != "pending")
            throw new ConflictException($"approval '{id}' is already {approval.Status}");

        approval.Status = resolution;
        approval.ResolvedAt = DateTimeOffset.UtcNow;
        if (approvedArgs is { } args)
            approval.ApprovedArgsJson = MaskSensitive(args).GetRawText();
        await db.SaveChangesAsync(ct);

        feed?.Record(new McpActivityEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = McpActivityKind.ApprovalResolved,
            Transport = "ui",
            Method = "approvals",
            ToolName = approval.ToolName,
            Succeeded = resolution == "approved",
            Error = resolution == "approved" ? null : resolution
        });
        return ToDto(approval);
    }

    /// <summary>Lazily expires pending approvals older than the configured timeout.
    /// DateTimeOffset comparisons do not translate on SQLite — filter in memory.</summary>
    private async Task ExpireOverdueAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - approvalTimeout;
        var pending = await db.Approvals.Where(a => a.Status == "pending").ToListAsync(ct);
        var overdue = pending.Where(a => a.CreatedAt < cutoff).ToList();
        foreach (var a in overdue)
        {
            a.Status = "expired";
            a.ResolvedAt = DateTimeOffset.UtcNow;
        }
        if (overdue.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private void ExpireIfOverdue(ToolApproval approval)
    {
        if (approval.Status == "pending" && approval.CreatedAt + approvalTimeout < DateTimeOffset.UtcNow)
        {
            approval.Status = "expired";
            approval.ResolvedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Masks fields whose name contains key/token/secret/password before persisting.</summary>
    public static JsonElement MaskSensitive(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
            return args;
        var masked = new Dictionary<string, object?>();
        foreach (var p in args.EnumerateObject())
        {
            var sensitive = p.Name.Contains("key", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("password", StringComparison.OrdinalIgnoreCase);
            masked[p.Name] = sensitive ? "***" : p.Value.Clone();
        }
        return JsonSerializer.SerializeToElement(masked, JsonSerializerOptions.Web);
    }

    public static ApprovalDto ToDto(ToolApproval a) => new()
    {
        Id = a.Id,
        ToolName = a.ToolName,
        ArgumentsJson = a.ArgumentsJson,
        RequestedBy = a.RequestedBy,
        Status = a.Status,
        CreatedAt = a.CreatedAt,
        ResolvedAt = a.ResolvedAt,
        ResumedAt = a.ResumedAt
    };
}
