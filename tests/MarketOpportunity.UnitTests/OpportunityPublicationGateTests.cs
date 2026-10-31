using MarketOpportunity.Application;
using MarketOpportunity.Analytics;
using MarketOpportunity.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketOpportunity.UnitTests;

public sealed class OpportunityPublicationGateTests
{
    private static readonly DateTimeOffset AsOfUtc =
        new(2026, 10, 24, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Healthy_feed_activates_an_eligible_candidate()
    {
        var decision = new OpportunityPublicationGate().Evaluate(
            Candidate(),
            MarketDataHealthStatus.Live,
            AsOfUtc.AddSeconds(1));

        Assert.Equal(OpportunityLifecycleState.Active, decision.State);
        Assert.Equal("PublicationAllowed", decision.ReasonCode);
    }

    [Theory]
    [InlineData(MarketDataHealthStatus.Stale, "MarketDataStale")]
    [InlineData(MarketDataHealthStatus.Offline, "MarketDataOffline")]
    [InlineData(MarketDataHealthStatus.Error, "MarketDataError")]
    public void Unhealthy_feed_suspends_an_eligible_candidate(
        MarketDataHealthStatus health,
        string reasonCode)
    {
        var decision = new OpportunityPublicationGate().Evaluate(
            Candidate(),
            health,
            AsOfUtc.AddSeconds(1));

        Assert.Equal(OpportunityLifecycleState.Suspended, decision.State);
        Assert.Equal(reasonCode, decision.ReasonCode);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact]
    public void Healthy_to_stale_transition_suspends_current_publication_without_rewriting_analysis()
    {
        var gate = new OpportunityPublicationGate();
        var state = new ScannerState(gate);
        var candidate = Candidate();
        var features = Features();
        var result = new ScannerAnalysisResult(
            features, candidate, PendingPublication(), Plan(features, candidate));

        state.SetResults(Status(MarketDataHealthStatus.Live), [result], AsOfUtc);
        var active = Assert.Single(state.Read().Results);
        Assert.Equal(OpportunityLifecycleState.Active, active.Publication.State);

        state.SetStatus(Status(MarketDataHealthStatus.Stale), AsOfUtc.AddSeconds(5));
        var suspended = Assert.Single(state.Read().Results);

        Assert.Equal(OpportunityLifecycleState.Suspended, suspended.Publication.State);
        Assert.Equal("MarketDataStale", suspended.Publication.ReasonCode);
        Assert.Equal(candidate, suspended.Opportunity);
        Assert.Equal(result.TradePlan, suspended.TradePlan);
        Assert.Equal(candidate.SupportingEvidence, suspended.Opportunity.SupportingEvidence);
        Assert.Equal(candidate.ContradictoryEvidence, suspended.Opportunity.ContradictoryEvidence);
        Assert.Empty(suspended.Opportunity.ActiveVetoes);
    }

    [Fact]
    public void Unresolved_market_data_gap_suspends_publication_even_after_feed_resumes()
    {
        var decision = new OpportunityPublicationGate().Evaluate(
            Candidate(),
            MarketDataHealthStatus.Live,
            AsOfUtc.AddSeconds(1),
            hasUnresolvedDataGap: true);

        Assert.Equal(OpportunityLifecycleState.Suspended, decision.State);
        Assert.Equal("MarketDataGapUnresolved", decision.ReasonCode);
        Assert.Contains("gap", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recorded_fixture_publication_uses_event_time_and_is_deterministic()
    {
        var candidate = Candidate();
        var first = new ScannerState();
        var second = new ScannerState();
        var features = Features();
        var result = new ScannerAnalysisResult(
            features, candidate, PendingPublication(), Plan(features, candidate));

        first.SetResults(Status(MarketDataHealthStatus.Recorded), [result]);
        second.SetResults(Status(MarketDataHealthStatus.Recorded), [result]);

        var firstDecision = Assert.Single(first.Read().Results).Publication;
        var secondDecision = Assert.Single(second.Read().Results).Publication;
        Assert.Equal(OpportunityLifecycleState.Active, firstDecision.State);
        Assert.Equal(firstDecision, secondDecision);
        Assert.Equal(AsOfUtc, firstDecision.EvaluatedAtUtc);
    }

    [Fact]
    public void Publication_state_and_reason_are_part_of_the_application_read_model()
    {
        var state = new ScannerState();
        var features = Features();
        var opportunity = Candidate();
        state.SetResults(
            Status(MarketDataHealthStatus.Offline),
            [new ScannerAnalysisResult(
                features, opportunity, PendingPublication(), Plan(features, opportunity))],
            AsOfUtc);

        var readModel = state.Read();
        var result = Assert.Single(readModel.Results);
        Assert.Equal(MarketDataHealthStatus.Offline, readModel.Status.FeedStatus);
        Assert.Equal(OpportunityLifecycleState.Suspended, result.Publication.State);
        Assert.Contains("offline", result.Publication.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Opportunity_API_read_model_serializes_lifecycle_reason_and_feed_provenance()
    {
        var publication = new PublicationDecision(
            OpportunityLifecycleState.Suspended,
            "MarketDataStale",
            "Market data is stale.",
            AsOfUtc);
        var opportunity = Candidate();
        var apiModel = new OpportunityPublicationReadModel(
            opportunity,
            publication,
            Plan(Features(), opportunity));
        var json = JsonSerializer.Serialize(
            apiModel,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            });
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            "Suspended",
            document.RootElement.GetProperty("publication").GetProperty("state").GetString());
        Assert.Equal(
            "Market data is stale.",
            document.RootElement.GetProperty("publication").GetProperty("reason").GetString());
        Assert.Equal(
            "Unknown",
            document.RootElement.GetProperty("opportunity").GetProperty("marketData")
                .GetProperty("provider").GetString());
        Assert.Equal(
            TradePlanBuilder.DefinitionVersion,
            document.RootElement.GetProperty("tradePlan").GetProperty("planDefinitionVersion").GetString());
    }

    [Fact]
    public void Expired_candidate_cannot_be_active_even_with_healthy_feed()
    {
        var decision = new OpportunityPublicationGate().Evaluate(
            Candidate(),
            MarketDataHealthStatus.Live,
            AsOfUtc.AddMinutes(3));

        Assert.Equal(OpportunityLifecycleState.Expired, decision.State);
        Assert.Equal("OpportunityExpired", decision.ReasonCode);
    }

    private static ScannerStatus Status(MarketDataHealthStatus health) =>
        new(health, "TestProvider", 0, AsOfUtc, null);

    private static PublicationDecision PendingPublication() =>
        new(
            OpportunityLifecycleState.NotQualified,
            "PendingPublication",
            "Pending.",
            AsOfUtc);

    private static TradePlan Plan(FeatureSnapshot features, Opportunity opportunity) =>
        new TradePlanBuilder().Build(features, opportunity);

    private static FeatureSnapshot Features() =>
        new(
            Guid.NewGuid(),
            "MOE",
            "publication-session",
            AsOfUtc,
            "features-test",
            100m,
            99.99m,
            100.01m,
            2m,
            99.5m,
            null,
            VwapSlopeDirection.Insufficient,
            0.5m,
            0.1m,
            null,
            100,
            100_000m,
            ShortHorizonDollarVolumeStatus.Available,
            100,
            100m,
            100m,
            null,
            null,
            null,
            AdxStatus.Insufficient,
            null,
            RealizedVolatilityStatus.Insufficient,
            TrendDirectionContext.Insufficient,
            null,
            null,
            null,
            null,
            null,
            TimeSpan.Zero,
            true,
            true,
            true,
            true,
            DataQualityStatus.Good,
            MarketDataCapabilities.Unknown);

    private static Opportunity Candidate() =>
        new(
            Guid.NewGuid(),
            "MOE",
            "publication-session",
            AsOfUtc,
            AsOfUtc.AddMinutes(2),
            "Momentum Continuation",
            SetupBias.Long,
            OpportunityClassification.ProbableLong,
            90,
            [new OpportunityEvidence("Momentum", "Positive momentum.")],
            [new OpportunityEvidence("Context", "Benchmark is unavailable.")],
            [],
            "Below session VWAP.",
            100m,
            "features-test",
            "setup-test",
            "config-test",
            MarketDataCapabilities.Unknown);
}
