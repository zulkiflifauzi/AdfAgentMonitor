using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdfAgentMonitor.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PipelineRunState> PipelineRunStates => Set<PipelineRunState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PipelineRunState>(entity =>
        {
            entity.ToTable("PipelineRunStates");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .ValueGeneratedNever(); // Guid assigned by application

            entity.Property(e => e.PipelineRunId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.HasIndex(e => e.PipelineRunId)
                  .IsUnique()
                  .HasDatabaseName("UX_PipelineRunStates_PipelineRunId");

            entity.Property(e => e.PipelineName)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(e => e.FactoryName)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(e => e.Status)
                  .IsRequired()
                  .HasConversion<string>()
                  .HasMaxLength(64);

            entity.Property(e => e.FailedAt)
                  .IsRequired(false);

            entity.Property(e => e.DiagnosisCode)
                  .IsRequired(false)
                  .HasConversion<string>()
                  .HasMaxLength(64);

            entity.Property(e => e.DiagnosisSummary)
                  .IsRequired(false)
                  .HasMaxLength(4000);

            entity.Property(e => e.RemediationPlan)
                  .IsRequired(false)
                  .HasColumnType("nvarchar(max)");

            entity.Property(e => e.RemediationRisk)
                  .IsRequired(false)
                  .HasConversion<string>()
                  .HasMaxLength(32);

            entity.Property(e => e.ApprovalStatus)
                  .IsRequired(false)
                  .HasMaxLength(32);

            entity.Property(e => e.TeamsMessageId)
                  .IsRequired(false)
                  .HasMaxLength(256);

            entity.Property(e => e.ResolvedAt)
                  .IsRequired(false);

            entity.Property(e => e.CreatedAt)
                  .IsRequired();

            entity.Property(e => e.UpdatedAt)
                  .IsRequired()
                  .IsConcurrencyToken(); // Optimistic concurrency on every update

            entity.HasIndex(e => e.Status)
                  .HasDatabaseName("IX_PipelineRunStates_Status");

            entity.HasIndex(e => new { e.FactoryName, e.Status })
                  .HasDatabaseName("IX_PipelineRunStates_FactoryName_Status");
        });
    }
}
