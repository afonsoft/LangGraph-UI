using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Services;

/// <summary>Thread persistence (SPEC-20260914-conversation-threads RF-001).</summary>
public sealed class ConversationService(KnowledgeHubDbContext db) : IConversationService
{
    public async Task<IReadOnlyList<ThreadDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Threads
            .Select(t => new { t.Id, t.Title, t.CreatedAt, t.LastActivityAt, t.Summary, Count = t.Messages.Count })
            .ToListAsync(ct);
        // DateTimeOffset ordering does not translate on SQLite — sort in memory.
        return rows
            .OrderByDescending(t => t.LastActivityAt)
            .Select(t => new ThreadDto
            {
                Id = t.Id,
                Title = t.Title,
                CreatedAt = t.CreatedAt,
                LastActivityAt = t.LastActivityAt,
                MessageCount = t.Count,
                Summarized = t.Summary is not null
            }).ToList();
    }

    public async Task<ThreadDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.Threads.Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"thread '{id}' not found");
        var messages = t.Messages.OrderBy(m => m.CreatedAt).Select(ToMessageDto).ToList();
        return new ThreadDetailDto
        {
            Thread = new ThreadDto
            {
                Id = t.Id,
                Title = t.Title,
                CreatedAt = t.CreatedAt,
                LastActivityAt = t.LastActivityAt,
                MessageCount = messages.Count,
                Summarized = t.Summary is not null
            },
            Summary = t.Summary,
            Messages = messages
        };
    }

    public async Task<ThreadDto> CreateAsync(string? title, CancellationToken ct = default)
    {
        var thread = new ConversationThread { Title = string.IsNullOrWhiteSpace(title) ? "nova conversa" : title.Trim() };
        db.Threads.Add(thread);
        await db.SaveChangesAsync(ct);
        return new ThreadDto
        {
            Id = thread.Id,
            Title = thread.Title,
            CreatedAt = thread.CreatedAt,
            LastActivityAt = thread.LastActivityAt,
            MessageCount = 0,
            Summarized = false
        };
    }

    public async Task<ThreadDto> RenameAsync(Guid id, string title, CancellationToken ct = default)
    {
        var t = await db.Threads.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"thread '{id}' not found");
        t.Title = title.Trim();
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct)).Thread;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.Threads.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"thread '{id}' not found");
        db.Threads.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    private static ThreadMessageDto ToMessageDto(ConversationMessage m) => new()
    {
        Id = m.Id,
        Role = m.Role,
        Content = m.Content,
        ToolName = m.ToolName,
        CreatedAt = m.CreatedAt
    };
}
