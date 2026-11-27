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
    public DbSet<MarketSituationCandidateSetEntity> MarketSituationCandidateSets =>
        Set<MarketSituationCandidateSetEntity>();
    public DbSet<MarketSituationCandidateSourceEntity> MarketSituationCandidateSources =>
        Set<MarketSituationCandidateSourceEntity>();
    public DbSet<MarketSituationCandidateMemberEntity> MarketSituationCandidateMembers =>
        Set<MarketSituationCandidateMemberEntity>();
    public DbSet<LevelInteractionEpisodeEntity> LevelInteractionEpisodes =>
        Set<LevelInteractionEpisodeEntity>();
    public DbSet<MarketReactionAssessmentEntity> MarketReactionAssessments =>
        Set<MarketReactionAssessmentEntity>();
    public DbSet<MarketReactionResearchContextEntity> MarketReactionResearchContexts =>
        Set<MarketReactionResearchContextEntity>();

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
            entity.ToTable("market_situation_assessment", table =>
                table.HasCheckConstraint(
                    "CK_market_situation_assessment_PersistenceEnvelope",
                    """
                    ([PersistenceVersion] IS NULL AND [CandidateSetId] IS NULL
                        AND [AssessmentMaterialDigest] IS NULL)
                    OR
                    ([PersistenceVersion] = N'market-situation-persistence-v2'
                        AND [CandidateSetId] IS NOT NULL
                        AND [AssessmentMaterialDigest] IS NOT NULL)
                    """));
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
            entity.Property(item => item.PersistenceVersion).HasMaxLength(64);
            entity.Property(item => item.CandidateSetId)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.AssessmentMaterialDigest)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => item.CandidateSetId)
                .HasDatabaseName("IX_market_situation_assessment_CandidateSetId");
            entity.HasIndex(item => new { item.SupportSituationType, item.SupportLevelSource, item.AsOfUtc });
            entity.HasIndex(item => new { item.ResistanceSituationType, item.ResistanceLevelSource, item.AsOfUtc });
            entity.HasIndex(item => new { item.PrimarySituationType, item.AsOfUtc });
        });

        modelBuilder.Entity<LevelInteractionEpisodeEntity>(entity =>
        {
            entity.ToTable("level_interaction_episode");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(32).IsRequired();
            entity.Property(item => item.LatestAsOfUtc).IsRequired();
            entity.Property(item => item.EpisodeJson).IsRequired();
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.Status });
            entity.HasIndex(item => item.LevelId);
            entity.HasIndex(item => item.StartedAtUtc);
        });

        modelBuilder.Entity<MarketReactionAssessmentEntity>(entity =>
        {
            entity.ToTable("market_reaction_assessment");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ReactionType).HasMaxLength(40);
            entity.Property(item => item.AssessmentJson).IsRequired();
            entity.HasOne<LevelInteractionEpisodeEntity>()
                .WithMany()
                .HasForeignKey(item => item.EpisodeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => item.EpisodeId);
            entity.HasIndex(item => item.EpisodeId)
                .IsUnique()
                .HasFilter("[ReactionType] IS NOT NULL")
                .HasDatabaseName("UX_market_reaction_assessment_EpisodeId_Confirmed");
            entity.HasIndex(item => item.LevelId);
            entity.HasIndex(item => item.ReactionType);
        });

        modelBuilder.Entity<MarketReactionResearchContextEntity>(entity =>
        {
            entity.ToTable("market_reaction_research_context");
            entity.HasKey(item => item.AssessmentId);
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ContextJson).IsRequired();
            entity.HasOne<MarketReactionAssessmentEntity>()
                .WithOne()
                .HasForeignKey<MarketReactionResearchContextEntity>(item => item.AssessmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => item.EpisodeId);
        });

        modelBuilder.Entity<MarketSituationCandidateSetEntity>(entity =>
        {
            entity.ToTable("market_situation_candidate_set", table =>
            {
                table.HasCheckConstraint(
                    "CK_market_situation_candidate_set_Counts",
                    """
                    [SupportCandidateCount] >= 0
                    AND [ResistanceCandidateCount] >= 0
                    AND [TotalCandidateCount] = [SupportCandidateCount] + [ResistanceCandidateCount]
                    """);
                table.HasCheckConstraint(
                    "CK_market_situation_candidate_set_Completion",
                    """
                    ([IsComplete] = 0 AND [CompletedAtUtc] IS NULL)
                    OR ([IsComplete] = 1 AND [CompletedAtUtc] IS NOT NULL)
                    """);
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.CanonicalizationVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ContextMode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.DefinitionVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.FeatureSetVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ConfigurationVersion).HasMaxLength(64).IsRequired();
            entity.Property(item => item.MarketDataProvider).HasMaxLength(64).IsRequired();
            entity.Property(item => item.MarketDataFeed).HasMaxLength(64).IsRequired();
            entity.Property(item => item.MarketDataCoverage).HasMaxLength(32).IsRequired();
            entity.Property(item => item.DataQuality).HasMaxLength(32).IsRequired();
            entity.Property(item => item.MarketStructureStatus).HasMaxLength(32).IsRequired();
            entity.Property(item => item.MaterialDigest)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            entity.Property(item => item.CandidateMaterialDigest)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            entity.Property(item => item.TestZonePercent).HasPrecision(38, 18);
            entity.Property(item => item.ApproachDistancePercent).HasPrecision(38, 18);
            entity.HasIndex(item => new { item.Symbol, item.SessionId, item.AsOfUtc });
            entity.HasIndex(item => item.MaterialDigest);
        });

        modelBuilder.Entity<MarketSituationCandidateSourceEntity>(entity =>
        {
            entity.ToTable("market_situation_candidate_source");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.CurrentSessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SourceSessionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SourceKind).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ContextMode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.SourceCompleteness).HasMaxLength(256).IsRequired();
            entity.Property(item => item.HistoricalSource).HasMaxLength(128);
            entity.Property(item => item.StructureAlgorithmVersion).HasMaxLength(64);
            entity.Property(item => item.FeatureSetVersion).HasMaxLength(64);
            entity.Property(item => item.ContextDefinitionVersion).HasMaxLength(64);
            entity.Property(item => item.HistoricalProvider).HasMaxLength(64);
            entity.Property(item => item.HistoricalFeed).HasMaxLength(64);
            entity.Property(item => item.MaterialDigest)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            entity.HasIndex(item => item.MaterialDigest);
        });

        modelBuilder.Entity<MarketSituationCandidateMemberEntity>(entity =>
        {
            entity.ToTable("market_situation_candidate_member");
            entity.HasKey(item => new { item.CandidateSetId, item.Ordinal });
            entity.Property(item => item.CandidateSetId)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.CandidateSourceId)
                .HasColumnType("char(64)")
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.Role).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Price).HasPrecision(38, 18);
            entity.Property(item => item.PriceCanonical).HasMaxLength(64).IsRequired();
            entity.Property(item => item.SourceEventIdentity).HasMaxLength(256);
            entity.Property(item => item.ConfirmationEventIdentity).HasMaxLength(256);
            entity.HasOne<MarketSituationCandidateSetEntity>()
                .WithMany()
                .HasForeignKey(item => item.CandidateSetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MarketSituationCandidateSourceEntity>()
                .WithMany()
                .HasForeignKey(item => item.CandidateSourceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.CandidateSetId, item.LevelId })
                .IsUnique()
                .HasDatabaseName("UX_market_situation_candidate_member_Set_LevelId");
            entity.HasIndex(item => item.CandidateSourceId);
        });

        modelBuilder.Entity<MarketSituationAssessmentEntity>(entity =>
        {
            entity.HasOne<MarketSituationCandidateSetEntity>()
                .WithMany()
                .HasForeignKey(item => item.CandidateSetId)
                .OnDelete(DeleteBehavior.Restrict);
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
    public string? PersistenceVersion { get; set; }
    public string? CandidateSetId { get; set; }
    public string? AssessmentMaterialDigest { get; set; }
    public Guid? SupportLevelId { get; set; }
    public Guid? ResistanceLevelId { get; set; }
}

public sealed class MarketSituationCandidateSetEntity
{
    public string Id { get; set; } = "";
    public string CanonicalizationVersion { get; set; } = "";
    public Guid FeatureSnapshotId { get; set; }
    public Guid? PriorSessionContextId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public string ContextMode { get; set; } = "";
    public int SupportCandidateCount { get; set; }
    public int ResistanceCandidateCount { get; set; }
    public int TotalCandidateCount { get; set; }
    public decimal TestZonePercent { get; set; }
    public decimal ApproachDistancePercent { get; set; }
    public string DefinitionVersion { get; set; } = "";
    public string FeatureSetVersion { get; set; } = "";
    public string ConfigurationVersion { get; set; } = "";
    public string MarketDataProvider { get; set; } = "";
    public string MarketDataFeed { get; set; } = "";
    public string MarketDataCoverage { get; set; } = "";
    public bool MarketDataIsRealTime { get; set; }
    public bool MarketDataSupportsTrades { get; set; }
    public bool MarketDataSupportsQuotes { get; set; }
    public string DataQuality { get; set; } = "";
    public string MarketStructureStatus { get; set; } = "";
    public bool HasCompleteCurrentSessionData { get; set; }
    public string MaterialDigest { get; set; } = "";
    public string CandidateMaterialDigest { get; set; } = "";
    public bool IsComplete { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class MarketSituationCandidateSourceEntity
{
    public string Id { get; set; } = "";
    public Guid? SourceContextId { get; set; }
    public string SourceSessionId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string CurrentSessionId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string ContextMode { get; set; } = "";
    public string SourceCompleteness { get; set; } = "";
    public string? HistoricalSource { get; set; }
    public DateTimeOffset? SourceCoverageStartUtc { get; set; }
    public DateTimeOffset? SourceCoverageEndUtc { get; set; }
    public string? StructureAlgorithmVersion { get; set; }
    public string? FeatureSetVersion { get; set; }
    public string? ContextDefinitionVersion { get; set; }
    public string? HistoricalProvider { get; set; }
    public string? HistoricalFeed { get; set; }
    public string MaterialDigest { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class MarketSituationCandidateMemberEntity
{
    public string CandidateSetId { get; set; } = "";
    public int Ordinal { get; set; }
    public Guid LevelId { get; set; }
    public string CandidateSourceId { get; set; } = "";
    public string Role { get; set; } = "";
    public decimal Price { get; set; }
    public string PriceCanonical { get; set; } = "";
    public Guid SourceStructuralIdentity { get; set; }
    public string? SourceEventIdentity { get; set; }
    public string? ConfirmationEventIdentity { get; set; }
    public DateTimeOffset? SourceEventTimeUtc { get; set; }
    public DateTimeOffset? ConfirmedAtEventTimeUtc { get; set; }
}

/// <summary>Mutable lifecycle projection; pinned episode identity and geometry remain immutable.</summary>
public sealed class LevelInteractionEpisodeEntity
{
    public Guid Id { get; set; }
    public Guid LevelId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LatestAsOfUtc { get; set; }
    public string Status { get; set; } = "";
    public string EpisodeJson { get; set; } = "";
}

/// <summary>Immutable reaction assessment history.</summary>
public sealed class MarketReactionAssessmentEntity
{
    public Guid Id { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid LevelId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public string? ReactionType { get; set; }
    public string AssessmentJson { get; set; } = "";
}

/// <summary>Immutable, non-gating feature context captured alongside a reaction assessment.</summary>
public sealed class MarketReactionResearchContextEntity
{
    public Guid AssessmentId { get; set; }
    public Guid EpisodeId { get; set; }
    public string Symbol { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
    public string ContextJson { get; set; } = "";
}
