using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Analytics;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MarketOpportunity.IntegrationTests;

public sealed class StructureWebApplicationTests
{
    [Fact]
    public async Task Forensic_candidate_api_pages_bounded_immutable_evidence()
    {
        await using var factory = new StructureApplicationFactory(useFixtureSource: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstResponse = await client.GetAsync(
            $"/api/v1/market-situations/{InMemoryMarketSituationEvidenceStore.KnownAssessmentId}/candidates" +
            "?pageSize=3",
            timeout.Token);
        using (firstResponse)
        using (var firstJson = JsonDocument.Parse(
                   await firstResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(3, firstJson.RootElement.GetProperty("returnedCount").GetInt32());
            Assert.Equal(0, firstJson.RootElement.GetProperty("firstOrdinal").GetInt32());
            Assert.Equal(2, firstJson.RootElement.GetProperty("lastOrdinal").GetInt32());
            Assert.True(firstJson.RootElement.GetProperty("hasMore").GetBoolean());
            Assert.Equal(2, firstJson.RootElement.GetProperty("nextAfterOrdinal").GetInt32());
        }

        var secondResponse = await client.GetAsync(
            $"/api/v1/market-situations/{InMemoryMarketSituationEvidenceStore.KnownAssessmentId}/candidates" +
            "?afterOrdinal=2&pageSize=3",
            timeout.Token);
        using (secondResponse)
        using (var secondJson = JsonDocument.Parse(
                   await secondResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            Assert.Equal(
                [3, 4, 5],
                secondJson.RootElement.GetProperty("members").EnumerateArray()
                    .Select(item => item.GetProperty("ordinal").GetInt32())
                    .ToArray());
        }

        using var unknown = await client.GetAsync(
            $"/api/v1/market-situations/{Guid.NewGuid()}/candidates",
            timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var legacy = await client.GetAsync(
            $"/api/v1/market-situations/{InMemoryMarketSituationEvidenceStore.LegacyAssessmentId}/candidates",
            timeout.Token);
        Assert.Equal(HttpStatusCode.Conflict, legacy.StatusCode);
        Assert.Contains(
            "LegacyCandidateEvidenceUnavailable",
            await legacy.Content.ReadAsStringAsync(timeout.Token),
            StringComparison.Ordinal);

        using var invalidCursor = await client.GetAsync(
            $"/api/v1/market-situations/{InMemoryMarketSituationEvidenceStore.KnownAssessmentId}/candidates" +
            "?afterOrdinal=-2",
            timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);

        using var invalidPageSize = await client.GetAsync(
            $"/api/v1/market-situations/{InMemoryMarketSituationEvidenceStore.KnownAssessmentId}/candidates" +
            "?pageSize=65",
            timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSize.StatusCode);

        using var oversized = await client.GetAsync(
            $"/api/v1/market-situations/{InMemoryMarketSituationEvidenceStore.OversizedAssessmentId}/candidates" +
            "?pageSize=1",
            timeout.Token);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Contains(
            "CandidateEvidencePageTooLarge",
            await oversized.Content.ReadAsStringAsync(timeout.Token),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hosted_normal_opportunity_and_dashboard_are_bounded_for_large_candidate_set()
    {
        await using var factory = new StructureApplicationFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        JsonDocument? smallJson = null;
        while (smallJson is null && !timeout.IsCancellationRequested)
        {
            using var response = await client.GetAsync("/api/v1/opportunities", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var json = await response.Content.ReadAsStringAsync(timeout.Token);
                var document = JsonDocument.Parse(json);
                if (document.RootElement.GetArrayLength() > 0)
                {
                    smallJson = document;
                    break;
                }

                document.Dispose();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(smallJson);
        using (smallJson)
        {
            var smallText = smallJson.RootElement.GetRawText();
            var smallBytes = Encoding.UTF8.GetByteCount(smallText);
            var scanner = factory.Services.GetRequiredService<ScannerState>();
            var state = scanner.Read();
            var result = Assert.Single(state.Results);
            var existing = Assert.IsType<MarketSituationReadModel>(result.MarketSituation);
            Assert.NotNull(existing.NearestSupportLevel);
            var selected = existing.NearestSupportLevel;
            var candidateLevels = Enumerable.Range(1, 100_005)
                .Select(index => selected with
                {
                    Id = new Guid(index, 0, 0, new byte[8]),
                    SourceStructuralIdentity = new Guid(index, 1, 0, new byte[8]),
                    Price = index == 1 ? selected.Price : 200m + index * 0.01m
                })
                .ToArray();
            var candidateSet = MarketSituationCandidateSet.Create(
                existing.FeatureSnapshotId,
                existing.PriorSessionContextId,
                existing.Symbol,
                existing.SessionId,
                existing.AsOfUtc,
                existing.ContextMode,
                candidateLevels,
                existing.TestZonePercent,
                existing.ApproachDistancePercent,
                existing.DefinitionVersion,
                existing.FeatureSetVersion,
                existing.ConfigurationVersion,
                existing.MarketData,
                existing.DataQuality,
                existing.MarketStructureStatus,
                existing.HasCompleteCurrentSessionData);
            var largeReadModel = existing with
            {
                CandidateSetId = candidateSet.CandidateSetId,
                SupportCandidateCount = candidateSet.SupportCandidateCount,
                ResistanceCandidateCount = candidateSet.ResistanceCandidateCount,
                TotalCandidateCount = candidateSet.TotalCandidateCount
            };
            scanner.SetResults(
                state.Status,
                [result with { MarketSituation = largeReadModel }],
                result.Features.AsOfUtc);

            using var largeResponse = await client.GetAsync("/api/v1/opportunities", timeout.Token);
            var largeText = await largeResponse.Content.ReadAsStringAsync(timeout.Token);
            using var largeJson = JsonDocument.Parse(largeText);
            var opportunityItem = Assert.Single(largeJson.RootElement.EnumerateArray());
            var marketSituation = opportunityItem.GetProperty("marketSituation");
            var largeBytes = Encoding.UTF8.GetByteCount(largeText);
            Assert.Equal(HttpStatusCode.OK, largeResponse.StatusCode);
            Assert.Equal(100_005, marketSituation.GetProperty("totalCandidateCount").GetInt32());
            Assert.False(marketSituation.TryGetProperty("eligibleLevels", out _));
            Assert.True(largeBytes <= smallBytes + 256);
            Assert.True(largeBytes < MarketSituationReadModelFactory.MaximumNormalSerializedBytes);
            var normalProperties = scanner.Read().Results.Single().MarketSituation!
                .GetType().GetProperties();
            Assert.DoesNotContain(
                normalProperties,
                item => item.Name is "CandidateSet" or "EligibleLevels");
            Assert.DoesNotContain(
                normalProperties,
                item => item.PropertyType == typeof(MarketSituationCandidateSet));

            using var detailResponse = await client.GetAsync(
                $"/api/v1/opportunities/{opportunityItem.GetProperty("opportunity").GetProperty("id").GetGuid()}",
                timeout.Token);
            using var detailJson = JsonDocument.Parse(
                await detailResponse.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            Assert.False(
                detailJson.RootElement.GetProperty("marketSituation")
                    .TryGetProperty("eligibleLevels", out _));

            var html = await client.GetStringAsync("/", timeout.Token);
            Assert.Contains("Candidate evidence: 100005 eligible levels", html);
            Assert.Contains("Selection: Support", html);
            Assert.Contains("Source session", html);
            Assert.DoesNotContain("Eligible structural reference levels", html);
            Assert.DoesNotContain(candidateLevels[^1].Id.ToString(), html, StringComparison.Ordinal);
            Console.WriteLine(
                $"Normal MarketSituation API: small={smallBytes} bytes, 100005-candidate=" +
                $"{largeBytes} bytes, budget={MarketSituationReadModelFactory.MaximumNormalSerializedBytes}.");
        }
    }

    [Fact]
    public async Task Hosted_fixture_api_and_dashboard_keep_structure_descriptive_and_offline()
    {
        await using var factory = new StructureApplicationFactory(useFixtureSource: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        HttpResponseMessage? response = null;
        while (!timeout.IsCancellationRequested)
        {
            response?.Dispose();
            response = await client.GetAsync(
                "/api/v1/symbols/MOE/snapshot?sessionId=fixture-2026-10-22", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(response);
        using (response)
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token)))
        {
            var root = json.RootElement;
            Assert.Equal("MOE", root.GetProperty("symbol").GetString());
            Assert.Equal("fixture-2026-10-22", root.GetProperty("sessionId").GetString());
            Assert.Equal("features-v16", root.GetProperty("featureSetVersion").GetString());
            Assert.Equal("Available", root.GetProperty("marketStructureStatus").GetString());
            Assert.Equal("Insufficient", root.GetProperty("structureState").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("nearestSupportCandidate").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("nearestResistanceCandidate").ValueKind);
        }

        using (var opportunitiesResponse = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var opportunitiesJson = JsonDocument.Parse(
                   await opportunitiesResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunitiesResponse.StatusCode);
            var marketSituation = opportunitiesJson.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("marketSituation"))
                .First(item => item.GetProperty("sessionId").GetString() == "fixture-2026-10-22");
            Assert.Equal("market-situation-v1", marketSituation.GetProperty("definitionVersion").GetString());
            Assert.Equal("CurrentSessionOnly", marketSituation.GetProperty("contextMode").GetString());
            Assert.False(marketSituation.TryGetProperty("eligibleLevels", out _));
            Assert.True(marketSituation.GetProperty("candidateSetId").GetString()?.Length == 64);
            Assert.True(marketSituation.GetProperty("totalCandidateCount").GetInt32() >= 0);
        }

        using (var reactionsResponse = await client.GetAsync(
                   "/api/v1/reactions?symbol=MOE&sessionId=fixture-2026-10-22",
                   timeout.Token))
        using (var json = JsonDocument.Parse(await reactionsResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, reactionsResponse.StatusCode);
            Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        }

        using (var priorResponse = await client.GetAsync(
                   "/api/v1/symbols/MOE/prior-context?sessionId=fixture-2026-10-23",
                   timeout.Token))
        {
            var priorJson = await priorResponse.Content.ReadAsStringAsync(timeout.Token);
            using var json = JsonDocument.Parse(priorJson);
            Assert.Equal(HttpStatusCode.OK, priorResponse.StatusCode);
            var contextView = json.RootElement;
            var context = contextView.GetProperty("context");
            Assert.Equal("fixture-2026-10-22", context.GetProperty("priorSessionId").GetString());
            Assert.Equal("Partial", context.GetProperty("completeness").GetString());
            Assert.Equal("prior-session-context-v1", context.GetProperty("contextDefinitionVersion").GetString());
            Assert.Equal("RecordedFixture", context.GetProperty("marketData").GetProperty("provider").GetString());
            Assert.Equal("recorded-fixture-v1", context.GetProperty("historicalSource").GetString());
            Assert.Equal(JsonValueKind.Null, context.GetProperty("priorSessionHigh").ValueKind);
            Assert.Equal(JsonValueKind.Null, context.GetProperty("priorSessionClose").ValueKind);
            Assert.True(context.GetProperty("observedHigh").GetDecimal() > 0);
            Assert.Equal("MOE", context.GetProperty("symbol").GetString());
            Assert.True(contextView.GetProperty("currentReferencePrice").GetDecimal() > 0);
            Assert.DoesNotContain("BUY", priorJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELL", priorJson, StringComparison.OrdinalIgnoreCase);
        }

        using (var unavailableResponse = await client.GetAsync(
                   "/api/v1/symbols/MOE/prior-context?sessionId=fixture-2026-10-22",
                   timeout.Token))
        using (var json = JsonDocument.Parse(await unavailableResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, unavailableResponse.StatusCode);
            Assert.Equal(
                "Unavailable",
                json.RootElement.GetProperty("context").GetProperty("completeness").GetString());
        }

        using (var configurationResponse = await client.GetAsync(
                   "/api/v1/configuration/effective",
                   timeout.Token))
        using (var json = JsonDocument.Parse(await configurationResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, configurationResponse.StatusCode);
            Assert.Equal("scanner-config-v20", json.RootElement.GetProperty("version").GetString());
            var marketSituation = json.RootElement.GetProperty("marketSituation");
            Assert.Equal(0.1m, marketSituation.GetProperty("testZonePercent").GetDecimal());
            Assert.Equal(0.25m, marketSituation.GetProperty("approachDistancePercent").GetDecimal());
            var marketReaction = json.RootElement.GetProperty("marketReaction");
            Assert.Equal(0.1m, marketReaction.GetProperty("confirmationDistancePercent").GetDecimal());
            Assert.Equal(300, marketReaction.GetProperty("maxEpisodeDurationSeconds").GetInt32());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        var overview = ExtractSymbolOverview(html);
        Assert.True(html.IndexOf("SYMBOL OVERVIEW", StringComparison.Ordinal)
            < html.IndexOf("MARKET STRUCTURE", StringComparison.Ordinal));
        Assert.Contains("href=\"#symbol-", overview);
        Assert.Contains("id=\"symbol-", html);
        Assert.Contains("MARKET STRUCTURE", html);
        Assert.Contains("TRADE PLAN", html);
        Assert.Contains("PRIOR SESSION", html);
        Assert.Contains("PRE-TRADE READINESS", html);
        Assert.Contains("MARKET SITUATION", html);
        Assert.Contains("MARKET REACTION", html);
        Assert.Contains("Research context is descriptive", html);
        Assert.Contains("No tracked candidate opportunity", html);
        Assert.Contains("Support and resistance are historical reference areas, not guarantees.", html);
        Assert.Contains("Insufficient", html);
        Assert.DoesNotContain("probability of profit", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Buy now", html);
        Assert.DoesNotContain("Sell now", html);
        Assert.DoesNotContain("Short now", html);
        Assert.DoesNotContain("Guaranteed", html);
    }

    [Fact]
    public async Task Hosted_dashboard_overview_deduplicates_current_symbols_and_links_to_details()
    {
        await using var factory = new StructureApplicationFactory(useMultipleSymbols: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        JsonDocument? opportunitiesJson = null;
        while (!timeout.IsCancellationRequested)
        {
            opportunitiesJson?.Dispose();
            using var response = await client.GetAsync("/api/v1/opportunities", timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            opportunitiesJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (opportunitiesJson.RootElement.GetArrayLength() >= 3)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(opportunitiesJson);
        using (opportunitiesJson)
        {
            Assert.Equal(3, opportunitiesJson.RootElement.GetArrayLength());
        }

        var scanner = factory.Services.GetRequiredService<ScannerState>();
        var readModel = scanner.Read();
        var aapl = Assert.Single(readModel.Results, item => item.Opportunity.Symbol == "AAPL");
        var results = readModel.Results
            .Select(item => ReferenceEquals(item, aapl)
                ? item with { Readiness = WithReadinessState(item, PreTradeReadinessState.EntryReady) }
                : item)
            .ToArray();
        scanner.SetResults(readModel.Status, results, aapl.Opportunity.AsOfUtc);

        var html = await client.GetStringAsync("/", timeout.Token);
        var overview = ExtractSymbolOverview(html);
        Assert.Equal(3, overview.Split("symbol-overview-card", StringSplitOptions.None).Length - 1);
        Assert.True(overview.IndexOf("AAPL", StringComparison.Ordinal)
            < overview.IndexOf("MSFT", StringComparison.Ordinal));
        Assert.True(overview.IndexOf("MSFT", StringComparison.Ordinal)
            < overview.IndexOf("NVDA", StringComparison.Ordinal));
        Assert.Contains("ENTRY READY", overview);
        Assert.Contains("NO TRACKED CANDIDATE", overview);
        Assert.Contains("href=\"#symbol-", overview);
        Assert.Contains("id=\"symbol-", html);
        Assert.DoesNotContain("BUY NOW", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELL NOW", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GUARANTEED", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("probability of profit", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRE-TRADE READINESS", html);
        Assert.Contains("MARKET SITUATION", html);
        Assert.Contains("MARKET REACTION", html);
        Assert.Contains("TRADE PLAN", html);
    }

    [Fact]
    public async Task Hosted_live_restart_marks_restored_episode_continuity_unknown_before_new_observations()
    {
        var reactionStore = new InMemoryReactionStore();
        reactionStore.Seed(StructureApplicationFactory.CreateActiveEpisode());
        await using var factory = new StructureApplicationFactory(
            useRestartSource: true,
            reactionStore: reactionStore);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        JsonDocument? responseJson = null;
        while (!timeout.IsCancellationRequested)
        {
            responseJson?.Dispose();
            using var response = await client.GetAsync(
                "/api/v1/reactions?symbol=RSTART&sessionId=restart-session",
                timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (responseJson.RootElement.GetArrayLength() > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(responseJson);
        using (responseJson)
        {
            var reaction = Assert.Single(responseJson.RootElement.EnumerateArray());
            Assert.Equal("ContinuityUnknown", reaction.GetProperty("assessment").GetProperty("status").GetString());
            Assert.Equal("restart-session", reaction.GetProperty("assessment").GetProperty("sessionId").GetString());
            Assert.Equal(JsonValueKind.Null, reaction.GetProperty("assessment").GetProperty("reaction").ValueKind);
        }

        using var wrongSession = await client.GetAsync(
            "/api/v1/reactions?symbol=RSTART&sessionId=other-session",
            timeout.Token);
        using var wrongSessionJson = JsonDocument.Parse(await wrongSession.Content.ReadAsStringAsync(timeout.Token));
        Assert.Empty(wrongSessionJson.RootElement.EnumerateArray());
        Assert.Contains(
            reactionStore.Persisted,
            evaluation => evaluation.Episode.Status == MarketReactionEpisodeStatus.ContinuityUnknown);
    }

    [Fact]
    public async Task Hosted_pipeline_confirms_reaction_and_exposes_event_time_research_context()
    {
        await using var factory = new StructureApplicationFactory(
            reactionSource: new ReactionMarketDataSource());
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        JsonDocument? reactionsJson = null;
        while (!timeout.IsCancellationRequested)
        {
            reactionsJson?.Dispose();
            using var response = await client.GetAsync(
                "/api/v1/reactions?symbol=REACTION&sessionId=reaction-session",
                timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            reactionsJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (reactionsJson.RootElement.EnumerateArray().Any(reaction =>
                    reaction.GetProperty("assessment").GetProperty("status").GetString() == "Confirmed"))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(reactionsJson);
        DateTimeOffset reactionAtUtc;
        using (reactionsJson)
        {
            var reaction = Assert.Single(reactionsJson.RootElement.EnumerateArray());
            var assessment = reaction.GetProperty("assessment");
            Assert.Equal("Confirmed", assessment.GetProperty("status").GetString());
            Assert.Equal("SupportHeld", assessment.GetProperty("reaction").GetString());
            Assert.Equal(100.3m, assessment.GetProperty("reactionPrice").GetDecimal());
            reactionAtUtc = assessment.GetProperty("reactionAtUtc").GetDateTimeOffset();
            Assert.Equal(new DateTimeOffset(2026, 11, 6, 18, 30, 1, TimeSpan.Zero), reactionAtUtc);
            var researchContext = reaction.GetProperty("researchContext");
            Assert.Equal(
                "market-reaction-research-context-v1",
                researchContext.GetProperty("definitionVersion").GetString());
            Assert.Equal(reactionAtUtc, researchContext.GetProperty("asOfUtc").GetDateTimeOffset());
            Assert.Equal("features-v16", researchContext.GetProperty("featureSetVersion").GetString());
            Assert.Equal("Available", researchContext.GetProperty("marketSituationStatus").GetString());
        }

        using (var opportunitiesResponse = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var opportunitiesJson = JsonDocument.Parse(
                   await opportunitiesResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunitiesResponse.StatusCode);
            var opportunity = Assert.Single(opportunitiesJson.RootElement.EnumerateArray());
            var reaction = Assert.Single(opportunity.GetProperty("marketReactions").EnumerateArray());
            Assert.Equal(reactionAtUtc,
                reaction.GetProperty("assessment").GetProperty("reactionAtUtc").GetDateTimeOffset());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("Support Held", html);
        Assert.Contains("Support Held", ExtractSymbolOverview(html));
        Assert.Contains("Research context at", html);
        Assert.Contains("Research context is descriptive", html);

        var scanner = factory.Services.GetRequiredService<ScannerState>();
        var result = Assert.Single(scanner.Read().Results);
        scanner.SetResults(
            new ScannerStatus(
                MarketDataHealthStatus.Recorded,
                "Test",
                0,
                result.Opportunity.AsOfUtc,
                null),
            [result with { Readiness = WithReadinessState(result, PreTradeReadinessState.Unavailable) }],
            result.Opportunity.AsOfUtc);
        var unavailableOverview = ExtractSymbolOverview(await client.GetStringAsync("/", timeout.Token));
        Assert.Contains("UNAVAILABLE", unavailableOverview);
        Assert.Contains("Support Held", unavailableOverview);
        Assert.True(unavailableOverview.IndexOf("UNAVAILABLE", StringComparison.Ordinal)
            < unavailableOverview.IndexOf("Support Held", StringComparison.Ordinal));

        scanner.SetStatus(
            new ScannerStatus(
                MarketDataHealthStatus.Stale,
                "Test",
                0,
                result.Opportunity.AsOfUtc,
                null),
            result.Opportunity.AsOfUtc.AddSeconds(1));
        var suspendedOverview = ExtractSymbolOverview(await client.GetStringAsync("/", timeout.Token));
        Assert.Contains("SUSPENDED", suspendedOverview);
        Assert.Contains("Support Held", suspendedOverview);
        Assert.True(suspendedOverview.IndexOf("SUSPENDED", StringComparison.Ordinal)
            < suspendedOverview.IndexOf("Support Held", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hosted_live_warmup_completes_before_prior_level_analysis_and_reaction()
    {
        var calendar = new WarmupCalendar();
        var historical = new WarmupHistoricalSource();
        await using var factory = new StructureApplicationFactory(
            reactionSource: new ReactionMarketDataSource(
                "WARMUP",
                "current-session",
                initialPrice: 100.05m,
                confirmationPrice: 100.3m,
                alpaca: true),
            warmupCalendar: calendar,
            warmupHistorical: historical);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        ScannerAnalysisResult? result = null;
        while (!timeout.IsCancellationRequested)
        {
            result = factory.Services.GetRequiredService<ScannerState>().Read().Results
                .SingleOrDefault();
            if (result?.MarketReactions?.Any(item =>
                    item.Assessment.Status == MarketReactionStatus.Confirmed) == true)
            {
                break;
            }

            await Task.Delay(50, timeout.Token);
        }

        Assert.NotNull(result);
        Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, result.PriorSessionWarmup?.Status);
        Assert.Equal(PriorSessionContextStatus.Available,
            result.PriorSessionContext?.Context.Completeness);
        Assert.Equal(MarketSituationContextMode.PriorSessionEnhanced,
            result.MarketSituation?.ContextMode);
        Assert.Equal(100m, result.PriorSessionContext?.Context.PriorSessionLow);
        var reaction = Assert.Single(result.MarketReactions!);
        Assert.Equal(ImportantPriceLevelSource.PriorSessionLow, reaction.Episode.LevelSource);
        Assert.Equal("prior-session", reaction.Episode.LevelSourceSessionId);
        Assert.Equal("Alpaca", reaction.Episode.HistoricalProvider);
        Assert.Equal("sip", reaction.Episode.HistoricalFeed);
        Assert.Equal(result.PriorSessionContext?.Context.ContextId,
            reaction.Episode.PriorSessionContextId);

        using (var priorResponse = await client.GetAsync(
                   "/api/v1/symbols/WARMUP/prior-context?sessionId=current-session",
                   timeout.Token))
        using (var priorJson = JsonDocument.Parse(
                   await priorResponse.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, priorResponse.StatusCode);
            Assert.Equal("CompleteUsable",
                priorJson.RootElement.GetProperty("warmup").GetProperty("status").GetString());
            Assert.Equal("Available",
                priorJson.RootElement.GetProperty("context").GetProperty("completeness").GetString());
            Assert.Equal("Alpaca",
                priorJson.RootElement.GetProperty("warmup").GetProperty("scope")
                    .GetProperty("historicalProvider").GetString());
            Assert.Equal("sip",
                priorJson.RootElement.GetProperty("warmup").GetProperty("scope")
                    .GetProperty("historicalFeed").GetString());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("PRIOR SESSION CONTEXT", html);
        Assert.Contains("sip Historical", html);
        Assert.Contains("live stream: Alpaca / iex", html);
        Assert.Contains("last eligible regular-session trade", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        MarketSessionCalendarStatus.ProviderFailure,
        HistoricalRetrievalStatus.SuccessPaginationExhausted,
        true,
        false,
        PriorSessionWarmupStatus.ProviderFailure,
        PriorSessionContextStatus.Unavailable)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.AuthenticationFailure,
        false,
        false,
        PriorSessionWarmupStatus.AuthenticationFailure,
        PriorSessionContextStatus.Unavailable)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.EntitlementFailure,
        false,
        false,
        PriorSessionWarmupStatus.EntitlementFailure,
        PriorSessionContextStatus.Unavailable)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.LimitExceeded,
        false,
        false,
        PriorSessionWarmupStatus.LimitExceeded,
        PriorSessionContextStatus.Partial)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.SuccessPaginationExhausted,
        false,
        false,
        PriorSessionWarmupStatus.Partial,
        PriorSessionContextStatus.Partial)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.MalformedResponse,
        false,
        false,
        PriorSessionWarmupStatus.Invalid,
        PriorSessionContextStatus.Invalid)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.SuccessPaginationExhausted,
        true,
        true,
        PriorSessionWarmupStatus.Invalid,
        PriorSessionContextStatus.Invalid)]
    [InlineData(
        MarketSessionCalendarStatus.Success,
        HistoricalRetrievalStatus.Cancelled,
        false,
        false,
        PriorSessionWarmupStatus.Cancelled,
        PriorSessionContextStatus.Unavailable)]
    public async Task Hosted_warmup_failures_keep_current_session_analysis_operational(
        MarketSessionCalendarStatus calendarStatus,
        HistoricalRetrievalStatus retrievalStatus,
        bool historicalUsable,
        bool invalidProvenance,
        PriorSessionWarmupStatus expectedWarmup,
        PriorSessionContextStatus expectedCompleteness)
    {
        var calendar = new WarmupCalendar(calendarStatus);
        var historical = new WarmupHistoricalSource(
            retrievalStatus,
            historicalUsable,
            invalidProvenance);
        await using var factory = new StructureApplicationFactory(
            reactionSource: new ReactionMarketDataSource(
                "FALLBACK",
                "current-session",
                initialPrice: 100.05m,
                confirmationPrice: null,
                alpaca: true),
            warmupCalendar: calendar,
            warmupHistorical: historical);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        ScannerAnalysisResult? result = null;
        while (!timeout.IsCancellationRequested)
        {
            result = factory.Services.GetRequiredService<ScannerState>().Read().Results
                .SingleOrDefault();
            if (result is not null)
            {
                break;
            }

            await Task.Delay(50, timeout.Token);
        }

        Assert.NotNull(result);
        Assert.Equal(expectedWarmup, result.PriorSessionWarmup?.Status);
        Assert.Equal(expectedCompleteness, result.PriorSessionContext?.Context.Completeness);
        Assert.Equal(MarketSituationContextMode.CurrentSessionOnly,
            result.MarketSituation?.ContextMode);
        Assert.Null(result.PriorSessionContext?.Context.PriorSessionHigh);
        Assert.Null(result.PriorSessionContext?.Context.PriorSessionLow);
        Assert.NotEqual(MarketDataHealthStatus.Error,
            factory.Services.GetRequiredService<ScannerState>().Read().Status.FeedStatus);

        using var response = await client.GetAsync(
            "/api/v1/symbols/FALLBACK/prior-context?sessionId=current-session",
            timeout.Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedWarmup.ToString(),
            json.RootElement.GetProperty("warmup").GetProperty("status").GetString());
        Assert.Contains(
            "Current-session analysis continues without prior-session enhancement",
            await client.GetStringAsync("/", timeout.Token));
    }

    [Fact]
    public async Task Hosted_one_symbol_failure_does_not_block_another_symbol_success()
    {
        await using var factory = new StructureApplicationFactory(
            warmupCalendar: new WarmupCalendar(),
            warmupHistorical: new WarmupHistoricalSource(symbol =>
                symbol == "FAILED"
                    ? HistoricalRetrievalStatus.AuthenticationFailure
                    : HistoricalRetrievalStatus.SuccessPaginationExhausted),
            warmupSource: new MultipleWarmupLiveSource(
                ("FAILED", "current-session"),
                ("HEALTHY", "current-session")));
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        ScannerReadModel? readModel = null;
        while (!timeout.IsCancellationRequested)
        {
            readModel = factory.Services.GetRequiredService<ScannerState>().Read();
            if (readModel.Results.Count == 2)
            {
                break;
            }

            await Task.Delay(50, timeout.Token);
        }

        Assert.NotNull(readModel);
        Assert.Equal(2, readModel.Results.Count);
        var failed = Assert.Single(readModel.Results, item => item.Features.Symbol == "FAILED");
        var healthy = Assert.Single(readModel.Results, item => item.Features.Symbol == "HEALTHY");
        Assert.Equal(PriorSessionWarmupStatus.AuthenticationFailure, failed.PriorSessionWarmup?.Status);
        Assert.Equal(PriorSessionContextStatus.Unavailable,
            failed.PriorSessionContext?.Context.Completeness);
        Assert.Equal(PriorSessionWarmupStatus.CompleteUsable, healthy.PriorSessionWarmup?.Status);
        Assert.Equal(PriorSessionContextStatus.Available,
            healthy.PriorSessionContext?.Context.Completeness);
        Assert.NotEqual(MarketDataHealthStatus.Error, readModel.Status.FeedStatus);
        using var response = await client.GetAsync("/api/v1/opportunities", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hosted_session_transition_rejects_late_old_warmup_contamination()
    {
        var historical = new BlockingSessionWarmupHistoricalSource();
        await using var factory = new StructureApplicationFactory(
            warmupCalendar: new WarmupCalendar(),
            warmupHistorical: historical,
            warmupSource: new SessionTransitionWarmupLiveSource());
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        ScannerAnalysisResult? sessionB = null;
        while (!timeout.IsCancellationRequested)
        {
            sessionB = factory.Services.GetRequiredService<ScannerState>().Read().Results
                .SingleOrDefault(item => item.Features.SessionId == "session-b");
            if (sessionB is not null)
            {
                break;
            }

            await Task.Delay(50, timeout.Token);
        }

        Assert.NotNull(sessionB);
        Assert.Equal("prior-b", sessionB.PriorSessionContext?.Context.PriorSessionId);
        var scannerState = factory.Services.GetRequiredService<ScannerState>();
        Assert.Equal("session-b", Assert.Single(scannerState.Read().Warmups).Scope.CurrentSessionId);

        historical.ReleaseOldSession();
        await WaitUntilAsync(
            () => scannerState.Read().Results.Count == 2,
            timeout.Token);
        var sessionA = Assert.Single(
            scannerState.Read().Results,
            item => item.Features.SessionId == "session-a");
        Assert.Equal(PriorSessionWarmupStatus.Cancelled, sessionA.PriorSessionWarmup?.Status);
        Assert.Null(sessionA.PriorSessionContext?.Context.PriorSessionId);
        Assert.Equal("session-b", Assert.Single(scannerState.Read().Warmups).Scope.CurrentSessionId);

        using var response = await client.GetAsync(
            "/api/v1/symbols/AAPL/prior-context?sessionId=session-b",
            timeout.Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("prior-b",
            json.RootElement.GetProperty("context").GetProperty("priorSessionId").GetString());
    }

    [Fact]
    public async Task Hosted_unresolved_reaction_is_exposed_as_unresolved_in_api_and_dashboard()
    {
        await using var factory = new StructureApplicationFactory(
            reactionSource: new ReactionMarketDataSource(
                "UNRESOLVED",
                "unresolved-session",
                100.05m,
                confirmationPrice: null));
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        JsonDocument? reactionsJson = null;
        while (!timeout.IsCancellationRequested)
        {
            reactionsJson?.Dispose();
            using var response = await client.GetAsync(
                "/api/v1/reactions?symbol=UNRESOLVED&sessionId=unresolved-session",
                timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            reactionsJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (reactionsJson.RootElement.EnumerateArray().Any(reaction =>
                    reaction.GetProperty("assessment").GetProperty("status").GetString() == "Unresolved"))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(reactionsJson);
        using (reactionsJson)
        {
            var reaction = Assert.Single(reactionsJson.RootElement.EnumerateArray());
            var assessment = reaction.GetProperty("assessment");
            Assert.Equal("Unresolved", assessment.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, assessment.GetProperty("reaction").ValueKind);
            Assert.Equal("unresolved-session", assessment.GetProperty("sessionId").GetString());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("Unresolved Reaction · Unresolved", html);
        Assert.Contains("Pinned support level: $100.00",
            System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " "));
    }

    [Fact]
    public async Task Hosted_pipeline_confirms_resistance_reaction_in_api_and_dashboard()
    {
        await using var factory = new StructureApplicationFactory(
            reactionSource: new ReactionMarketDataSource(
                "RESISTANCE",
                "resistance-session",
                110.05m,
                110.3m));
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        JsonDocument? reactionsJson = null;
        while (!timeout.IsCancellationRequested)
        {
            reactionsJson?.Dispose();
            using var response = await client.GetAsync(
                "/api/v1/reactions?symbol=RESISTANCE&sessionId=resistance-session",
                timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            reactionsJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (reactionsJson.RootElement.EnumerateArray().Any(reaction =>
                    reaction.GetProperty("assessment").GetProperty("status").GetString() == "Confirmed"))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(reactionsJson);
        using (reactionsJson)
        {
            Assert.Single(reactionsJson.RootElement.EnumerateArray());
            var reaction = Assert.Single(
                reactionsJson.RootElement.EnumerateArray(),
                item => item.GetProperty("assessment").GetProperty("status").GetString() == "Confirmed");
            var assessment = reaction.GetProperty("assessment");
            Assert.Equal("Confirmed", assessment.GetProperty("status").GetString());
            Assert.Equal("ResistanceBreakout", assessment.GetProperty("reaction").GetString());
            Assert.Equal(110.3m, assessment.GetProperty("reactionPrice").GetDecimal());
            Assert.Equal("resistance-session", assessment.GetProperty("sessionId").GetString());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("Resistance Breakout", html);
        Assert.Contains("Pinned resistance level: $110.00",
            System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " "));
    }

    [Fact]
    public async Task Dashboard_and_snapshot_api_display_confirmed_structure_from_the_scanner()
    {
        await using var factory = new StructureApplicationFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        HttpResponseMessage? response = null;
        while (!timeout.IsCancellationRequested)
        {
            response?.Dispose();
            response = await client.GetAsync(
                "/api/v1/symbols/STRUCT/snapshot?sessionId=hosted-structure", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(response);
        using (response)
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token)))
        {
            var root = json.RootElement;
            Assert.Equal("Available", root.GetProperty("marketStructureStatus").GetString());
            var high = root.GetProperty("latestConfirmedSwingHigh");
            var low = root.GetProperty("latestConfirmedSwingLow");
            Assert.Equal("ConfirmedSwingHigh", high.GetProperty("type").GetString());
            Assert.Equal("ConfirmedSwingLow", low.GetProperty("type").GetString());
            Assert.Equal(107m, high.GetProperty("price").GetDecimal());
            Assert.Equal(104m, low.GetProperty("price").GetDecimal());
            Assert.Equal("STRUCT", high.GetProperty("symbol").GetString());
            Assert.Equal("hosted-structure", high.GetProperty("sessionId").GetString());
            Assert.Equal("features-v16", high.GetProperty("featureSetVersion").GetString());
            Assert.Equal("session-swing-fractal-v1", high.GetProperty("algorithmVersion").GetString());
            Assert.NotEqual(Guid.Empty, high.GetProperty("id").GetGuid());
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 14, 30, 7, TimeSpan.Zero),
                high.GetProperty("eventTimeUtc").GetDateTimeOffset());
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 14, 30, 9, TimeSpan.Zero),
                high.GetProperty("confirmedAtEventTimeUtc").GetDateTimeOffset());
            Assert.Equal(MarketEventIdentity.Create(new TradeEvent(
                "STRUCT", "hosted-structure",
                new DateTimeOffset(2026, 10, 23, 14, 30, 7, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 23, 14, 30, 8, TimeSpan.Zero),
                "RecordedFixture", "hosted-007", 107m, 10)),
                high.GetProperty("originEventIdentity").GetString());
            Assert.Equal(MarketEventIdentity.Create(new TradeEvent(
                "STRUCT", "hosted-structure",
                new DateTimeOffset(2026, 10, 23, 14, 30, 9, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 23, 14, 30, 10, TimeSpan.Zero),
                "RecordedFixture", "hosted-009", 104m, 10)),
                high.GetProperty("confirmationEventIdentity").GetString());
            Assert.Equal(factory.Store.Snapshot!.Id, root.GetProperty("id").GetGuid());
            Assert.NotNull(factory.Store.TradePlan);
            Assert.Equal("Bullish", root.GetProperty("structureState").GetString());
            Assert.Equal("Higher", root.GetProperty("highSwingRelationship").GetString());
            Assert.Equal("Higher", root.GetProperty("lowSwingRelationship").GetString());
            Assert.Equal(105m, root.GetProperty("previousConfirmedSwingHigh").GetProperty("price").GetDecimal());
            Assert.Equal(102m, root.GetProperty("previousConfirmedSwingLow").GetProperty("price").GetDecimal());
            Assert.Equal(0m, root.GetProperty("distanceToNearestConfirmedSwingHigh").GetDecimal());
            Assert.Equal(1m, root.GetProperty("distanceToNearestConfirmedSwingLow").GetDecimal());
            Assert.Equal(104m, root.GetProperty("nearestSupportCandidate").GetProperty("price").GetDecimal());
            Assert.Equal(105m, root.GetProperty("nearestResistanceCandidate").GetProperty("price").GetDecimal());
        }

        using (var opportunities = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var json = JsonDocument.Parse(await opportunities.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
            var analysis = Assert.Single(json.RootElement.EnumerateArray());
            var plan = analysis.GetProperty("tradePlan");
            Assert.Equal("trade-plan-v1", plan.GetProperty("planDefinitionVersion").GetString());
            Assert.Equal(factory.Store.TradePlan!.Id, plan.GetProperty("id").GetGuid());
            Assert.Equal(factory.Store.TradePlan.Status.ToString(), plan.GetProperty("status").GetString());
            Assert.Equal(factory.Store.TradePlan.AsOfUtc, plan.GetProperty("asOfUtc").GetDateTimeOffset());
            var marketSituation = analysis.GetProperty("marketSituation");
            Assert.Equal("market-situation-v1", marketSituation.GetProperty("definitionVersion").GetString());
            Assert.Equal("STRUCT", marketSituation.GetProperty("symbol").GetString());
            Assert.Equal("CurrentSessionOnly", marketSituation.GetProperty("contextMode").GetString());
            Assert.False(marketSituation.TryGetProperty("eligibleLevels", out _));
            Assert.Equal(64, marketSituation.GetProperty("candidateSetId").GetString()!.Length);
            Assert.True(marketSituation.GetProperty("supportCandidateCount").GetInt32() > 0);
            Assert.Equal(
                JsonValueKind.Object,
                marketSituation.GetProperty("nearestSupportLevel").ValueKind);
            Assert.Equal(
                "TestingResistance",
                marketSituation.GetProperty("resistanceInteraction").GetProperty("situationType").GetString());
            Assert.Contains(factory.MarketSituationStore.Persisted, item => item.Id == marketSituation.GetProperty("id").GetGuid());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("MARKET SITUATION", html);
        Assert.Contains("Testing Resistance", html);
        Assert.Contains("Candidate evidence:", html);
        Assert.DoesNotContain("Eligible structural reference levels", html);
        Assert.Contains("Latest swing high: $107.00", html);
        Assert.Contains("TRADE PLAN", html);
        Assert.Contains("Candidate geometry only", html);
        Assert.Contains("PRE-TRADE READINESS", html);
        Assert.Contains("Latest swing low: $104.00", html);
        Assert.Contains("MARKET STRUCTURE", html);
        Assert.Contains("Nearest resistance candidate: $105.00 · support candidate: $104.00", html);
        Assert.Contains("structure-observations", html);
        Assert.DoesNotContain("probability of profit", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BREAKDOWN CONFIRMED", html);
        Assert.DoesNotContain("BREAKOUT CONFIRMED", html);
    }

    [Fact]
    public async Task Api_and_dashboard_preserve_simultaneous_support_and_resistance_interactions()
    {
        await using var factory = new StructureApplicationFactory(simultaneousSituationSource: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        HttpResponseMessage? response = null;
        while (!timeout.IsCancellationRequested)
        {
            response?.Dispose();
            response = await client.GetAsync(
                "/api/v1/symbols/STRUCT/snapshot?sessionId=hosted-structure",
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        Assert.NotNull(response);
        response.Dispose();
        using (var opportunities = await client.GetAsync("/api/v1/opportunities", timeout.Token))
        using (var json = JsonDocument.Parse(await opportunities.Content.ReadAsStringAsync(timeout.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
            var marketSituation = Assert.Single(json.RootElement.EnumerateArray())
                .GetProperty("marketSituation");
            Assert.Equal(
                "ApproachingSupport",
                marketSituation.GetProperty("supportInteraction").GetProperty("situationType").GetString());
            Assert.Equal(
                "ApproachingResistance",
                marketSituation.GetProperty("resistanceInteraction").GetProperty("situationType").GetString());
            Assert.Equal("Support", marketSituation.GetProperty("primarySituation").GetProperty("side").GetString());
        }

        var scanner = factory.Services.GetRequiredService<ScannerState>();
        var readModel = scanner.Read();
        var result = Assert.Single(readModel.Results);
        scanner.SetResults(
            readModel.Status,
            [result with { MarketReactions = [] }],
            result.Opportunity.AsOfUtc);
        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("Approaching Support", html);
        Assert.Contains("Approaching Resistance", html);
        Assert.Contains("Approaching Support", ExtractSymbolOverview(html));
        Assert.DoesNotContain("Buy now", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sell now", html, StringComparison.OrdinalIgnoreCase);
    }

    private static PreTradeReadinessProjection WithReadinessState(
        ScannerAnalysisResult result,
        PreTradeReadinessState state)
    {
        var opportunity = result.Opportunity;
        var plan = result.TradePlan;
        var direction = plan.Direction ?? TradeDirection.Long;
        var tracked = new TrackedOpportunity(
            Guid.NewGuid(),
            plan.Id,
            opportunity.Id,
            opportunity.Symbol,
            opportunity.SessionId,
            direction,
            opportunity.AsOfUtc,
            plan.AsOfUtc,
            "hosted-dashboard-test",
            TrackedOpportunityStatus.SessionEnded,
            "TestProjection",
            "Test-only terminal projection",
            false,
            opportunity.AsOfUtc,
            "SessionEnded",
            "Test-only terminal projection");
        var assessment = new PreTradeReadinessAssessment(
            Guid.NewGuid(),
            tracked.Id,
            plan.Id,
            opportunity.Id,
            opportunity.Symbol,
            opportunity.SessionId,
            opportunity.AsOfUtc,
            opportunity.AsOfUtc,
            state,
            PreTradeReadinessReason.None,
            direction,
            ReadinessContextMode.CurrentSessionOnly,
            result.Features.ReferencePrice,
            null,
            null,
            false,
            "hosted-dashboard-test",
            result.Features.FeatureSetVersion,
            opportunity.ConfigurationVersion,
            opportunity.MarketData,
            []);
        return new PreTradeReadinessProjection(tracked, plan, assessment);
    }

    private static string ExtractSymbolOverview(string html)
    {
        var start = html.IndexOf("<section class=\"symbol-overview\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "The dashboard did not render the SYMBOL OVERVIEW section.");
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The SYMBOL OVERVIEW section was not closed.");
        return html[start..(end + "</section>".Length)];
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(25, cancellationToken);
        }
    }

    private sealed class StructureApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool _useFixtureSource;
        private readonly bool _simultaneousSituationSource;
        private readonly bool _useRestartSource;
        private readonly bool _useMultipleSymbols;
        private readonly ReactionMarketDataSource? _reactionSource;
        private readonly InMemoryReactionStore _reactionStore;
        private readonly IMarketSessionCalendar? _warmupCalendar;
        private readonly IHistoricalMarketDataSource? _warmupHistorical;
        private readonly IMarketDataSource? _warmupSource;

        public StructureApplicationFactory(
            bool useFixtureSource = false,
            bool simultaneousSituationSource = false,
            bool useRestartSource = false,
            bool useMultipleSymbols = false,
            ReactionMarketDataSource? reactionSource = null,
            InMemoryReactionStore? reactionStore = null,
            IMarketSessionCalendar? warmupCalendar = null,
            IHistoricalMarketDataSource? warmupHistorical = null,
            IMarketDataSource? warmupSource = null)
        {
            _useFixtureSource = useFixtureSource;
            _simultaneousSituationSource = simultaneousSituationSource;
            _useRestartSource = useRestartSource;
            _useMultipleSymbols = useMultipleSymbols;
            _reactionSource = reactionSource;
            _reactionStore = reactionStore ?? new InMemoryReactionStore();
            _warmupCalendar = warmupCalendar;
            _warmupHistorical = warmupHistorical;
            _warmupSource = warmupSource;
        }

        private static string WebProjectPath
        {
            get
            {
                var webOutput = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                if (webOutput is not null && Directory.Exists(Path.Combine(webOutput, "Fixtures")))
                {
                    return webOutput;
                }

                return new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }
                    .SelectMany(AncestorDirectories)
                    .Select(directory => Path.Combine(directory, "src", "MarketOpportunity.Web"))
                    .FirstOrDefault(Directory.Exists)
                    ?? throw new DirectoryNotFoundException("Could not locate src/MarketOpportunity.Web for the hosted test.");
            }
        }

        private static IEnumerable<string> AncestorDirectories(string path)
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            {
                yield return directory.FullName;
            }
        }

        public CapturingStore Store { get; } = new();

        public InMemoryReadinessStore ReadinessStore { get; } = new();

        public InMemoryMarketSituationStore MarketSituationStore { get; } = new();
        public InMemoryMarketSituationEvidenceStore EvidenceStore { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(WebProjectPath);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(Settings()));
            builder.ConfigureServices(services =>
            {
                if (_warmupSource is not null)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddSingleton(_warmupSource);
                }
                else if (_reactionSource is not null)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddSingleton<IMarketDataSource>(_reactionSource);
                    if (_warmupCalendar is null && _warmupHistorical is null)
                    {
                        services.RemoveAll<IPriorSessionContextProvider>();
                        services.AddSingleton<IPriorSessionContextProvider>(
                            new CompletePriorSessionContextProvider());
                    }
                }
                else if (_useRestartSource)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddSingleton<IMarketDataSource>(new RestartMarketDataSource());
                }
                else if (_useMultipleSymbols)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddSingleton<IMarketDataSource>(new MultipleSymbolStructureSource());
                }
                else if (!_useFixtureSource)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddScoped<IMarketDataSource>(_ =>
                        new SyntheticStructureSource(_simultaneousSituationSource));
                }
                if (_warmupCalendar is not null)
                {
                    services.RemoveAll<IMarketSessionCalendar>();
                    services.AddSingleton(_warmupCalendar);
                }
                if (_warmupHistorical is not null)
                {
                    services.RemoveAll<IHistoricalMarketDataSource>();
                    services.AddSingleton(_warmupHistorical);
                }
                services.RemoveAll<IScannerStore>();
                services.AddSingleton<IScannerStore>(Store);
                services.RemoveAll<IPreTradeReadinessStore>();
                services.AddSingleton<IPreTradeReadinessStore>(ReadinessStore);
                services.RemoveAll<IMarketSituationStore>();
                services.AddSingleton<IMarketSituationStore>(MarketSituationStore);
                services.RemoveAll<IMarketSituationEvidenceStore>();
                services.AddSingleton<IMarketSituationEvidenceStore>(EvidenceStore);
                services.RemoveAll<IMarketReactionStore>();
                services.AddSingleton<IMarketReactionStore>(_reactionStore);
            });
        }

        public static MarketReactionEvaluation CreateActiveEpisode()
        {
            var start = new DateTimeOffset(2026, 11, 6, 18, 30, 0, TimeSpan.Zero);
            var configuration = new ScannerConfiguration();
            var level = new ImportantPriceLevel(
                Guid.NewGuid(),
                "RSTART",
                "restart-session",
                "restart-session",
                100m,
                ImportantPriceLevelRole.Support,
                ImportantPriceLevelSource.CurrentSwingLow,
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                MarketSituationContextMode.CurrentSessionOnly,
                "Complete current-session retained event history",
                null,
                null,
                null,
                "market-structure-v1",
                "features-v16",
                null);
            var interaction = new MarketSituationInteraction(
                MarketSituationType.TestingSupport,
                level,
                99.9m,
                100.1m,
                0m,
                0m,
                0m,
                0m,
                0m);
            var startObservation = new TradeEvent(
                "RSTART",
                "restart-session",
                start,
                start,
                "UnitTest",
                "restart-start",
                100.05m,
                10);
            var evaluator = new MarketReactionEvaluator();
            var episode = evaluator.StartEpisode(interaction, startObservation, configuration);
            return evaluator.Evaluate(episode, [], start);
        }

        private static Dictionary<string, string?> Settings() => new()
        {
            ["MarketData:Provider"] = "Fixture",
            ["ConnectionStrings:MarketOpportunity"] =
                "Server=localhost;Database=UnusedHostedTest;Integrated Security=true;TrustServerCertificate=true"
        };
    }

    private sealed class CapturingStore : IScannerStore
    {
        public FeatureSnapshot? Snapshot { get; private set; }
        public TradePlan? TradePlan { get; private set; }

        public Task PersistRunAsync(
            ScannerConfiguration configuration,
            IReadOnlyCollection<MarketEvent> marketEvents,
            FeatureSnapshot featureSnapshot,
            Opportunity opportunity,
            TradePlan tradePlan,
            CancellationToken cancellationToken)
        {
            Snapshot = featureSnapshot;
            TradePlan = tradePlan;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReadinessStore : IPreTradeReadinessStore
    {
        public List<(TrackedOpportunity Tracked, PreTradeReadinessAssessment? Assessment)> Persisted { get; } = [];

        public Task<IReadOnlyList<TrackedOpportunitySnapshot>> RestoreActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrackedOpportunitySnapshot>>([]);

        public Task PersistAsync(
            TrackedOpportunity tracked,
            PreTradeReadinessAssessment? assessment,
            CancellationToken cancellationToken)
        {
            Persisted.Add((tracked, assessment));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryMarketSituationStore : IMarketSituationStore
    {
        public List<MarketSituationAssessment> Persisted { get; } = [];

        public Task PersistAsync(
            MarketSituationAssessment assessment,
            CancellationToken cancellationToken)
        {
            Persisted.Add(assessment);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryMarketSituationEvidenceStore : IMarketSituationEvidenceStore
    {
        public static readonly Guid KnownAssessmentId =
            new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public static readonly Guid LegacyAssessmentId =
            new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public static readonly Guid OversizedAssessmentId =
            new("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private const string CandidateSetId =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        public Task<MarketSituationCandidatePageReadModel?> ReadCandidatePageAsync(
            Guid assessmentId,
            int afterOrdinal,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (assessmentId == LegacyAssessmentId)
            {
                throw new MarketSituationEvidenceReadException(
                    MarketSituationEvidenceReadFailure.LegacyCandidateEvidenceUnavailable,
                    "Legacy evidence does not have normalized members.");
            }

            if (assessmentId != KnownAssessmentId && assessmentId != OversizedAssessmentId)
            {
                return Task.FromResult<MarketSituationCandidatePageReadModel?>(null);
            }

            var oversized = assessmentId == OversizedAssessmentId;
            var source = new MarketSituationCandidateSourceReadModel(
                "source-a",
                Guid.NewGuid(),
                "prior-session",
                "STRUCT",
                "hosted-structure",
                "PriorSwingHigh",
                "PriorSessionEnhanced",
                oversized ? new string('x', 70_000) : "Complete",
                "in-memory",
                null,
                null,
                "session-swing-fractal-v1",
                "features-v16",
                "prior-session-context-v1",
                null,
                null);
            var members = Enumerable.Range(0, 8)
                .Select(ordinal => new MarketSituationCandidateMemberReadModel(
                    ordinal,
                    new Guid(ordinal + 1, 0, 0, new byte[8]),
                    ImportantPriceLevelRole.Support,
                    100m + ordinal,
                    (100m + ordinal).ToString("G29", CultureInfo.InvariantCulture),
                    new Guid(ordinal + 1, 1, 0, new byte[8]),
                    $"origin-{ordinal}",
                    $"confirmation-{ordinal}",
                    new DateTimeOffset(2026, 10, 23, 14, 30, ordinal, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026,
                        10,
                        23,
                        14,
                        30,
                        ordinal + 1,
                        TimeSpan.Zero),
                    source.Id))
                .Where(item => item.Ordinal > afterOrdinal)
                .Take(pageSize + 1)
                .ToArray();
            var hasMore = members.Length > pageSize;
            var pageMembers = hasMore ? members[..pageSize] : members;
            return Task.FromResult<MarketSituationCandidatePageReadModel?>(
                new MarketSituationCandidatePageReadModel(
                    assessmentId,
                    CandidateSetId,
                    "market-situation-candidate-set-v1",
                    8,
                    pageMembers.Length,
                    pageMembers.Length == 0 ? null : pageMembers[0].Ordinal,
                    pageMembers.Length == 0 ? null : pageMembers[^1].Ordinal,
                    afterOrdinal,
                    hasMore ? pageMembers[^1].Ordinal : null,
                    hasMore,
                    pageMembers,
                    new Dictionary<string, MarketSituationCandidateSourceReadModel>(
                        StringComparer.Ordinal)
                    {
                        [source.Id] = source
                    },
                    "market-situation-candidate-read-v1"));
        }

        public Task<MarketSituationSelectedCandidateReadModel?> ReadSelectedCandidateAsync(
            string candidateSetId,
            Guid levelId,
            CancellationToken cancellationToken) =>
            Task.FromResult<MarketSituationSelectedCandidateReadModel?>(null);
    }

    private sealed class InMemoryReactionStore : IMarketReactionStore
    {
        private readonly Dictionary<Guid, LevelInteractionEpisode> _episodes = [];

        public List<MarketReactionEvaluation> Persisted { get; } = [];
        public Dictionary<Guid, MarketReactionResearchContext?> ResearchContexts { get; } = [];

        public void Seed(MarketReactionEvaluation evaluation) =>
            _episodes.Add(evaluation.Episode.Id, evaluation.Episode);

        public Task<IReadOnlyList<LevelInteractionEpisode>> RestoreActiveEpisodesAsync(
            string sessionId,
            IReadOnlyCollection<string>? symbols,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelInteractionEpisode>>(_episodes.Values
                .Where(item => item.Status == MarketReactionEpisodeStatus.Active
                    && item.SessionId == sessionId
                    && (symbols is null || symbols.Contains(item.Symbol, StringComparer.OrdinalIgnoreCase)))
                .ToArray());

        public Task PersistAsync(
            MarketReactionEvaluation evaluation,
            CancellationToken cancellationToken)
        {
            _episodes[evaluation.Episode.Id] = evaluation.Episode;
            Persisted.Add(evaluation);
            return Task.CompletedTask;
        }

        public Task PersistAsync(
            MarketReactionEvaluation evaluation,
            MarketReactionResearchContext? researchContext,
            CancellationToken cancellationToken)
        {
            ResearchContexts[evaluation.Assessment.Id] = researchContext;
            return PersistAsync(evaluation, cancellationToken);
        }
    }

    private sealed class CompletePriorSessionContextProvider : IPriorSessionContextProvider
    {
        private static readonly Guid ContextId = Guid.Parse("150bde7f-9f62-4f34-883f-4f62ac6b1c47");

        public Task<PriorSessionContextView> GetContextAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            decimal? currentReferencePrice,
            CancellationToken cancellationToken)
        {
            var priorStart = currentEventTimeUtc.AddDays(-1);
            var priorEnd = priorStart.AddHours(6);
            var context = new PriorSessionContext(
                ContextId,
                symbol,
                currentSessionId,
                "reaction-prior-session",
                priorStart,
                priorEnd,
                110m,
                100m,
                105m,
                priorEnd,
                PriorSessionCloseSemantics.LastEligibleTradeAtOrBeforeSessionEnd,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                MarketStructureState.Insufficient,
                [],
                [],
                PriorSessionContextStatus.Available,
                "Complete synthetic prior-session context for hosted reaction coverage.",
                new MarketDataCapabilities(
                    "RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true),
                "synthetic-prior-history",
                priorStart,
                priorEnd,
                true,
                true,
                false,
                "prior-session-context-v1");
            return Task.FromResult(new PriorSessionContextView(
                context,
                currentReferencePrice,
                null,
                currentReferencePrice is { } referencePrice
                    ? new PriceDistance(referencePrice, 100m, Math.Abs(referencePrice - 100m), null)
                    : null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }
    }

    private sealed class WarmupCalendar(
        MarketSessionCalendarStatus status = MarketSessionCalendarStatus.Success)
        : IMarketSessionCalendar
    {
        public Task<MarketSessionCalendarResolution> ResolvePreviousSessionAsync(
            string symbol,
            string currentSessionId,
            DateTimeOffset currentEventTimeUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (status != MarketSessionCalendarStatus.Success)
            {
                return Task.FromResult(new MarketSessionCalendarResolution(
                    null,
                    null,
                    "alpaca-test-calendar",
                    $"Calendar resolution ended with {status}.",
                    status));
            }

            var (current, prior) = currentSessionId switch
            {
                "session-a" => (Session("session-a", 2026, 11, 6),
                    Session("prior-a", 2026, 11, 5)),
                "session-b" => (Session("session-b", 2026, 11, 7),
                    Session("prior-b", 2026, 11, 6)),
                _ => (Session(currentSessionId, 2026, 11, 6),
                    Session("prior-session", 2026, 11, 5))
            };
            return Task.FromResult(new MarketSessionCalendarResolution(
                current,
                prior,
                "alpaca-test-calendar",
                null));
        }

        private static MarketSessionDefinition Session(
            string sessionId,
            int year,
            int month,
            int day) =>
            new(
                sessionId,
                new DateOnly(year, month, day),
                new DateTimeOffset(year, month, day, 13, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(year, month, day, 20, 0, 0, TimeSpan.Zero),
                "Alpaca",
                "market-calendar-rest",
                "alpaca-calendar-test-v1",
                MarketSessionType.Regular,
                MarketSessionValidity.Valid);
    }

    private sealed class WarmupHistoricalSource : IHistoricalMarketDataSource
    {
        private readonly Func<string, HistoricalRetrievalStatus> _status;
        private readonly bool _declaresComplete;
        private readonly bool _invalidProvenance;

        public WarmupHistoricalSource()
            : this(HistoricalRetrievalStatus.SuccessPaginationExhausted, true, false)
        {
        }

        public WarmupHistoricalSource(
            HistoricalRetrievalStatus status,
            bool declaresComplete,
            bool invalidProvenance)
            : this(_ => status, declaresComplete, invalidProvenance)
        {
        }

        public WarmupHistoricalSource(
            Func<string, HistoricalRetrievalStatus> status)
            : this(status, true, false)
        {
        }

        private WarmupHistoricalSource(
            Func<string, HistoricalRetrievalStatus> status,
            bool declaresComplete,
            bool invalidProvenance)
        {
            _status = status;
            _declaresComplete = declaresComplete;
            _invalidProvenance = invalidProvenance;
        }

        public Task<HistoricalMarketSessionData?> ReadSessionAsync(
            string symbol,
            MarketSession session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _status(symbol);
            var hasObservations = status is HistoricalRetrievalStatus.SuccessPaginationExhausted
                or HistoricalRetrievalStatus.LimitExceeded;
            var complete = _declaresComplete
                && status == HistoricalRetrievalStatus.SuccessPaginationExhausted;
            var events = hasObservations
                ? CompleteHistory(symbol, session)
                : [];
            var provider = _invalidProvenance ? "WrongProvider" : "Alpaca";
            var exhausted = status == HistoricalRetrievalStatus.SuccessPaginationExhausted;
            return Task.FromResult<HistoricalMarketSessionData?>(new HistoricalMarketSessionData(
                events,
                new MarketDataCapabilities(
                    "Alpaca", "sip", MarketDataCoverage.Consolidated, false, true, false),
                "historical-rest",
                session.StartUtc,
                session.EndUtc,
                complete,
                HasKnownGaps: false,
                new HistoricalDataRequest(
                    symbol,
                    session.SessionId,
                    session.StartUtc,
                    session.EndUtc),
                status,
                new HistoricalDataProvenance(
                    provider,
                    "sip",
                    "historical-rest",
                    session.EndUtc.AddHours(1),
                    exhausted ? 1 : 0,
                    0),
                PaginationExhausted: exhausted,
                StatusMessage: status.ToString()));
        }

        internal static TradeEvent[] CompleteHistory(string symbol, MarketSession session)
        {
            decimal[] prices = [102m, 100m, 101m, 100.5m, 101.5m, 100.2m];
            return prices.Select((price, index) => new TradeEvent(
                    symbol,
                    session.SessionId,
                    session.StartUtc.AddMinutes(index + 1),
                    session.StartUtc.AddMinutes(index + 2),
                    "Alpaca",
                    $"history-{symbol}-{session.SessionId}-{index}",
                    price,
                    100))
                .ToArray();
        }
    }

    private sealed class MultipleWarmupLiveSource(
        params (string Symbol, string SessionId)[] scopes) : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("Alpaca", "iex", MarketDataCoverage.Limited, true, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var eventTime = new DateTimeOffset(2026, 11, 6, 18, 30, 0, TimeSpan.Zero);
            foreach (var (symbol, sessionId) in scopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new TradeEvent(
                    symbol,
                    sessionId,
                    eventTime,
                    eventTime,
                    "Alpaca",
                    $"{symbol}-start",
                    100.05m,
                    10);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class SessionTransitionWarmupLiveSource : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("Alpaca", "iex", MarketDataCoverage.Limited, true, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var sessionA = new DateTimeOffset(2026, 11, 6, 18, 30, 0, TimeSpan.Zero);
            yield return new TradeEvent(
                "AAPL", "session-a", sessionA, sessionA, "Alpaca", "session-a-start", 100.05m, 10);
            var sessionB = new DateTimeOffset(2026, 11, 7, 18, 30, 0, TimeSpan.Zero);
            yield return new TradeEvent(
                "AAPL", "session-b", sessionB, sessionB, "Alpaca", "session-b-start", 100.05m, 10);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class BlockingSessionWarmupHistoricalSource : IHistoricalMarketDataSource
    {
        private readonly TaskCompletionSource _releaseOld =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HistoricalMarketSessionData?> ReadSessionAsync(
            string symbol,
            MarketSession session,
            CancellationToken cancellationToken)
        {
            if (session.SessionId == "prior-a")
            {
                await _releaseOld.Task;
            }

            return new HistoricalMarketSessionData(
                WarmupHistoricalSource.CompleteHistory(symbol, session),
                new MarketDataCapabilities(
                    "Alpaca", "sip", MarketDataCoverage.Consolidated, false, true, false),
                "historical-rest",
                session.StartUtc,
                session.EndUtc,
                DeclaresCompleteCoverage: true,
                HasKnownGaps: false,
                new HistoricalDataRequest(
                    symbol, session.SessionId, session.StartUtc, session.EndUtc),
                HistoricalRetrievalStatus.SuccessPaginationExhausted,
                new HistoricalDataProvenance(
                    "Alpaca", "sip", "historical-rest", session.EndUtc.AddHours(1), 1, 0),
                PaginationExhausted: true,
                StatusMessage: null);
        }

        public void ReleaseOldSession() => _releaseOld.TrySetResult();
    }

    private sealed class ReactionMarketDataSource(
        string symbol = "REACTION",
        string sessionId = "reaction-session",
        decimal initialPrice = 100.05m,
        decimal? confirmationPrice = 100.3m,
        bool alpaca = false) : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            alpaca
                ? new("Alpaca", "iex", MarketDataCoverage.Limited, true, true, true)
                : new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = new DateTimeOffset(2026, 11, 6, 18, 30, 0, TimeSpan.Zero);
            yield return new TradeEvent(
                symbol, sessionId, start, start, alpaca ? "Alpaca" : "RecordedFixture",
                "reaction-start", initialPrice, 10);
            if (confirmationPrice is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                yield break;
            }

            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            yield return new TradeEvent(
                symbol, sessionId, start.AddSeconds(1), start.AddSeconds(1),
                alpaca ? "Alpaca" : "RecordedFixture", "reaction-confirm", confirmationPrice.Value, 10);
            yield return new QuoteEvent(
                symbol, sessionId, start.AddSeconds(2), start.AddSeconds(2),
                alpaca ? "Alpaca" : "RecordedFixture", "reaction-final-quote",
                initialPrice - 0.01m, initialPrice + 0.01m, 100, 100);
            await Task.CompletedTask;
        }
    }

    private sealed class RestartMarketDataSource : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var eventTime = new DateTimeOffset(2026, 11, 6, 18, 30, 1, TimeSpan.Zero);
            yield return new TradeEvent(
                "RSTART",
                "restart-session",
                eventTime,
                eventTime,
                "RecordedFixture",
                "restart-after",
                100.05m,
                10);
            await Task.CompletedTask;
        }
    }

    private sealed class SyntheticStructureSource(bool simultaneousSituation = false) : IMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
            decimal[] prices = simultaneousSituation
                ? [101m, 100m, 99.9m, 100.3m, 100.1m, 100.2m, 100m]
                : [101.5m, 101m, 105m, 103m, 102m, 104m, 103m, 107m, 105m, 104m, 106m, 105m];
            for (var index = 0; index < prices.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new TradeEvent(
                    "STRUCT", "hosted-structure", start.AddSeconds(index), start.AddSeconds(index + 1),
                    "RecordedFixture", $"hosted-{index:D3}", prices[index], 10);
            }

            await Task.CompletedTask;
        }
    }

    private sealed class MultipleSymbolStructureSource : IMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
            decimal[] prices = [101.5m, 101m, 105m, 103m, 102m, 104m, 103m, 107m, 105m, 104m, 106m, 105m];
            foreach (var symbol in new[] { "AAPL", "MSFT", "NVDA" })
            {
                for (var index = 0; index < prices.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new TradeEvent(
                        symbol,
                        "overview-session",
                        start.AddSeconds(index),
                        start.AddSeconds(index + 1),
                        "RecordedFixture",
                        $"{symbol}-{index:D3}",
                        prices[index],
                        10);
                }
            }

            await Task.CompletedTask;
        }
    }
}
