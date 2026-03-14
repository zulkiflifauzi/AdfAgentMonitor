using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdfAgentMonitor.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PipelineRunState>    PipelineRunStates    => Set<PipelineRunState>();
    public DbSet<AgentActivityLog>   AgentActivityLogs    => Set<AgentActivityLog>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentActivityLog>(entity =>
        {
            entity.ToTable("AgentActivityLogs");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .ValueGeneratedNever();

            entity.Property(e => e.AgentName)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(e => e.PipelineRunId)
                  .IsRequired(false);

            entity.Property(e => e.PipelineName)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(e => e.Action)
                  .IsRequired()
                  .HasMaxLength(512);

            entity.Property(e => e.ResultMessage)
                  .IsRequired(false)
                  .HasMaxLength(4000);

            entity.Property(e => e.Timestamp)
                  .IsRequired();

            entity.HasIndex(e => e.Timestamp)
                  .HasDatabaseName("IX_AgentActivityLogs_Timestamp");

            entity.HasIndex(e => new { e.AgentName, e.Timestamp })
                  .HasDatabaseName("IX_AgentActivityLogs_AgentName_Timestamp");

            entity.HasIndex(e => e.PipelineRunId)
                  .HasDatabaseName("IX_AgentActivityLogs_PipelineRunId");
        });

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

        modelBuilder.Entity<NotificationSettings>(e =>
        {
            e.ToTable("NotificationSettings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RecipientEmails).HasMaxLength(2000).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.Ignore(x => x.RecipientEmailList);
        });
    }
}
