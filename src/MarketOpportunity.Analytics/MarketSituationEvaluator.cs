using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed class MarketSituationEvaluator
{
    public const string DefinitionVersion = "market-situation-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MarketSituationAssessment Evaluate(
        FeatureSnapshot snapshot,
        IReadOnlyList<MarketEvent> currentEvents,
        PriorSessionContextView? priorContext,
        ScannerConfiguration configuration,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentEvents);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        var situationConfiguration = configuration.MarketSituation;
        var levels = new List<ImportantPriceLevel>();
        var currentSessionEvents = currentEvents
            .Where(item => item.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
                && item.SessionId.Equals(snapshot.SessionId, StringComparison.Ordinal)
                && item.EventTimeUtc <= snapshot.AsOfUtc)
            .GroupBy(MarketEventIdentity.Create, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.EventTimeUtc)
            .ThenBy(item => item.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.SequenceId, StringComparer.Ordinal)
            .ThenBy(MarketEventIdentity.Create, StringComparer.Ordinal)
            .ToArray();

        AddPriorSessionLevels(levels, snapshot, priorContext);
        AddCurrentSessionLevels(levels, snapshot, currentSessionEvents);

        var orderedLevels = levels
            .OrderBy(item => item.Role)
            .ThenBy(item => item.Price)
            .ThenBy(item => SourcePrecedence(item.Source))
            .ThenBy(item => item.Id)
            .ToArray();
        var contextMode = orderedLevels.Any(IsPriorSessionLevel)
            ? MarketSituationContextMode.PriorSessionEnhanced
            : MarketSituationContextMode.CurrentSessionOnly;
        var currentPrice = snapshot.IsPriceDataValid && snapshot.ReferencePrice is > 0
            ? snapshot.ReferencePrice
            : null;
        var supportLevel = currentPrice is { } price
            ? NearestLevel(orderedLevels, ImportantPriceLevelRole.Support, price)
            : null;
        var resistanceLevel = currentPrice is { } reference
            ? NearestLevel(orderedLevels, ImportantPriceLevelRole.Resistance, reference)
            : null;
        var support = supportLevel is null
            ? null
            : Classify(supportLevel, currentPrice!.Value, situationConfiguration);
        var resistance = resistanceLevel is null
            ? null
            : Classify(resistanceLevel, currentPrice!.Value, situationConfiguration);
        var activeSupport = support is null ? null : support;
        var activeResistance = resistance is null ? null : resistance;

        var status = currentPrice is null
            ? MarketSituationStatus.NotComputable
            : orderedLevels.Length == 0
                ? MarketSituationStatus.NoEligibleLevels
                : MarketSituationStatus.Available;
        var unavailableReason = status switch
        {
            MarketSituationStatus.NotComputable =>
                "A positive current reference price is required to evaluate structural levels.",
            MarketSituationStatus.NoEligibleLevels =>
                "No complete, eligible prior-session or current-session structural levels are available.",
            _ => null
        };
        var primary = ChoosePrimary(activeSupport, activeResistance);
        var material = new
        {
            FeatureSnapshotId = snapshot.Id,
            PriorSessionContextId = priorContext?.Context.ContextId,
            snapshot.Symbol,
            snapshot.SessionId,
            snapshot.AsOfUtc,
            CurrentReferencePrice = currentPrice,
            Status = status,
            UnavailableReason = unavailableReason,
            ContextMode = contextMode,
            NearestSupportLevel = supportLevel,
            NearestResistanceLevel = resistanceLevel,
            SupportInteraction = activeSupport,
            ResistanceInteraction = activeResistance,
            PrimarySituation = primary,
            EligibleLevels = orderedLevels,
            situationConfiguration.TestZonePercent,
            situationConfiguration.ApproachDistancePercent,
            DefinitionVersion,
            snapshot.FeatureSetVersion,
            ConfigurationVersion = configuration.Version,
            snapshot.MarketData,
            snapshot.DataQuality,
            snapshot.MarketStructureStatus,
            snapshot.HasCompleteSessionData
        };

        return new MarketSituationAssessment(
            DeterministicId(JsonSerializer.Serialize(material, JsonOptions)),
            snapshot.Id,
            priorContext?.Context.ContextId,
            snapshot.Symbol,
            snapshot.SessionId,
            snapshot.AsOfUtc,
            evaluatedAtUtc.ToUniversalTime(),
            currentPrice,
            status,
            unavailableReason,
            contextMode,
            supportLevel,
            resistanceLevel,
            activeSupport,
            activeResistance,
            primary,
            orderedLevels,
            situationConfiguration.TestZonePercent,
            situationConfiguration.ApproachDistancePercent,
            DefinitionVersion,
            snapshot.FeatureSetVersion,
            configuration.Version,
            snapshot.MarketData,
            snapshot.DataQuality,
            snapshot.MarketStructureStatus,
            snapshot.HasCompleteSessionData);
    }

    private static void AddPriorSessionLevels(
        ICollection<ImportantPriceLevel> levels,
        FeatureSnapshot snapshot,
        PriorSessionContextView? priorContext)
    {
        if (priorContext is null)
        {
            return;
        }

        var context = priorContext.Context;
        if (context.Completeness != PriorSessionContextStatus.Available
            || context.PriorSessionId is null
            || context.PriorSessionId == context.CurrentSessionId
            || context.PriorSessionStartUtc is not { } priorStart
            || context.PriorSessionEndUtc is not { } priorEnd
            || priorStart >= priorEnd
            || context.CoverageStartUtc is not { } coverageStart
            || context.CoverageEndUtc is not { } coverageEnd
            || coverageStart > priorStart
            || coverageEnd < priorEnd
            || context.MarketData is not { SupportsTrades: true }
            || !context.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
            || !context.CurrentSessionId.Equals(snapshot.SessionId, StringComparison.Ordinal)
            || context.HasKnownGaps
            || !context.IsSessionStartCovered
            || !context.IsSessionEndCovered)
        {
            return;
        }

        foreach (var candidate in context.SupportCandidates
                     .Where(item => item.Type == StructuralLevelType.Support
                         && item.Price > 0
                         && item.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
                         && item.SessionId == context.PriorSessionId))
        {
            levels.Add(CreateSwingLevel(
                snapshot,
                candidate,
                ImportantPriceLevelSource.PriorSwingLow,
                ImportantPriceLevelRole.Support,
                MarketSituationContextMode.PriorSessionEnhanced,
                "Complete prior-session history",
                context));
        }

        foreach (var candidate in context.ResistanceCandidates
                     .Where(item => item.Type == StructuralLevelType.Resistance
                         && item.Price > 0
                         && item.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
                         && item.SessionId == context.PriorSessionId))
        {
            levels.Add(CreateSwingLevel(
                snapshot,
                candidate,
                ImportantPriceLevelSource.PriorSwingHigh,
                ImportantPriceLevelRole.Resistance,
                MarketSituationContextMode.PriorSessionEnhanced,
                "Complete prior-session history",
                context));
        }

        AddPriorExtreme(
            levels,
            snapshot,
            context,
            context.PriorSessionHigh,
            ImportantPriceLevelSource.PriorSessionHigh,
            ImportantPriceLevelRole.Resistance);
        AddPriorExtreme(
            levels,
            snapshot,
            context,
            context.PriorSessionLow,
            ImportantPriceLevelSource.PriorSessionLow,
            ImportantPriceLevelRole.Support);
    }

    private static void AddPriorExtreme(
        ICollection<ImportantPriceLevel> levels,
        FeatureSnapshot snapshot,
        PriorSessionContext context,
        decimal? price,
        ImportantPriceLevelSource source,
        ImportantPriceLevelRole role)
    {
        if (price is not > 0)
        {
            return;
        }

        levels.Add(new ImportantPriceLevel(
            CreateLevelId(
                snapshot.Symbol,
                snapshot.SessionId,
                context.PriorSessionId!,
                source,
                context.ContextId.ToString("N"),
                price.Value),
            snapshot.Symbol,
            snapshot.SessionId,
            context.PriorSessionId!,
            price.Value,
            role,
            source,
            context.ContextId,
            context.ContextId,
            null,
            null,
            null,
            null,
            MarketSituationContextMode.PriorSessionEnhanced,
            "Complete prior-session history",
            context.HistoricalSource,
            context.CoverageStartUtc,
            context.CoverageEndUtc,
            null,
            context.LatestConfirmedSwingHigh?.FeatureSetVersion
                ?? context.LatestConfirmedSwingLow?.FeatureSetVersion,
            context.ContextDefinitionVersion));
    }

    private static void AddCurrentSessionLevels(
        ICollection<ImportantPriceLevel> levels,
        FeatureSnapshot snapshot,
        IReadOnlyList<MarketEvent> currentSessionEvents)
    {
        if (snapshot.MarketStructureStatus != MarketStructureStatus.Available
            || !snapshot.IsPriceDataValid
            || !snapshot.HasCompleteSessionData)
        {
            return;
        }

        var trades = currentSessionEvents.OfType<TradeEvent>().ToArray();
        var (highs, lows) = FeatureCalculator.CalculateConfirmedSwings(
            trades,
            snapshot.Symbol,
            snapshot.SessionId,
            snapshot.FeatureSetVersion);

        foreach (var swing in lows)
        {
            levels.Add(CreateCurrentSwingLevel(
                snapshot, swing, ImportantPriceLevelSource.CurrentSwingLow,
                ImportantPriceLevelRole.Support));
        }

        foreach (var swing in highs)
        {
            levels.Add(CreateCurrentSwingLevel(
                snapshot, swing, ImportantPriceLevelSource.CurrentSwingHigh,
                ImportantPriceLevelRole.Resistance));
        }
    }

    private static ImportantPriceLevel CreateCurrentSwingLevel(
        FeatureSnapshot snapshot,
        ConfirmedSwing swing,
        ImportantPriceLevelSource source,
        ImportantPriceLevelRole role) =>
        new(
            CreateLevelId(
                snapshot.Symbol,
                snapshot.SessionId,
                swing.SessionId,
                source,
                swing.Id.ToString("N"),
                swing.Price),
            snapshot.Symbol,
            snapshot.SessionId,
            swing.SessionId,
            swing.Price,
            role,
            source,
            swing.Id,
            null,
            swing.OriginEventIdentity,
            swing.ConfirmationEventIdentity,
            swing.EventTimeUtc,
            swing.ConfirmedAtEventTimeUtc,
            MarketSituationContextMode.CurrentSessionOnly,
            "Complete current-session retained event history",
            snapshot.MarketData.Provider,
            null,
            null,
            swing.AlgorithmVersion,
            swing.FeatureSetVersion,
            null);

    private static ImportantPriceLevel CreateSwingLevel(
        FeatureSnapshot snapshot,
        StructuralLevelCandidate candidate,
        ImportantPriceLevelSource source,
        ImportantPriceLevelRole role,
        MarketSituationContextMode contextMode,
        string completeness,
        PriorSessionContext context) =>
        new(
            CreateLevelId(
                snapshot.Symbol,
                snapshot.SessionId,
                candidate.SessionId,
                source,
                candidate.SourceSwingId.ToString("N"),
                candidate.Price),
            snapshot.Symbol,
            snapshot.SessionId,
            candidate.SessionId,
            candidate.Price,
            role,
            source,
            candidate.SourceSwingId,
            context.ContextId,
            candidate.OriginEventIdentity,
            candidate.ConfirmationEventIdentity,
            candidate.EventTimeUtc,
            candidate.ConfirmedAtEventTimeUtc,
            contextMode,
            completeness,
            context.HistoricalSource,
            context.CoverageStartUtc,
            context.CoverageEndUtc,
            candidate.AlgorithmVersion,
            candidate.FeatureSetVersion,
            context.ContextDefinitionVersion);

    private static ImportantPriceLevel? NearestLevel(
        IEnumerable<ImportantPriceLevel> levels,
        ImportantPriceLevelRole role,
        decimal referencePrice) =>
        levels.Where(item => item.Role == role)
            .OrderBy(item => Math.Abs(referencePrice - item.Price))
            .ThenBy(item => SourcePrecedence(item.Source))
            .ThenBy(item => item.Id)
            .FirstOrDefault();

    private static MarketSituationInteraction? Classify(
        ImportantPriceLevel level,
        decimal referencePrice,
        MarketSituationConfiguration configuration)
    {
        var halfWidth = level.Price * configuration.TestZonePercent / 100m;
        var zoneLow = level.Price - halfWidth;
        var zoneHigh = level.Price + halfWidth;
        MarketSituationType? type = null;
        decimal distanceToActiveZone = 0m;
        decimal edgePrice = level.Price;

        if (zoneLow <= referencePrice && referencePrice <= zoneHigh)
        {
            type = level.Role == ImportantPriceLevelRole.Support
                ? MarketSituationType.TestingSupport
                : MarketSituationType.TestingResistance;
        }
        else if (level.Role == ImportantPriceLevelRole.Support && referencePrice > zoneHigh)
        {
            edgePrice = zoneHigh;
            distanceToActiveZone = referencePrice - zoneHigh;
            if (IsWithinApproachDistance(distanceToActiveZone, edgePrice, configuration))
            {
                type = MarketSituationType.ApproachingSupport;
            }
        }
        else if (level.Role == ImportantPriceLevelRole.Support && referencePrice < zoneLow)
        {
            edgePrice = zoneLow;
            distanceToActiveZone = zoneLow - referencePrice;
            if (IsWithinApproachDistance(distanceToActiveZone, edgePrice, configuration))
            {
                type = MarketSituationType.CrossedSupport;
            }
        }
        else if (level.Role == ImportantPriceLevelRole.Resistance && referencePrice < zoneLow)
        {
            edgePrice = zoneLow;
            distanceToActiveZone = zoneLow - referencePrice;
            if (IsWithinApproachDistance(distanceToActiveZone, edgePrice, configuration))
            {
                type = MarketSituationType.ApproachingResistance;
            }
        }
        else if (level.Role == ImportantPriceLevelRole.Resistance && referencePrice > zoneHigh)
        {
            edgePrice = zoneHigh;
            distanceToActiveZone = referencePrice - zoneHigh;
            if (IsWithinApproachDistance(distanceToActiveZone, edgePrice, configuration))
            {
                type = MarketSituationType.CrossedResistance;
            }
        }

        if (type is null)
        {
            return null;
        }

        return new MarketSituationInteraction(
            type.Value,
            level,
            zoneLow,
            zoneHigh,
            level.Price - referencePrice,
            Math.Abs(level.Price - referencePrice),
            Math.Abs(level.Price - referencePrice) / referencePrice * 100m,
            distanceToActiveZone,
            distanceToActiveZone == 0m ? 0m : distanceToActiveZone / edgePrice * 100m);
    }

    private static bool IsWithinApproachDistance(
        decimal distance,
        decimal zoneEdge,
        MarketSituationConfiguration configuration) =>
        distance / zoneEdge * 100m <= configuration.ApproachDistancePercent;

    private static MarketSituationPrimary? ChoosePrimary(
        MarketSituationInteraction? support,
        MarketSituationInteraction? resistance)
    {
        if (support is null)
        {
            return resistance is null
                ? null
                : ToPrimary(MarketSituationSide.Resistance, resistance);
        }

        if (resistance is null || support.DistanceToActiveZone <= resistance.DistanceToActiveZone)
        {
            return ToPrimary(MarketSituationSide.Support, support);
        }

        return ToPrimary(MarketSituationSide.Resistance, resistance);
    }

    private static MarketSituationPrimary ToPrimary(
        MarketSituationSide side,
        MarketSituationInteraction interaction) =>
        new(side, interaction.SituationType, interaction.Level.Id, interaction.DistanceToActiveZone);

    private static bool IsPriorSessionLevel(ImportantPriceLevel level) =>
        level.Source is ImportantPriceLevelSource.PriorSwingHigh
            or ImportantPriceLevelSource.PriorSwingLow
            or ImportantPriceLevelSource.PriorSessionHigh
            or ImportantPriceLevelSource.PriorSessionLow;

    private static int SourcePrecedence(ImportantPriceLevelSource source) => source switch
    {
        ImportantPriceLevelSource.PriorSwingHigh or ImportantPriceLevelSource.PriorSwingLow => 0,
        ImportantPriceLevelSource.CurrentSwingHigh or ImportantPriceLevelSource.CurrentSwingLow => 1,
        ImportantPriceLevelSource.PriorSessionHigh or ImportantPriceLevelSource.PriorSessionLow => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown important price level source.")
    };

    private static Guid CreateLevelId(
        string symbol,
        string currentSessionId,
        string sourceSessionId,
        ImportantPriceLevelSource source,
        string structuralIdentity,
        decimal price)
    {
        var canonical = string.Join(
            "\u001f",
            symbol.ToUpperInvariant(),
            currentSessionId,
            sourceSessionId,
            source.ToString(),
            structuralIdentity,
            price.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
        return DeterministicId(canonical);
    }

    private static Guid DeterministicId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
