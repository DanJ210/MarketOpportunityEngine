using System.Text.Json;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MarketOpportunity.UnitTests;

public sealed class MarketSituationPersistenceMappingTests
{
    private static readonly DateTimeOffset AsOfUtc =
        new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Maps_complete_candidates_to_bounded_normalized_sources_and_members()
    {
        var candidateSet = CandidateSet();
        var sources = MarketSituationPersistenceMapper.Sources(candidateSet);
        var members = MarketSituationPersistenceMapper.Members(candidateSet);

        Assert.Equal(2, sources.Count);
        Assert.Equal(candidateSet.TotalCandidateCount, members.Count);
        Assert.Equal(Enumerable.Range(0, members.Count), members.Select(item => item.Ordinal));
        Assert.All(members, member =>
        {
            Assert.Contains(sources, source => source.CandidateSourceId == member.CandidateSourceId);
            Assert.Equal(member.Price, decimal.Parse(member.PriceCanonical));
        });
        Assert.Equal(64, MarketSituationPersistenceMapper.HeaderMaterialDigest(candidateSet).Length);
    }

    [Fact]
    public void Mapping_material_digests_are_deterministic_and_reject_duplicate_level_identity()
    {
        var candidateSet = CandidateSet();
        var duplicateLevels = candidateSet.EligibleLevels
            .Append(candidateSet.EligibleLevels[0])
            .ToArray();
        var duplicateSet = candidateSet with { EligibleLevels = duplicateLevels };

        Assert.Equal(
            MarketSituationPersistenceMapper.HeaderMaterialDigest(candidateSet),
            MarketSituationPersistenceMapper.HeaderMaterialDigest(
                candidateSet with { EligibleLevels = candidateSet.EligibleLevels.Reverse().ToArray() }));
        Assert.Throws<MarketSituationPersistenceException>(() =>
            MarketSituationPersistenceMapper.Members(duplicateSet));
    }

    [Fact]
    public void Compact_persistence_json_uses_bounded_selected_evidence_only()
    {
        var assessment = CompactAssessment(CandidateSet());
        var json = MarketSituationPersistenceMapper.CompactJson(assessment, out var byteCount);

        Assert.True(byteCount <= MarketSituationPersistenceOptions.CompactAssessmentMaximumBytes);
        Assert.Equal(
            CompactMarketSituationAssessment.CurrentPersistenceContractVersion,
            assessment.PersistenceContractVersion);
        Assert.DoesNotContain("EligibleLevels", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            MarketSituationPersistenceMapper.CompactMaterialDigest(assessment),
            MarketSituationPersistenceMapper.CompactMaterialDigest(
                assessment with { EvaluatedAtUtc = AsOfUtc.AddHours(1) }));
    }

    [Fact]
    public void Compact_persistence_rejects_over_budget_without_truncation()
    {
        var candidateSet = CandidateSet();
        var selected = candidateSet.EligibleLevels[0] with
        {
            SourceCompleteness = new string('x', 20_000)
        };
        var assessment = CompactAssessment(candidateSet) with
        {
            NearestSupportLevel = selected
        };

        var exception = Assert.Throws<MarketSituationPersistenceException>(() =>
            MarketSituationPersistenceMapper.CompactJson(assessment, out _));
        Assert.Equal(
            MarketSituationPersistenceFailure.CompactAssessmentTooLarge,
            exception.Failure);
    }

