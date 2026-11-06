using MarketOpportunity.Domain;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.Infrastructure;

public sealed class MarketOpportunityDbContext(DbContextOptions<MarketOpportunityDbContext> options)
    : DbContext(options)
{
    public DbSet<MarketEventEntity> MarketEvents => Set<MarketEventEntity>();
    public DbSet<FeatureSnapshotEntity> FeatureSnapshots => Set<FeatureSnapshotEntity>();
    public DbSet<OpportunityEntity> Opportunities => Set<OpportunityEntity>();
    public DbSet<OpportunityEvidenceEntity> OpportunityEvidence => Set<OpportunityEvidenceEntity>();
    public DbSet<ConfigurationSnapshotEntity> ConfigurationSnapshots => Set<ConfigurationSnapshotEntity>();
    public DbSet<TradePlanEntity> TradePlans => Set<TradePlanEntity>();
    public DbSet<TrackedOpportunityEntity> TrackedOpportunities => Set<TrackedOpportunityEntity>();
    public DbSet<PreTradeReadinessAssessmentEntity> PreTradeReadinessAssessments =>
        Set<PreTradeReadinessAssessmentEntity>();
    public DbSet<MarketSituationAssessmentEntity> MarketSituationAssessments =>
        Set<MarketSituationAssessmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketEventEntity>(entity =>
        {
            entity.ToTable("market_event");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.EventType).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Provider).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SequenceId).HasMaxLength(128);
            entity.Property(item => item.EventIdentity).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.EventTimeUtc });
            entity.HasIndex(item => item.EventIdentity).IsUnique();
        });

        modelBuilder.Entity<FeatureSnapshotEntity>(entity =>
        {
            entity.ToTable("feature_snapshot");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.FeatureSetVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SnapshotJson).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
        });

        modelBuilder.Entity<OpportunityEntity>(entity =>
        {
            entity.ToTable("opportunity");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SetupType).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Bias).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Classification).HasMaxLength(32).IsRequired();
            entity.Property(item => item.FeatureSetVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SetupDefinitionVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ConfigurationVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.DataProvider).HasMaxLength(64).IsRequired();
            entity.Property(item => item.DataFeed).HasMaxLength(64).IsRequired();
            entity.Property(item => item.DataCoverage).HasMaxLength(32).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasMany(item => item.Evidence)
                .WithOne()
                .HasForeignKey(item => item.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OpportunityEvidenceEntity>(entity =>
        {
            entity.ToTable("opportunity_evidence");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Kind).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(1024).IsRequired();
            entity.HasIndex(item => item.OpportunityId);
        });

        modelBuilder.Entity<ConfigurationSnapshotEntity>(entity =>
        {
            entity.ToTable("configuration_snapshot");
            entity.HasKey(item => item.Version);
            entity.Property(item => item.Version).HasMaxLength(64);
            entity.Property(item => item.ConfigurationJson).IsRequired();
        });

        modelBuilder.Entity<TradePlanEntity>(entity =>
        {
            entity.ToTable("trade_plan");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.PlanDefinitionVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.TradePlanJson).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => item.OpportunityId);
        });

        modelBuilder.Entity<TrackedOpportunityEntity>(entity =>
        {
            entity.ToTable("tracked_opportunity");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Direction).HasMaxLength(8).IsRequired();
            entity.Property(item => item.SelectionDefinitionVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SelectionReasonCode).HasMaxLength(128).IsRequired();
            entity.Property(item => item.SelectionReason).HasMaxLength(1024).IsRequired();
            entity.Property(item => item.TerminalReasonCode).HasMaxLength(128);
            entity.Property(item => item.TerminalReason).HasMaxLength(1024);
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.Status });
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.PlanAsOfUtc });
            entity.HasIndex(item => item.PlanId);
        });

        modelBuilder.Entity<PreTradeReadinessAssessmentEntity>(entity =>
        {
            entity.ToTable("pre_trade_readiness_assessment");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.State).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Reason).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ContextMode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ReadinessDefinitionVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.AssessmentJson).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => item.TrackedOpportunityId);
            entity.HasIndex(item => item.PlanId);
        });

        modelBuilder.Entity<MarketSituationAssessmentEntity>(entity =>
        {
            entity.ToTable("market_situation_assessment");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ContextMode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.PrimarySituationType).HasMaxLength(32);
            entity.Property(item => item.SupportSituationType).HasMaxLength(32);
            entity.Property(item => item.ResistanceSituationType).HasMaxLength(32);
            entity.Property(item => item.SupportLevelSource).HasMaxLength(32);
            entity.Property(item => item.ResistanceLevelSource).HasMaxLength(32);
            entity.Property(item => item.DefinitionVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ConfigurationVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.AssessmentJson).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => new { item.SupportSituationType, item.SupportLevelSource, item.AsOfUtc });
            entity.HasIndex(item => new { item.ResistanceSituationType, item.ResistanceLevelSource, item.AsOfUtc });
            entity.HasIndex(item => new { item.PrimarySituationType, item.AsOfUtc });
        });
    }
}

