using KnowledgeHub.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Data;

public sealed class KnowledgeHubDbContext(DbContextOptions<KnowledgeHubDbContext> options) : DbContext(options)
{
    public DbSet<KnowledgeSource> Sources => Set<KnowledgeSource>();
    public DbSet<KnowledgeDocument> Documents => Set<KnowledgeDocument>();
    public DbSet<DocumentChunk> Chunks => Set<DocumentChunk>();
    public DbSet<ToolApproval> Approvals => Set<ToolApproval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgeSource>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(s => s.Name).IsUnique();
            e.Property(s => s.SourceType).HasConversion<string>().HasMaxLength(32);
            e.HasMany(s => s.Documents)
                .WithOne(d => d.Source)
                .HasForeignKey(d => d.KnowledgeSourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeDocument>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Title).IsRequired().HasMaxLength(500);
            e.Property(d => d.UriReference).IsRequired().HasMaxLength(1000);
            e.HasIndex(d => new { d.KnowledgeSourceId, d.UriReference }).IsUnique();
            e.HasMany(d => d.Chunks)
                .WithOne(c => c.Document)
                .HasForeignKey(c => c.KnowledgeDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ToolApproval>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.ToolName).IsRequired().HasMaxLength(200);
            e.Property(a => a.RequestedBy).IsRequired().HasMaxLength(32);
            e.Property(a => a.Status).IsRequired().HasMaxLength(16);
            e.HasIndex(a => a.Status);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.TextContent).IsRequired();
            e.Property(c => c.Embedding).HasColumnType("BLOB");
        });
    }
}
