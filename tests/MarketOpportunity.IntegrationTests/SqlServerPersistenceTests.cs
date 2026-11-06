using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using MarketOpportunity.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MarketOpportunity.IntegrationTests;

public sealed class SqlServerPersistenceTests
{
    [Fact]
    public async Task Market_situation_history_is_immutable_idempotent_and_round_trips()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var sessionId = $"it-situation-session-{token}";
        var assessedAt = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        var assessment = new MarketSituationAssessment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            symbol,
            sessionId,
            assessedAt,
            assessedAt.AddSeconds(1),
            100m,
            MarketSituationStatus.NoEligibleLevels,
            "No eligible structural levels.",
            MarketSituationContextMode.CurrentSessionOnly,
            null,
            null,
            null,
            null,
            null,
            [],
            0.10m,
            0.25m,
            "market-situation-v1",
            "features-v16",
            "scanner-config-v19",
            MarketDataCapabilities.Unknown,
            DataQualityStatus.Good,
            MarketStructureStatus.Insufficient,
            true);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }
            }

            schemaReady = true;
            await PersistMarketSituation(connectionString, assessment);
            await PersistMarketSituation(
                connectionString,
                assessment with { EvaluatedAtUtc = assessment.EvaluatedAtUtc.AddMinutes(1) });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PersistMarketSituation(
                    connectionString,
                    assessment with { UnavailableReason = "Conflicting material content." }));

            await using var verifyContext = CreateContext(connectionString);
            var stored = await verifyContext.MarketSituationAssessments.AsNoTracking()
                .SingleAsync(item => item.Id == assessment.Id);
            Assert.Equal(1, await verifyContext.MarketSituationAssessments
                .CountAsync(item => item.Symbol == symbol && item.SessionId == sessionId));
            Assert.Equal(MarketSituationStatus.NoEligibleLevels.ToString(), stored.Status);
            Assert.Equal("market-situation-v1", stored.DefinitionVersion);
            var restored = JsonSerializer.Deserialize<MarketSituationAssessment>(
                stored.AssessmentJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(restored);
            Assert.Equal(assessment.Id, restored.Id);
            Assert.Equal(assessment.EligibleLevels, restored.EligibleLevels);
            Assert.Equal(assessment.CurrentReferencePrice, restored.CurrentReferencePrice);
        }
        finally
        {
            if (schemaReady)
            {
                await using var cleanupContext = CreateContext(connectionString);
                await cleanupContext.MarketSituationAssessments
                    .Where(item => item.Symbol == symbol && item.SessionId == sessionId)
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task Confirmed_swing_snapshots_are_persisted_idempotently_without_rewriting_earlier_results()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var sessionId = $"it-swing-session-{token}";
        var configuration = new ScannerConfiguration
        {
            Version = $"it-swing-config-{token}",
            FeatureSetVersion = $"it-swing-features-{token}"
        };
        var start = new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
        decimal[] prices = [101.5m, 101m, 105m, 103m, 102m, 104m, 103m, 107m, 105m, 104m, 106m, 105m];
        var events = prices.Select((price, index) => (MarketEvent)new TradeEvent(
            symbol, sessionId, start.AddSeconds(index), start.AddSeconds(index + 1),
            "IntegrationFixture", $"swing-{index:D3}", price, 10)).ToArray();
        var earlyEvents = events.Take(4).ToArray();
        var early = Analyze(earlyEvents, configuration);
        var confirmed = Analyze(events, configuration);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }
            }
            schemaReady = true;

            await Persist(connectionString, configuration, earlyEvents, early);
            string originalJson;
            await using (var originalContext = CreateContext(connectionString))
            {
                originalJson = await originalContext.FeatureSnapshots
                    .Where(item => item.Id == early.Features.Id)
                    .Select(item => item.SnapshotJson)
                    .SingleAsync();
            }

            await Persist(connectionString, configuration, events, confirmed);
            await Persist(connectionString, configuration, events, confirmed);
            await using var verifyContext = CreateContext(connectionString);
            var snapshots = await verifyContext.FeatureSnapshots
                .Where(item => item.Symbol == symbol && item.SessionId == sessionId)
                .ToListAsync();
            Assert.Equal(2, snapshots.Count);
            Assert.Equal(originalJson, Assert.Single(snapshots, item => item.Id == early.Features.Id).SnapshotJson);
            var confirmedJson = Assert.Single(snapshots, item => item.Id == confirmed.Features.Id).SnapshotJson;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var storedEarly = JsonSerializer.Deserialize<FeatureSnapshot>(originalJson, options);
            var storedConfirmed = JsonSerializer.Deserialize<FeatureSnapshot>(confirmedJson, options);
            Assert.NotNull(storedEarly);
            Assert.NotNull(storedConfirmed);
            Assert.Equal(MarketStructureStatus.Insufficient, storedEarly.MarketStructureStatus);
            Assert.Null(storedEarly.LatestConfirmedSwingHigh);
            Assert.Equal(confirmed.Features, storedConfirmed);
            Assert.Equal(107m, storedConfirmed.LatestConfirmedSwingHigh!.Price);
            Assert.Equal(104m, storedConfirmed.LatestConfirmedSwingLow!.Price);
            Assert.Equal(105m, storedConfirmed.PreviousConfirmedSwingHigh!.Price);
            Assert.Equal(102m, storedConfirmed.PreviousConfirmedSwingLow!.Price);
            Assert.Equal(MarketStructureState.Bullish, storedConfirmed.StructureState);
            Assert.Equal(104m, storedConfirmed.NearestSupportCandidate!.Price);
            Assert.Equal(105m, storedConfirmed.NearestResistanceCandidate!.Price);
            Assert.Equal(MarketEventIdentity.Create(events[7]), storedConfirmed.LatestConfirmedSwingHigh.OriginEventIdentity);
            Assert.Equal(MarketEventIdentity.Create(events[9]), storedConfirmed.LatestConfirmedSwingHigh.ConfirmationEventIdentity);
            Assert.Equal(configuration.FeatureSetVersion, storedConfirmed.LatestConfirmedSwingHigh.FeatureSetVersion);
            Assert.Equal("session-swing-fractal-v1", storedConfirmed.LatestConfirmedSwingHigh.AlgorithmVersion);
            Assert.Equal(2, await verifyContext.Opportunities.CountAsync(item => item.Symbol == symbol));
            Assert.Equal(2, await verifyContext.TradePlans.CountAsync(item => item.Symbol == symbol));
            var storedPlanJson = await verifyContext.TradePlans
                .Where(item => item.Id == confirmed.TradePlan.Id)
                .Select(item => item.TradePlanJson)
                .SingleAsync();
            var storedPlan = JsonSerializer.Deserialize<TradePlan>(
                storedPlanJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(storedPlan);
            Assert.Equal(confirmed.TradePlan.Id, storedPlan.Id);
            Assert.Equal(confirmed.TradePlan.RiskPerShare, storedPlan.RiskPerShare);
            Assert.Equal(confirmed.TradePlan.Evidence, storedPlan.Evidence);
            Assert.Equal(events.Length, await verifyContext.MarketEvents.CountAsync(item => item.Symbol == symbol));
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(connectionString, [symbol], configuration.Version, configuration.Version);
            }
        }
    }

    [Fact]
    public async Task Sql_persistence_is_session_scoped_immutable_and_replay_idempotent()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var otherSymbol = $"IT{token}X";
        var sessionId = $"it-session-{token}";
        var otherSessionId = $"it-session2-{token}";
        var firstConfiguration = new ScannerConfiguration
        {
            Version = $"it-config-{token}",
            FeatureSetVersion = $"it-features-{token}",
            SetupDefinitionVersion = $"it-setup-{token}",
            TrendStrengthPeriod = 2
        };
        var changedConfiguration = firstConfiguration with
        {
            Version = $"it-config2-{token}",
            MomentumScoreWeight = 49,
            VwapScoreWeight = 36
        };
        var initialEvents = CreateEvents(symbol, sessionId);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }
            }
            schemaReady = true;

            var first = Analyze(initialEvents, firstConfiguration);
            await Persist(connectionString, firstConfiguration, initialEvents, first);
            await Persist(connectionString, firstConfiguration, initialEvents, first);

            var conflictingConfiguration = firstConfiguration with
            {
                MinimumShortHorizonDollarVolumeUsd = 100_001m
            };
            var conflictingAnalysis = Analyze(initialEvents, conflictingConfiguration);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Persist(connectionString, conflictingConfiguration, initialEvents, conflictingAnalysis));

            var changedConfig = Analyze(initialEvents, changedConfiguration);
            await Persist(connectionString, changedConfiguration, initialEvents, changedConfig);

            var changedEvents = initialEvents
                .Append(new TradeEvent(
                    symbol,
                    sessionId,
                    first.Features.AsOfUtc.AddSeconds(1),
                    first.Features.AsOfUtc.AddSeconds(2),
                    "IntegrationFixture",
                    $"late-high-{token}",
                    103.50m,
                    699))
                .Append(new TradeEvent(
                    symbol,
                    sessionId,
                    first.Features.AsOfUtc.AddSeconds(2),
                    first.Features.AsOfUtc.AddSeconds(3),
                    "IntegrationFixture",
                    $"late-interior-{token}",
                    102.50m,
                    1))
                .ToArray();
            var changedInput = Analyze(changedEvents, changedConfiguration);
            await Persist(connectionString, changedConfiguration, changedEvents, changedInput);

            var secondSessionEvents = CreateEvents(symbol, otherSessionId, 300m);
            var secondSession = Analyze(secondSessionEvents, firstConfiguration);
            await Persist(connectionString, firstConfiguration, secondSessionEvents, secondSession);

            var otherSymbolEvents = CreateEvents(otherSymbol, sessionId, 200m);
            var otherSymbolAnalysis = Analyze(otherSymbolEvents, firstConfiguration);
            await Persist(connectionString, firstConfiguration, otherSymbolEvents, otherSymbolAnalysis);

            await using var verifyContext = CreateContext(connectionString);
            var storedEvents = await verifyContext.MarketEvents
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var storedFeatures = await verifyContext.FeatureSnapshots
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var storedOpportunities = await verifyContext.Opportunities
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var storedPlans = await verifyContext.TradePlans
                .Where(item => item.Symbol == symbol)
                .ToListAsync();
            var otherSymbolOpportunityCount = await verifyContext.Opportunities
                .CountAsync(item => item.Symbol == otherSymbol && item.SessionId == sessionId);
            var storedEvidence = await verifyContext.OpportunityEvidence
                .Where(item => storedOpportunities.Select(opportunity => opportunity.Id).Contains(item.OpportunityId))
                .ToListAsync();
            var storedConfigurations = await verifyContext.ConfigurationSnapshots
                .Where(item => item.Version == firstConfiguration.Version
                    || item.Version == changedConfiguration.Version)
                .ToListAsync();

            Assert.Equal(changedEvents.Length + secondSessionEvents.Length, storedEvents.Count);
            Assert.Equal(secondSessionEvents.Length, storedEvents.Count(item => item.SessionId == otherSessionId));
            Assert.All(storedEvents.Where(item => item.SessionId == sessionId), item => Assert.Equal(symbol, item.Symbol));
            Assert.Equal(4, storedFeatures.Count);
            Assert.Equal(4, storedOpportunities.Count);
            Assert.Equal(4, storedPlans.Count);
            Assert.Equal(1, storedPlans.Count(item => item.Id == first.TradePlan.Id));
            var storedFirstPlan = JsonSerializer.Deserialize<TradePlan>(
                Assert.Single(storedPlans, item => item.Id == first.TradePlan.Id).TradePlanJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(storedFirstPlan);
            Assert.Equal(first.TradePlan.Id, storedFirstPlan.Id);
            Assert.Equal(first.TradePlan.EntryZone, storedFirstPlan.EntryZone);
            Assert.Equal(first.TradePlan.RewardRiskRatioTarget1, storedFirstPlan.RewardRiskRatioTarget1);
            Assert.Equal(1, otherSymbolOpportunityCount);
            Assert.Equal(2, storedConfigurations.Count);
            var originalConfiguration = Assert.Single(storedConfigurations,
                item => item.Version == firstConfiguration.Version);
            Assert.Equal(firstConfiguration, JsonSerializer.Deserialize<ScannerConfiguration>(
                originalConfiguration.ConfigurationJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var originalFeatures = Assert.Single(storedFeatures, item => item.Id == first.Features.Id);
            using var originalJson = JsonDocument.Parse(originalFeatures.SnapshotJson);
            Assert.Equal(100, originalJson.RootElement.GetProperty("sessionCumulativeVolume").GetInt64());
            Assert.Equal(103m, originalJson.RootElement.GetProperty("sessionHighPrice").GetDecimal());
            Assert.Equal(100m, originalJson.RootElement.GetProperty("sessionLowPrice").GetDecimal());
            Assert.Equal(100m, originalJson.RootElement.GetProperty("sessionRangePositionPercent").GetDecimal());
            Assert.Equal(JsonValueKind.Null, originalJson.RootElement.GetProperty("sessionPriceStructure").ValueKind);
            Assert.Equal(JsonValueKind.Null, originalJson.RootElement.GetProperty("fastEma").ValueKind);
            Assert.Equal(JsonValueKind.Null, originalJson.RootElement.GetProperty("mediumEma").ValueKind);
            Assert.Equal(100m, originalJson.RootElement.GetProperty("sessionAdx").GetDecimal());
            Assert.Equal((int)AdxStatus.Available,
                originalJson.RootElement.GetProperty("sessionAdxStatus").GetInt32());
            Assert.Equal((int)TrendDirectionContext.Bullish,
                originalJson.RootElement.GetProperty("sessionTrendDirection").GetInt32());
            Assert.Equal(first.Features.SessionRealizedVolatilityPercent,
                originalJson.RootElement.GetProperty("sessionRealizedVolatilityPercent").GetDecimal());
            Assert.Equal((int)first.Features.SessionRealizedVolatilityStatus,
                originalJson.RootElement.GetProperty("sessionRealizedVolatilityStatus").GetInt32());
            Assert.Equal(first.Features.ShortHorizonDollarVolumeUsd,
                originalJson.RootElement.GetProperty("shortHorizonDollarVolumeUsd").GetDecimal());
            Assert.Equal((int)first.Features.ShortHorizonDollarVolumeStatus,
                originalJson.RootElement.GetProperty("shortHorizonDollarVolumeStatus").GetInt32());
            var changedFeatures = Assert.Single(storedFeatures, item => item.Id == changedInput.Features.Id);
            using var changedJson = JsonDocument.Parse(changedFeatures.SnapshotJson);
            Assert.Equal(800, changedJson.RootElement.GetProperty("sessionCumulativeVolume").GetInt64());
            Assert.Equal(103.50m, changedJson.RootElement.GetProperty("sessionHighPrice").GetDecimal());
            Assert.Equal(100m, changedJson.RootElement.GetProperty("sessionLowPrice").GetDecimal());
            var expectedChangedPosition = (102.50m - 100m) / (103.50m - 100m) * 100m;
            Assert.Equal(expectedChangedPosition,
                changedJson.RootElement.GetProperty("sessionRangePositionPercent").GetDecimal());
            Assert.Equal(102.1m, changedJson.RootElement.GetProperty("fastEma").GetDecimal());
            Assert.Equal(12m, changedJson.RootElement.GetProperty("fastEmaSlopePerMinute").GetDecimal());
            Assert.Equal(JsonValueKind.Null, changedJson.RootElement.GetProperty("mediumEma").ValueKind);
            Assert.Equal(JsonValueKind.Null, changedJson.RootElement.GetProperty("sessionPriceStructure").ValueKind);
            Assert.Equal(57.14285714285714285714285714m,
                changedJson.RootElement.GetProperty("sessionAdx").GetDecimal());
            Assert.Equal((int)AdxStatus.Available,
                changedJson.RootElement.GetProperty("sessionAdxStatus").GetInt32());
            Assert.Equal((int)TrendDirectionContext.Bullish,
                changedJson.RootElement.GetProperty("sessionTrendDirection").GetInt32());
            Assert.Equal(changedInput.Features.SessionRealizedVolatilityPercent,
                changedJson.RootElement.GetProperty("sessionRealizedVolatilityPercent").GetDecimal());
            Assert.Equal((int)changedInput.Features.SessionRealizedVolatilityStatus,
                changedJson.RootElement.GetProperty("sessionRealizedVolatilityStatus").GetInt32());
            Assert.Equal(changedInput.Features.ShortHorizonDollarVolumeUsd,
                changedJson.RootElement.GetProperty("shortHorizonDollarVolumeUsd").GetDecimal());
            Assert.Equal((int)changedInput.Features.ShortHorizonDollarVolumeStatus,
                changedJson.RootElement.GetProperty("shortHorizonDollarVolumeStatus").GetInt32());
            Assert.NotEqual(
                originalJson.RootElement.GetProperty("sessionRealizedVolatilityPercent").GetDecimal(),
                changedJson.RootElement.GetProperty("sessionRealizedVolatilityPercent").GetDecimal());
            Assert.Equal(first.Features, JsonSerializer.Deserialize<FeatureSnapshot>(
                originalFeatures.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.DoesNotContain(storedFeatures, item => item.Id == conflictingAnalysis.Features.Id);

            var originalDecision = Assert.Single(storedOpportunities, item => item.Id == first.Opportunity.Id);
            Assert.Equal(first.Opportunity.Symbol, originalDecision.Symbol);
            Assert.Equal(first.Opportunity.SessionId, originalDecision.SessionId);
            Assert.Equal(first.Opportunity.AsOfUtc, originalDecision.AsOfUtc);
            Assert.Equal(first.Opportunity.ValidUntilUtc, originalDecision.ValidUntilUtc);
            Assert.Equal(first.Opportunity.SetupType, originalDecision.SetupType);
            Assert.Equal(first.Opportunity.Bias.ToString(), originalDecision.Bias);
            Assert.Equal(first.Opportunity.Classification.ToString(), originalDecision.Classification);
            Assert.Equal(first.Opportunity.QualityScore, originalDecision.QualityScore);
            Assert.Equal(first.Opportunity.ReferencePrice, originalDecision.ReferencePrice);
            Assert.Equal(first.Opportunity.FeatureSetVersion, originalDecision.FeatureSetVersion);
            Assert.Equal(first.Opportunity.SetupDefinitionVersion, originalDecision.SetupDefinitionVersion);
            Assert.Equal(first.Opportunity.ConfigurationVersion, originalDecision.ConfigurationVersion);
            Assert.NotEqual(first.Opportunity.Id, changedInput.Opportunity.Id);
            Assert.Contains(storedOpportunities, item => item.Id == changedInput.Opportunity.Id);
            var originalDecisionEvidence = storedEvidence.Where(item => item.OpportunityId == first.Opportunity.Id).ToArray();
            Assert.Equal(
                first.Opportunity.SupportingEvidence.Select(item => (item.Code, item.Description)).OrderBy(item => item.Code),
                originalDecisionEvidence.Where(item => item.Kind == "Supporting")
                    .Select(item => (item.Code, item.Description)).OrderBy(item => item.Code));
            Assert.Equal(
                first.Opportunity.ContradictoryEvidence.Select(item => (item.Code, item.Description)).OrderBy(item => item.Code),
                originalDecisionEvidence.Where(item => item.Kind == "Contradictory")
                    .Select(item => (item.Code, item.Description)).OrderBy(item => item.Code));
            Assert.Equal(
                first.Opportunity.ActiveVetoes.OrderBy(code => code),
                originalDecisionEvidence.Where(item => item.Kind == "Veto")
                    .Select(item => item.Code).OrderBy(code => code));
            Assert.Contains(originalDecisionEvidence, item =>
                item.Kind == "Veto"
                && item.Code == "ExcessiveRealizedVolatility"
                && item.Description == "Session realized volatility meets or exceeds the configured maximum.");
            Assert.Equal(
                first.Opportunity.InvalidationCondition,
                Assert.Single(originalDecisionEvidence, item => item.Kind == "Invalidation").Description);
            Assert.Contains(storedEvidence, item => item.Kind == "Supporting");
            Assert.Contains(storedEvidence, item => item.Kind == "Invalidation");
            Assert.All(storedOpportunities, item =>
            {
                Assert.Equal("IntegrationFixture", item.DataProvider);
                Assert.Equal("Synthetic", item.DataFeed);
                Assert.Equal("Synthetic", item.DataCoverage);
                Assert.False(item.IsRealTimeData);
            });
            Assert.Equal(4, storedFeatures.Select(item => item.Id).Distinct().Count());
            Assert.Equal(4, storedOpportunities.Select(item => item.Id).Distinct().Count());
            Assert.Equal(2, storedOpportunities.Count(item => item.ConfigurationVersion == changedConfiguration.Version));
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(
                    connectionString,
                    [symbol, otherSymbol],
                    firstConfiguration.Version,
                    changedConfiguration.Version);
            }
        }
    }

    [Theory]
    [InlineData(TradeDirection.Long)]
    [InlineData(TradeDirection.Short)]
    public async Task Defined_trade_plans_round_trip_all_fields_and_reject_conflicting_replay(
        TradeDirection direction)
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var sessionId = $"it-plan-session-{token}";
        var configuration = new ScannerConfiguration
        {
            Version = $"it-plan-config-{token}",
            FeatureSetVersion = $"it-plan-features-{token}"
        };
        var start = new DateTimeOffset(2026, 10, 30, 14, 30, 0, TimeSpan.Zero);
        decimal[] prices = [101.5m, 101m, 105m, 103m, 102m, 104m, 103m, 107m, 105m, 104m, 105m, 105m, 106m];
        var events = prices.Select((price, index) => (MarketEvent)new TradeEvent(
            symbol, sessionId, start.AddSeconds(index), start.AddSeconds(index + 1),
            "IntegrationFixture", $"plan-{index:D3}",
            direction == TradeDirection.Long ? price : 208m - price, 10)).ToArray();
        var result = Analyze(events, configuration);
        var plan = result.TradePlan;
        Assert.Equal(direction, plan.Direction);
        Assert.Equal(TradePlanStatus.PlanDefined, plan.Status);
        Assert.Equal(direction == TradeDirection.Long ? new PriceZone(104m, 106m) : new PriceZone(102m, 104m),
            plan.EntryZone);
        Assert.Equal(direction == TradeDirection.Long ? 105m : 103m, plan.EntryReferencePrice);
        Assert.Equal(direction == TradeDirection.Long ? 102m : 106m, plan.InvalidationPrice);
        Assert.Equal(direction == TradeDirection.Long ? 107m : 101m, plan.Target1!.Price);
        Assert.Null(plan.Target2);
        Assert.Equal(3m, plan.RiskPerShare);
        Assert.Equal(2m, plan.RewardPerShareToTarget1);
        Assert.Equal(2m / 3m, plan.RewardRiskRatioTarget1);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expectedJson = JsonSerializer.Serialize(plan, options);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }
            }
            schemaReady = true;

            await Persist(connectionString, configuration, events, result);
            await Persist(connectionString, configuration, events, result);
            var collision = result with { TradePlan = plan with { InvalidationPrice = plan.InvalidationPrice + 1m } };
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Persist(connectionString, configuration, events, collision));
            Assert.Equal($"Trade plan '{plan.Id}' is immutable and already has different values.", exception.Message);

            await using var verifyContext = CreateContext(connectionString);
            var stored = Assert.Single(await verifyContext.TradePlans.AsNoTracking()
                .Where(item => item.Symbol == symbol).ToListAsync());
            Assert.Equal(plan.Id, stored.Id);
            Assert.Equal(plan.Symbol, stored.Symbol);
            Assert.Equal(plan.SessionId, stored.SessionId);
            Assert.Equal(plan.AsOfUtc, stored.AsOfUtc);
            Assert.Equal(plan.OpportunityId, stored.OpportunityId);
            Assert.Equal(plan.PlanDefinitionVersion, stored.PlanDefinitionVersion);
            Assert.Equal(expectedJson, stored.TradePlanJson);
            var roundTripped = JsonSerializer.Deserialize<TradePlan>(stored.TradePlanJson, options);
            Assert.NotNull(roundTripped);
            Assert.Equal(expectedJson, JsonSerializer.Serialize(roundTripped, options));
            Assert.Null(roundTripped.Target2);
            Assert.Equal(1, await verifyContext.Opportunities.CountAsync(item => item.Symbol == symbol));
            Assert.Equal(1, await verifyContext.FeatureSnapshots.CountAsync(item => item.Symbol == symbol));
            Assert.Equal(events.Length, await verifyContext.MarketEvents.CountAsync(item => item.Symbol == symbol));
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(connectionString, [symbol], configuration.Version, configuration.Version);
            }
        }
    }

    [Fact]
    public async Task Tracked_opportunity_and_readiness_history_round_trip_restart_and_reject_conflicts()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var sessionId = $"it-readiness-session-{token}";
        var asOfUtc = new DateTimeOffset(2026, 11, 1, 14, 30, 0, TimeSpan.Zero);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plan = new TradePlan(
            Guid.NewGuid(),
            symbol,
            sessionId,
            asOfUtc,
            asOfUtc.AddMinutes(2),
            TradeDirection.Long,
            TradePlanStatus.PlanDefined,
            new PriceZone(98m, 100m),
            99m,
            96m,
            new TradeTarget(106m, Guid.NewGuid(), "origin-event", "confirmation-event", "session-swing-fractal-v1"),
            null,
            3m,
            7m,
            7m / 3m,
            "Momentum Continuation",
            Guid.NewGuid(),
            "session-swing-fractal-v1",
            $"it-readiness-features-{token}",
            "momentum-continuation-v1",
            $"it-readiness-config-{token}",
            "trade-plan-v1",
            MarketDataCapabilities.Unknown,
            []);
        var tracked = new TrackedOpportunity(
            Guid.NewGuid(),
            plan.Id,
            plan.OpportunityId,
            symbol,
            sessionId,
            TradeDirection.Long,
            asOfUtc,
            asOfUtc,
            "tracked-opportunity-selection-v1",
            TrackedOpportunityStatus.Active,
            "EligiblePlanSelected",
            "The newest eligible PlanDefined candidate plan was pinned for readiness tracking.",
            false,
            null,
            null,
            null);
        var assessment = new PreTradeReadinessAssessment(
            Guid.NewGuid(),
            tracked.Id,
            plan.Id,
            plan.OpportunityId,
            symbol,
            sessionId,
            asOfUtc,
            asOfUtc,
            PreTradeReadinessState.EntryReady,
            PreTradeReadinessReason.InsideEntryZone,
            TradeDirection.Long,
            ReadinessContextMode.CurrentSessionOnly,
            99m,
            0m,
            0m,
            true,
            "pre-trade-readiness-v1",
            plan.FeatureSetVersion,
            plan.ConfigurationVersion,
            MarketDataCapabilities.Unknown,
            [new PreTradeReadinessEvidence("EntryConditionsAligned", "Candidate entry conditions are aligned.")]);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }

                setupContext.TradePlans.Add(new TradePlanEntity
                {
                    Id = plan.Id,
                    Symbol = plan.Symbol,
                    SessionId = plan.SessionId,
                    AsOfUtc = plan.AsOfUtc,
                    OpportunityId = plan.OpportunityId,
                    PlanDefinitionVersion = plan.PlanDefinitionVersion,
                    TradePlanJson = JsonSerializer.Serialize(plan, options)
                });
                await setupContext.SaveChangesAsync();
            }
            schemaReady = true;

            // Identical replay is idempotent, including a different audit-only EvaluatedAtUtc.
            await PersistReadiness(connectionString, tracked, assessment);
            await PersistReadiness(connectionString, tracked, assessment);
            await PersistReadiness(
                connectionString,
                tracked,
                assessment with { EvaluatedAtUtc = asOfUtc.AddSeconds(30) });

            await using (var verifyContext = CreateContext(connectionString))
            {
                Assert.Equal(1, await verifyContext.TrackedOpportunities.CountAsync(item => item.Symbol == symbol));
                var storedAssessment = Assert.Single(await verifyContext.PreTradeReadinessAssessments
                    .AsNoTracking().Where(item => item.Symbol == symbol).ToListAsync());
                Assert.Equal(assessment.Id, storedAssessment.Id);
                Assert.Equal("EntryReady", storedAssessment.State);
                Assert.Equal("InsideEntryZone", storedAssessment.Reason);
                Assert.Equal("CurrentSessionOnly", storedAssessment.ContextMode);
                var roundTripped = JsonSerializer.Deserialize<PreTradeReadinessAssessment>(
                    storedAssessment.AssessmentJson, options);
                Assert.NotNull(roundTripped);
                Assert.Equal(assessment.Id, roundTripped.Id);
                Assert.Equal(assessment.CurrentReferencePrice, roundTripped.CurrentReferencePrice);
                Assert.Equal(assessment.Evidence, roundTripped.Evidence);
            }

            // Restart restores the active association and resolves the pinned immutable plan.
            var restored = Assert.Single(await RestoreReadiness(connectionString));
            Assert.Equal(tracked, restored.Tracked);
            Assert.NotNull(restored.Plan);
            Assert.Equal(plan.Id, restored.Plan.Id);
            Assert.Equal(plan.EntryZone, restored.Plan.EntryZone);

            // Only controlled lifecycle fields may change.
            var entryReadyObserved = tracked with { HasEstablishedEntryReady = true };
            await PersistReadiness(connectionString, entryReadyObserved, null);

            // An established entry-ready observation can never be rewritten back to false.
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistReadiness(
                connectionString, tracked, null));
            var terminal = entryReadyObserved with
            {
                Status = TrackedOpportunityStatus.Invalidated,
                TerminalAtUtc = asOfUtc.AddMinutes(1),
                TerminalReasonCode = "InvalidationCrossed",
                TerminalReason = "The tracked candidate reached a terminal readiness condition."
            };
            await PersistReadiness(connectionString, terminal, null);
            await PersistReadiness(connectionString, terminal, null);
            Assert.Empty(await RestoreReadiness(connectionString));

            // Immutable fields, terminal transitions, and assessment payloads reject conflicts.
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistReadiness(
                connectionString, terminal with { PlanId = Guid.NewGuid() }, null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistReadiness(
                connectionString,
                terminal with { Status = TrackedOpportunityStatus.Active, TerminalAtUtc = null },
                null));
            var terminalRewrites = new[]
            {
                terminal with { TerminalAtUtc = terminal.TerminalAtUtc!.Value.AddSeconds(1) },
                terminal with { TerminalAtUtc = null },
                terminal with { TerminalReasonCode = "RewrittenReason" },
                terminal with { TerminalReasonCode = null },
                terminal with { TerminalReason = "Rewritten terminal history." },
                terminal with { TerminalReason = null },
                terminal with { HasEstablishedEntryReady = false }
            };
            foreach (var rewrite in terminalRewrites)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PersistReadiness(connectionString, rewrite, null));
            }

            var neverReady = terminal with { Id = Guid.NewGuid(), HasEstablishedEntryReady = false };
            await PersistReadiness(connectionString, neverReady, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistReadiness(
                connectionString, neverReady with { HasEstablishedEntryReady = true }, null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistReadiness(
                connectionString, terminal, assessment with { CurrentReferencePrice = 99.5m }));
            await using var terminalContext = CreateContext(connectionString);
            var unchanged = await terminalContext.TrackedOpportunities.AsNoTracking()
                .SingleAsync(item => item.Id == terminal.Id);
            Assert.Equal(terminal.TerminalAtUtc, unchanged.TerminalAtUtc);
            Assert.Equal(terminal.TerminalReasonCode, unchanged.TerminalReasonCode);
            Assert.Equal(terminal.TerminalReason, unchanged.TerminalReason);
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(connectionString, [symbol], $"it-readiness-config-{token}", $"it-readiness-config-{token}");
            }
        }
    }

    [Fact]
    public async Task Readiness_states_and_context_modes_round_trip_through_sql()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var sessionId = $"it-states-session-{token}";
        var asOfUtc = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var planId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();
        var tracked = new TrackedOpportunity(
            Guid.NewGuid(),
            planId,
            opportunityId,
            symbol,
            sessionId,
            TradeDirection.Long,
            asOfUtc,
            asOfUtc,
            "tracked-opportunity-selection-v1",
            TrackedOpportunityStatus.Active,
            "EligiblePlanSelected",
            "The newest eligible PlanDefined candidate plan was pinned for readiness tracking.",
            false,
            null,
            null,
            null);
        PreTradeReadinessAssessment Assessment(
            int minute,
            PreTradeReadinessState state,
            PreTradeReadinessReason reason,
            ReadinessContextMode contextMode,
            decimal? currentReferencePrice,
            decimal? distanceToEntry,
            decimal? distanceToEntryRiskMultiple,
            bool isInsideEntryZone) => new(
            Guid.NewGuid(),
            tracked.Id,
            planId,
            opportunityId,
            symbol,
            sessionId,
            asOfUtc.AddMinutes(minute),
            asOfUtc.AddMinutes(minute),
            state,
            reason,
            TradeDirection.Long,
            contextMode,
            currentReferencePrice,
            distanceToEntry,
            distanceToEntryRiskMultiple,
            isInsideEntryZone,
            "pre-trade-readiness-v1",
            $"it-states-features-{token}",
            $"it-states-config-{token}",
            MarketDataCapabilities.Unknown,
            [new PreTradeReadinessEvidence(state.ToString(), $"Representative {state} assessment.")]);
        // Historical v1 payloads remain readable; the v2 evaluator no longer claims prior enhancement.
        var assessments = new[]
        {
            Assessment(1, PreTradeReadinessState.Watching, PreTradeReadinessReason.ConfirmationNotAligned,
                ReadinessContextMode.CurrentSessionOnly, 104.5m, 4.5m, 1.5m, false),
            Assessment(2, PreTradeReadinessState.SetupForming, PreTradeReadinessReason.ApproachingEntry,
                ReadinessContextMode.PriorSessionEnhanced, 102.4m, 2.4m, 0.8m, false),
            Assessment(3, PreTradeReadinessState.EntryZoneActive, PreTradeReadinessReason.ConfirmationNotAligned,
                ReadinessContextMode.PriorSessionEnhanced, 99.750m, 0m, 0m, true),
            Assessment(4, PreTradeReadinessState.EntryReady, PreTradeReadinessReason.InsideEntryZone,
                ReadinessContextMode.PriorSessionEnhanced, 99.125m, 0m, 0m, true),
            // Partial/unavailable prior context is persisted as the explicit CurrentSessionOnly
            // fallback mode, never as confirmed prior-session levels.
            Assessment(5, PreTradeReadinessState.Suspended, PreTradeReadinessReason.FeedNotLive,
                ReadinessContextMode.CurrentSessionOnly, null, null, null, false),
            Assessment(6, PreTradeReadinessState.NoLongerActionable, PreTradeReadinessReason.OpportunityExpired,
                ReadinessContextMode.CurrentSessionOnly, 100.9m, 0.9m, 0.3m, false)
        };
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }
            }
            schemaReady = true;

            foreach (var assessment in assessments)
            {
                await PersistReadiness(connectionString, tracked, assessment);
            }

            await using var verifyContext = CreateContext(connectionString);
            Assert.Equal(1, await verifyContext.TrackedOpportunities.CountAsync(item => item.Symbol == symbol));
            var storedAssessments = await verifyContext.PreTradeReadinessAssessments.AsNoTracking()
                .Where(item => item.Symbol == symbol)
                .OrderBy(item => item.AsOfUtc)
                .ToListAsync();
            Assert.Equal(assessments.Length, storedAssessments.Count);
            foreach (var (expected, stored) in assessments.Zip(storedAssessments))
            {
                Assert.Equal(expected.Id, stored.Id);
                Assert.Equal(tracked.Id, stored.TrackedOpportunityId);
                Assert.Equal(planId, stored.PlanId);
                Assert.Equal(expected.AsOfUtc, stored.AsOfUtc);
                Assert.Equal(expected.State.ToString(), stored.State);
                Assert.Equal(expected.Reason.ToString(), stored.Reason);
                Assert.Equal(expected.ContextMode.ToString(), stored.ContextMode);
                Assert.Equal("pre-trade-readiness-v1", stored.ReadinessDefinitionVersion);
                // The persisted JSON is the exact serialized assessment, preserving decimals.
                Assert.Equal(JsonSerializer.Serialize(expected, options), stored.AssessmentJson);
                var roundTripped = JsonSerializer.Deserialize<PreTradeReadinessAssessment>(
                    stored.AssessmentJson, options);
                Assert.NotNull(roundTripped);
                Assert.Equal(expected.CurrentReferencePrice, roundTripped.CurrentReferencePrice);
                Assert.Equal(expected.DistanceToEntry, roundTripped.DistanceToEntry);
                Assert.Equal(expected.DistanceToEntryRiskMultiple, roundTripped.DistanceToEntryRiskMultiple);
                Assert.Equal(expected.IsInsideEntryZone, roundTripped.IsInsideEntryZone);
                Assert.Equal(expected.ContextMode, roundTripped.ContextMode);
                Assert.Equal(expected.FeatureSetVersion, roundTripped.FeatureSetVersion);
                Assert.Equal(expected.ConfigurationVersion, roundTripped.ConfigurationVersion);
                Assert.Equal(expected.MarketData, roundTripped.MarketData);
                Assert.Equal(expected.Evidence, roundTripped.Evidence);
            }
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(connectionString, [symbol], $"it-states-config-{token}", $"it-states-config-{token}");
            }
        }
    }

    [Fact]
    public async Task Restart_restores_the_pinned_plan_and_reevaluates_readiness_from_current_state()
    {
        var connectionString = GetTestConnectionString();
        var token = Guid.NewGuid().ToString("N")[..16];
        var symbol = $"IT{token}";
        var sessionId = $"it-restart-session-{token}";
        var asOfUtc = new DateTimeOffset(2026, 11, 1, 16, 0, 0, TimeSpan.Zero);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var configuration = new ScannerConfiguration
        {
            Version = $"it-restart-config-{token}",
            FeatureSetVersion = $"it-restart-features-{token}"
        };
        var opportunityId = Guid.NewGuid();
        var plan = CreateReadinessPlan(symbol, sessionId, asOfUtc, opportunityId, configuration);
        var candidate = CreateReadinessCandidate(opportunityId, symbol, sessionId, asOfUtc, configuration);
        var schemaReady = false;
        try
        {
            await using (var setupContext = CreateContext(connectionString))
            {
                if (!await setupContext.Database.EnsureCreatedAsync())
                {
                    await setupContext.Database.MigrateAsync();
                }

                setupContext.TradePlans.Add(ToPlanEntity(plan, options));
                await setupContext.SaveChangesAsync();
            }
            schemaReady = true;

            // Host instance A selects and pins the eligible plan and persists tracked + readiness.
            var firstTracker = new PreTradeReadinessTracker();
            var firstEvaluation = firstTracker.Evaluate(
                CreateReadinessSnapshot(symbol, sessionId, 99m, asOfUtc, configuration),
                candidate,
                plan,
                ActivePublication(asOfUtc),
                null,
                configuration,
                asOfUtc);
            Assert.NotNull(firstEvaluation.Projection);
            Assert.Equal(PreTradeReadinessState.EntryReady, firstEvaluation.Projection.Assessment.State);
            Assert.Equal(plan.Id, firstEvaluation.Projection.Tracked.PlanId);
            await PersistReadiness(
                connectionString, firstEvaluation.PersistTracked!, firstEvaluation.PersistAssessment);

            // A newer PlanDefined plan exists in SQL before the restart; it must not be selected.
            var newerPlan = plan with
            {
                Id = Guid.NewGuid(),
                AsOfUtc = asOfUtc.AddMinutes(5),
                ValidUntilUtc = asOfUtc.AddMinutes(7)
            };
            await using (var newerContext = CreateContext(connectionString))
            {
                newerContext.TradePlans.Add(ToPlanEntity(newerPlan, options));
                await newerContext.SaveChangesAsync();
            }

            // Host instance B restores the association and resolves the exact pinned plan.
            var restored = Assert.Single(
                await RestoreReadiness(connectionString),
                item => item.Tracked.Symbol == symbol);
            Assert.Equal(firstEvaluation.PersistTracked, restored.Tracked);
            Assert.True(restored.Tracked.HasEstablishedEntryReady);
            Assert.NotNull(restored.Plan);
            Assert.Equal(plan.Id, restored.Plan.Id);
            Assert.NotEqual(newerPlan.Id, restored.Plan.Id);
            Assert.Equal(plan.EntryZone, restored.Plan.EntryZone);
            Assert.Equal(plan.InvalidationPrice, restored.Plan.InvalidationPrice);
            Assert.Equal(plan.Target1, restored.Plan.Target1);

            // Historical readiness is not blindly restored: the restored candidate is reevaluated
            // from the current trustworthy market state, still against the pinned geometry.
            var laterAsOfUtc = asOfUtc.AddSeconds(45);
            var restartedTracker = new PreTradeReadinessTracker();
            restartedTracker.Restore([restored]);
            var reevaluated = restartedTracker.Evaluate(
                CreateReadinessSnapshot(symbol, sessionId, 103.5m, laterAsOfUtc, configuration),
                candidate,
                newerPlan,
                ActivePublication(laterAsOfUtc),
                null,
                configuration,
                laterAsOfUtc);
            Assert.NotNull(reevaluated.Projection);
            Assert.Equal(restored.Tracked.Id, reevaluated.Projection.Tracked.Id);
            Assert.Equal(plan.Id, reevaluated.Projection.Tracked.PlanId);
            Assert.Equal(plan.Id, reevaluated.Projection.Assessment.PlanId);
            Assert.Equal(laterAsOfUtc, reevaluated.Projection.Assessment.AsOfUtc);
            Assert.Equal(PreTradeReadinessState.Watching, reevaluated.Projection.Assessment.State);
            Assert.NotEqual(firstEvaluation.Projection.Assessment.Id, reevaluated.Projection.Assessment.Id);
            Assert.True(reevaluated.Projection.Tracked.HasEstablishedEntryReady);
            await PersistReadiness(
                connectionString, reevaluated.PersistTracked!, reevaluated.PersistAssessment);

            await using var verifyContext = CreateContext(connectionString);
            Assert.Equal(1, await verifyContext.TrackedOpportunities.CountAsync(item => item.Symbol == symbol));
            var storedAssessments = await verifyContext.PreTradeReadinessAssessments.AsNoTracking()
                .Where(item => item.Symbol == symbol)
                .OrderBy(item => item.AsOfUtc)
                .ToListAsync();
            Assert.Equal(2, storedAssessments.Count);
            Assert.Equal("EntryReady", storedAssessments[0].State);
            Assert.Equal("Watching", storedAssessments[1].State);
            Assert.All(storedAssessments, item => Assert.Equal(plan.Id, item.PlanId));

            // A restart/idle tick can persist expiry without receiving another market event.
            var expired = Assert.Single(restartedTracker.Reconcile(plan.ValidUntilUtc.AddDays(1)));
            Assert.Equal(restored.Tracked.Id, expired.Id);
            Assert.Equal(plan.ValidUntilUtc, expired.TerminalAtUtc);
            await PersistReadiness(connectionString, expired, null);
            Assert.DoesNotContain(await RestoreReadiness(connectionString), item => item.Tracked.Symbol == symbol);
            Assert.Equal(2, await verifyContext.PreTradeReadinessAssessments.CountAsync(item => item.Symbol == symbol));
        }
        finally
        {
            if (schemaReady)
            {
                await Cleanup(connectionString, [symbol], configuration.Version, configuration.Version);
            }
        }
    }

    private static string GetTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETOPPORTUNITY_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server integration test was not run: set MARKETOPPORTUNITY_SQL_TEST_CONNECTION to a dedicated SQL Server test database.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || !builder.InitialCatalog.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require a dedicated database whose name contains 'test'.");
        }

        return builder.ConnectionString;
    }

    private static MarketOpportunityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MarketOpportunityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MarketOpportunityDbContext(options);
    }

    private static async Task Persist(
        string connectionString,
        ScannerConfiguration configuration,
        IReadOnlyCollection<MarketEvent> events,
        (FeatureSnapshot Features, Opportunity Opportunity, TradePlan TradePlan) result)
    {
        await using var context = CreateContext(connectionString);
        var store = new EfScannerStore(context);
        await store.PersistRunAsync(
            configuration,
            events,
            result.Features,
            result.Opportunity,
            result.TradePlan,
            CancellationToken.None);
    }

    private static async Task PersistReadiness(
        string connectionString,
        TrackedOpportunity tracked,
        PreTradeReadinessAssessment? assessment)
    {
        await using var context = CreateContext(connectionString);
        var store = new EfPreTradeReadinessStore(context);
        await store.PersistAsync(tracked, assessment, CancellationToken.None);
    }

    private static async Task PersistMarketSituation(
        string connectionString,
        MarketSituationAssessment assessment)
    {
        await using var context = CreateContext(connectionString);
        await new EfMarketSituationStore(context).PersistAsync(assessment, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<TrackedOpportunitySnapshot>> RestoreReadiness(
        string connectionString)
    {
        await using var context = CreateContext(connectionString);
        var store = new EfPreTradeReadinessStore(context);
        return await store.RestoreActiveAsync(CancellationToken.None);
    }

    private static (FeatureSnapshot Features, Opportunity Opportunity, TradePlan TradePlan) Analyze(
        IReadOnlyCollection<MarketEvent> events,
        ScannerConfiguration configuration)
    {
        var first = events.First();
        var state = new SymbolEventState(first.Symbol, first.SessionId, configuration.MaximumStateEvents);
        foreach (var marketEvent in events)
        {
            state.Add(marketEvent);
        }

        var asOfUtc = events.Max(item => item.EventTimeUtc);
        var features = new FeatureCalculator().Calculate(
            state,
            asOfUtc,
            configuration,
            new MarketDataCapabilities(
                "IntegrationFixture",
                "Synthetic",
                MarketDataCoverage.Synthetic,
                false,
                true,
                true));
        var opportunity = new MomentumContinuationDetector().Evaluate(features, configuration);
        var tradePlan = new TradePlanBuilder().Build(features, opportunity);
        return (features, opportunity, tradePlan);
    }

    private static PublicationDecision ActivePublication(DateTimeOffset evaluatedAtUtc) => new(
        OpportunityLifecycleState.Active,
        "PublicationAllowed",
        "Market data is healthy and the candidate remains within its validity period.",
        evaluatedAtUtc);

    private static TradePlan CreateReadinessPlan(
        string symbol,
        string sessionId,
        DateTimeOffset asOfUtc,
        Guid opportunityId,
        ScannerConfiguration configuration) => new(
        Guid.NewGuid(),
        symbol,
        sessionId,
        asOfUtc,
        asOfUtc.AddMinutes(2),
        TradeDirection.Long,
        TradePlanStatus.PlanDefined,
        new PriceZone(98m, 100m),
        99m,
        96m,
        new TradeTarget(106m, Guid.NewGuid(), "origin-event", "confirmation-event", "session-swing-fractal-v1"),
        null,
        3m,
        7m,
        7m / 3m,
        "Momentum Continuation",
        opportunityId,
        "session-swing-fractal-v1",
        configuration.FeatureSetVersion,
        configuration.SetupDefinitionVersion,
        configuration.Version,
        "trade-plan-v1",
        MarketDataCapabilities.Unknown,
        []);

    private static Opportunity CreateReadinessCandidate(
        Guid opportunityId,
        string symbol,
        string sessionId,
        DateTimeOffset asOfUtc,
        ScannerConfiguration configuration) => new(
        opportunityId,
        symbol,
        sessionId,
        asOfUtc,
        asOfUtc.AddMinutes(2),
        "Momentum Continuation",
        SetupBias.Long,
        OpportunityClassification.ProbableLong,
        90,
        [new OpportunityEvidence("Momentum", "Positive momentum.")],
        [],
        [],
        "Below session VWAP.",
        100m,
        configuration.FeatureSetVersion,
        configuration.SetupDefinitionVersion,
        configuration.Version,
        MarketDataCapabilities.Unknown);

    private static FeatureSnapshot CreateReadinessSnapshot(
        string symbol,
        string sessionId,
        decimal price,
        DateTimeOffset asOfUtc,
        ScannerConfiguration configuration) => new(
        Guid.NewGuid(),
        symbol,
        sessionId,
        asOfUtc,
        configuration.FeatureSetVersion,
        price,
        price - 0.01m,
        price + 0.01m,
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
        price,
        price,
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

    private static TradePlanEntity ToPlanEntity(TradePlan plan, JsonSerializerOptions options) => new()
    {
        Id = plan.Id,
        Symbol = plan.Symbol,
        SessionId = plan.SessionId,
        AsOfUtc = plan.AsOfUtc,
        OpportunityId = plan.OpportunityId,
        PlanDefinitionVersion = plan.PlanDefinitionVersion,
        TradePlanJson = JsonSerializer.Serialize(plan, options)
    };

    private static MarketEvent[] CreateEvents(string symbol, string sessionId, decimal startPrice = 100m)
    {
        var asOfUtc = new DateTimeOffset(2026, 10, 23, 14, 30, 42, TimeSpan.Zero);
        return
        [
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-30), asOfUtc, "IntegrationFixture", $"trade-1-{sessionId}", startPrice, 10),
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-20), asOfUtc, "IntegrationFixture", $"trade-2-{sessionId}", startPrice + 1m, 20),
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-10), asOfUtc, "IntegrationFixture", $"trade-3-{sessionId}", startPrice + 2m, 30),
            new TradeEvent(symbol, sessionId, asOfUtc.AddSeconds(-1), asOfUtc, "IntegrationFixture", $"trade-4-{sessionId}", startPrice + 3m, 40),
            new QuoteEvent(symbol, sessionId, asOfUtc, asOfUtc, "IntegrationFixture", $"quote-1-{sessionId}", startPrice + 2.99m, startPrice + 3.01m, 500, 500)
        ];
    }

    private static async Task Cleanup(
        string connectionString,
        IReadOnlyCollection<string> symbols,
        string configurationVersion,
        string changedConfigurationVersion)
    {
        await using var context = CreateContext(connectionString);
        await context.PreTradeReadinessAssessments.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.TrackedOpportunities.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.TradePlans.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.Opportunities.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.FeatureSnapshots.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.MarketEvents.Where(item => symbols.Contains(item.Symbol)).ExecuteDeleteAsync();
        await context.ConfigurationSnapshots
            .Where(item => item.Version == configurationVersion || item.Version == changedConfigurationVersion)
            .ExecuteDeleteAsync();
    }
}