public sealed class MarketEventEntity
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTimeOffset EventTimeUtc { get; set; }
    public DateTimeOffset ReceivedTimeUtc { get; set; }
    public string Provider { get; set; } = "";
    public string? SequenceId { get; set; }
    public string EventIdentity { get; set; } = "";
    public decimal? TradePrice { get; set; }
    public long? TradeSize { get; set; }
    public decimal? BidPrice { get; set; }
    public decimal? AskPrice { get; set; }
    public long? BidSize { get; set; }
    public long? AskSize { get; set; }
}

public sealed class FeatureSnapshotEntity
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public string FeatureSetVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
}

public sealed class OpportunityEntity
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public DateTimeOffset ValidUntilUtc { get; set; }
    public string SetupType { get; set; } = "";
    public string Bias { get; set; } = "";
    public string Classification { get; set; } = "";
    public int QualityScore { get; set; }
    public decimal? ReferencePrice { get; set; }
    public string FeatureSetVersion { get; set; } = "";
    public string SetupDefinitionVersion { get; set; } = "";
    public string ConfigurationVersion { get; set; } = "";
    public string DataProvider { get; set; } = "";
    public string DataFeed { get; set; } = "";
    public string DataCoverage { get; set; } = "";
    public bool IsRealTimeData { get; set; }
    public bool SupportsTrades { get; set; }
    public bool SupportsQuotes { get; set; }
    public ICollection<OpportunityEvidenceEntity> Evidence { get; set; } = new List<OpportunityEvidenceEntity>();
}

public sealed class OpportunityEvidenceEntity
{
    public Guid Id { get; set; }
    public Guid OpportunityId { get; set; }
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ConfigurationSnapshotEntity
{
    public string Version { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string ConfigurationJson { get; set; } = "";
}

public sealed class TradePlanEntity
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public Guid OpportunityId { get; set; }
    public string PlanDefinitionVersion { get; set; } = "";
    public string TradePlanJson { get; set; } = "";
}

/// <summary>
/// Tracked-candidate association and lifecycle. Only <c>Status</c>, <c>HasEstablishedEntryReady</c>,
/// <c>TerminalAtUtc</c>, <c>TerminalReasonCode</c>, and <c>TerminalReason</c> may change after insert;
/// every other column is immutable (see ADR-008).
/// </summary>
public sealed class TrackedOpportunityEntity
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid OpportunityId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateTimeOffset SelectedAtUtc { get; set; }
    public DateTimeOffset PlanAsOfUtc { get; set; }
    public string SelectionDefinitionVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public string SelectionReasonCode { get; set; } = "";
    public string SelectionReason { get; set; } = "";
    public bool HasEstablishedEntryReady { get; set; }
    public DateTimeOffset? TerminalAtUtc { get; set; }
    public string? TerminalReasonCode { get; set; }
    public string? TerminalReason { get; set; }
}

/// <summary>Immutable readiness history row; identical replay is idempotent and a different payload for the same identity fails.</summary>
public sealed class PreTradeReadinessAssessmentEntity
{
    public Guid Id { get; set; }
    public Guid TrackedOpportunityId { get; set; }
    public Guid PlanId { get; set; }
    public Guid OpportunityId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; }
    public string State { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ContextMode { get; set; } = "";
    public string ReadinessDefinitionVersion { get; set; } = "";
    public string AssessmentJson { get; set; } = "";
}

/// <summary>Immutable descriptive market-situation history row.</summary>
public sealed class MarketSituationAssessmentEntity
{
    public Guid Id { get; set; }
    public Guid FeatureSnapshotId { get; set; }
    public Guid? PriorSessionContextId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; }
    public string Status { get; set; } = "";
    public string ContextMode { get; set; } = "";
    public string? PrimarySituationType { get; set; }
    public string? SupportSituationType { get; set; }
    public string? ResistanceSituationType { get; set; }
    public string? SupportLevelSource { get; set; }
    public string? ResistanceLevelSource { get; set; }
    public string DefinitionVersion { get; set; } = "";
    public string ConfigurationVersion { get; set; } = "";
    public string AssessmentJson { get; set; } = "";
}