    [Fact]
    public void Ef_model_maps_normalized_tables_and_exact_price_round_trip_columns()
    {
        var options = new DbContextOptionsBuilder<MarketOpportunityDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new MarketOpportunityDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var member = model.FindEntityType(typeof(MarketSituationCandidateMemberEntity))!;
        var assessment = model.FindEntityType(typeof(MarketSituationAssessmentEntity))!;
        var set = model.FindEntityType(typeof(MarketSituationCandidateSetEntity))!;

        Assert.Equal("market_situation_candidate_member", member.GetTableName());
        Assert.Equal(38, member.FindProperty(nameof(MarketSituationCandidateMemberEntity.Price))!
            .GetPrecision());
        Assert.Equal(18, member.FindProperty(nameof(MarketSituationCandidateMemberEntity.Price))!
            .GetScale());
        Assert.Equal(
            "nvarchar(max)",
            assessment.FindProperty(nameof(MarketSituationAssessmentEntity.AssessmentJson))!
                .GetColumnType());
        Assert.Equal("char(64)", set.FindProperty(nameof(MarketSituationCandidateSetEntity.Id))!
            .GetColumnType());
        Assert.Contains(
            assessment.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(MarketSituationCandidateSetEntity));
        Assert.Single(assessment.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_market_situation_assessment_PersistenceEnvelope");
        Assert.Equal(2, set.GetCheckConstraints().Count());
    }

    [Fact]
    public void Persistence_options_validate_batching_but_keep_fixed_compact_budget()
    {
        new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 1 }.Validate();
        new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 5_000 }.Validate();
        Assert.Throws<InvalidOperationException>(() =>
            new MarketSituationPersistenceOptions { CandidateMemberBatchSize = 0 }.Validate());
        Assert.Equal(
            16 * 1024,
            MarketSituationPersistenceOptions.CompactAssessmentMaximumBytes);
    }

    private static MarketSituationCandidateSet CandidateSet()
    {
        var first = Level(
            1,
            123.456789012345678m,
            ImportantPriceLevelRole.Support,
            ImportantPriceLevelSource.PriorSwingLow);
        var second = Level(
            2,
            124.456789012345678m,
            ImportantPriceLevelRole.Resistance,
            ImportantPriceLevelSource.PriorSwingHigh);
        return MarketSituationCandidateSet.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "TEST",
            "current-session",
            AsOfUtc,
            MarketSituationContextMode.PriorSessionEnhanced,
            [second, first],
            0.10m,
            0.25m,
            "market-situation-v1",
            "features-v16",
            "scanner-config-v20",
            new MarketDataCapabilities(
                "UnitTest",
                "Recorded",
                MarketDataCoverage.Synthetic,
                false,
                true,
                true),
            DataQualityStatus.Good,
            MarketStructureStatus.Available,
            true);
    }

    private static CompactMarketSituationAssessment CompactAssessment(
        MarketSituationCandidateSet candidateSet)
    {
        var support = candidateSet.EligibleLevels.Single(
            item => item.Role == ImportantPriceLevelRole.Support);
        var resistance = candidateSet.EligibleLevels.Single(
            item => item.Role == ImportantPriceLevelRole.Resistance);
        return new CompactMarketSituationAssessment(
            Guid.NewGuid(),
            candidateSet.CandidateSetId,
            candidateSet.FeatureSnapshotId,
            candidateSet.PriorSessionContextId,
            candidateSet.Symbol,
            candidateSet.SessionId,
            candidateSet.AsOfUtc,
            AsOfUtc,
            124m,
            MarketSituationStatus.Available,
            null,
            candidateSet.ContextMode,
            support,
            resistance,
            null,
            null,
            null,
            [],
            candidateSet.SupportCandidateCount,
            candidateSet.ResistanceCandidateCount,
            candidateSet.TotalCandidateCount,
            candidateSet.TestZonePercent,
            candidateSet.ApproachDistancePercent,
            candidateSet.DefinitionVersion,
            candidateSet.FeatureSetVersion,
            candidateSet.ConfigurationVersion,
            candidateSet.MarketData,
            candidateSet.DataQuality,
            candidateSet.MarketStructureStatus,
            candidateSet.HasCompleteCurrentSessionData);
    }

    private static ImportantPriceLevel Level(
        int id,
        decimal price,
        ImportantPriceLevelRole role,
        ImportantPriceLevelSource source) =>
        new(
            new Guid(id, 0, 0, new byte[8]),
            "TEST",
            "current-session",
            "prior-session",
            price,
            role,
            source,
            new Guid(id, 1, 0, new byte[8]),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            $"origin-{id}",
            $"confirmation-{id}",
            AsOfUtc.AddMinutes(id - 2),
            AsOfUtc.AddMinutes(id - 1),
            MarketSituationContextMode.PriorSessionEnhanced,
            "Complete prior-session history",
            "test-history",
            AsOfUtc.AddDays(-1).AddHours(-6),
            AsOfUtc.AddDays(-1),
            "session-swing-fractal-v1",
            "features-v16",
            "prior-session-context-v1",
            "UnitTest",
            "sip");
}
