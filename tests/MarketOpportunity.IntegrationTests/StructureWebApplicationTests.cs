using System.Net;
using System.Runtime.CompilerServices;
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
            Assert.DoesNotContain(
                marketSituation.GetProperty("eligibleLevels").EnumerateArray(),
                item => item.GetProperty("source").GetString() is "PriorSwingHigh"
                    or "PriorSwingLow" or "PriorSessionHigh" or "PriorSessionLow");
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
            Assert.Equal("scanner-config-v19", json.RootElement.GetProperty("version").GetString());
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
            Assert.Contains(
                marketSituation.GetProperty("eligibleLevels").EnumerateArray(),
                item => item.GetProperty("source").GetString() == "CurrentSwingLow");
            Assert.Equal(
                "TestingResistance",
                marketSituation.GetProperty("resistanceInteraction").GetProperty("situationType").GetString());
            Assert.Contains(factory.MarketSituationStore.Persisted, item => item.Id == marketSituation.GetProperty("id").GetGuid());
        }

        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("MARKET SITUATION", html);
        Assert.Contains("Testing Resistance", html);
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

    private sealed class StructureApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool _useFixtureSource;
        private readonly bool _simultaneousSituationSource;
        private readonly bool _useRestartSource;
        private readonly bool _useMultipleSymbols;
        private readonly ReactionMarketDataSource? _reactionSource;
        private readonly InMemoryReactionStore _reactionStore;

        public StructureApplicationFactory(
            bool useFixtureSource = false,
            bool simultaneousSituationSource = false,
            bool useRestartSource = false,
            bool useMultipleSymbols = false,
            ReactionMarketDataSource? reactionSource = null,
            InMemoryReactionStore? reactionStore = null)
        {
            _useFixtureSource = useFixtureSource;
            _simultaneousSituationSource = simultaneousSituationSource;
            _useRestartSource = useRestartSource;
            _useMultipleSymbols = useMultipleSymbols;
            _reactionSource = reactionSource;
            _reactionStore = reactionStore ?? new InMemoryReactionStore();
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
                if (_reactionSource is not null)
                {
                    services.RemoveAll<IMarketDataSource>();
                    services.AddSingleton<IMarketDataSource>(_reactionSource);
                    services.RemoveAll<IPriorSessionContextProvider>();
                    services.AddSingleton<IPriorSessionContextProvider>(new CompletePriorSessionContextProvider());
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
                services.RemoveAll<IScannerStore>();
                services.AddSingleton<IScannerStore>(Store);
                services.RemoveAll<IPreTradeReadinessStore>();
                services.AddSingleton<IPreTradeReadinessStore>(ReadinessStore);
                services.RemoveAll<IMarketSituationStore>();
                services.AddSingleton<IMarketSituationStore>(MarketSituationStore);
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

    private sealed class ReactionMarketDataSource(
        string symbol = "REACTION",
        string sessionId = "reaction-session",
        decimal initialPrice = 100.05m,
        decimal? confirmationPrice = 100.3m) : ILiveMarketDataSource
    {
        public MarketDataCapabilities Capabilities { get; } =
            new("RecordedFixture", "Synthetic", MarketDataCoverage.Synthetic, false, true, true);

        public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = new DateTimeOffset(2026, 11, 6, 18, 30, 0, TimeSpan.Zero);
            yield return new TradeEvent(
                symbol, sessionId, start, start, "RecordedFixture", "reaction-start", initialPrice, 10);
            if (confirmationPrice is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                yield break;
            }

            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            yield return new TradeEvent(
                symbol, sessionId, start.AddSeconds(1), start.AddSeconds(1),
                "RecordedFixture", "reaction-confirm", confirmationPrice.Value, 10);
            yield return new QuoteEvent(
                symbol, sessionId, start.AddSeconds(2), start.AddSeconds(2),
                "RecordedFixture", "reaction-final-quote", initialPrice - 0.01m, initialPrice + 0.01m, 100, 100);
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
