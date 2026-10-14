using AgenticEngineering.Domain;
using AgenticEngineering.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AgenticEngineering.Infrastructure.ShortUrls;

public sealed class ShortUrlDbContext(DbContextOptions<ShortUrlDbContext> options) : DbContext(options)
{
    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();

    public DbSet<UrlClick> UrlClicks => Set<UrlClick>();

    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();

    public DbSet<WorkflowAuditEvent> WorkflowAuditEvents => Set<WorkflowAuditEvent>();

    public DbSet<WorkflowApprovalRequest> WorkflowApprovalRequests => Set<WorkflowApprovalRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortUrl>(entity =>
        {
            entity.HasKey(shortUrl => shortUrl.Code);
            entity.Property(shortUrl => shortUrl.Code).HasMaxLength(32);
            entity.Property(shortUrl => shortUrl.OriginalUrl).HasMaxLength(2048).IsRequired();
            entity.Property(shortUrl => shortUrl.ExpiresAtUtc);
            entity.HasMany(shortUrl => shortUrl.Clicks)
                .WithOne()
                .HasForeignKey(click => click.ShortUrlCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UrlClick>(entity =>
        {
            entity.HasKey(click => click.Id);
            entity.HasIndex(click => click.ShortUrlCode);
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.HasKey(run => run.Id);
            entity.Property(run => run.OriginalRequirement).HasMaxLength(8000).IsRequired();
            entity.HasIndex(run => run.CreatedAtUtc);
        });

        modelBuilder.Entity<WorkflowTask>(entity =>
        {
            entity.HasKey(task => task.Id);
            entity.HasIndex(task => task.RunId);
            entity.Property(task => task.DependenciesJson).IsRequired();
            entity.Property(task => task.InputArtifactsJson).IsRequired();
            entity.Property(task => task.OutputArtifactsJson).IsRequired();
            entity.Property(task => task.OutputJson);
            entity.Property(task => task.Error);
        });

        modelBuilder.Entity<WorkflowAuditEvent>(entity =>
        {
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.HasIndex(auditEvent => new { auditEvent.RunId, auditEvent.OccurredAtUtc });
            entity.Property(auditEvent => auditEvent.EventType).HasMaxLength(80).IsRequired();
            entity.Property(auditEvent => auditEvent.Detail).HasMaxLength(8000).IsRequired();
        });

        modelBuilder.Entity<WorkflowApprovalRequest>(entity =>
        {
            entity.HasKey(approval => approval.Id);
            entity.HasIndex(approval => new { approval.RunId, approval.State });
            entity.Property(approval => approval.Description).HasMaxLength(2000).IsRequired();
            entity.Property(approval => approval.Reviewer).HasMaxLength(200);
            entity.Property(approval => approval.DecisionNotes).HasMaxLength(2000);
        });
    }
}