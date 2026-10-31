using System.Globalization;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed class TradePlanBuilder
{
    public const string DefinitionVersion = "trade-plan-v1";

    public TradePlan Build(FeatureSnapshot snapshot, Opportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(opportunity);
        if (snapshot.Id == Guid.Empty
            || snapshot.AsOfUtc != opportunity.AsOfUtc
            || !string.Equals(snapshot.Symbol, opportunity.Symbol, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.SessionId, opportunity.SessionId, StringComparison.Ordinal)
            || !string.Equals(snapshot.FeatureSetVersion, opportunity.FeatureSetVersion, StringComparison.Ordinal)
            || snapshot.MarketData != opportunity.MarketData)
        {
            throw new ArgumentException("The feature snapshot and opportunity must describe the same analysis.");
        }

        var direction = opportunity.Bias switch
        {
            SetupBias.Long => TradeDirection.Long,
            SetupBias.Short => TradeDirection.Short,
            _ => (TradeDirection?)null
        };
        var evidence = new List<TradePlanEvidence>();
        var status = TradePlanStatus.InsufficientStructure;
        PriceZone? entryZone = null;
        decimal? entryReference = null;
        decimal? invalidationPrice = null;
        TradeTarget? target1 = null;
        decimal? riskPerShare = null;
        decimal? rewardPerShare = null;
        decimal? rewardRiskRatio = null;
        StructuralLevelCandidate? anchor = null;
        ConfirmedSwing? invalidationSwing = null;

        if (direction is null)
        {
            evidence.Add(new TradePlanEvidence(
                "DirectionalSetupUnavailable",
                "A long or short analytical setup is required to form a structural plan."));
        }
        else if (!snapshot.IsPriceDataValid || snapshot.ReferencePrice is null or <= 0)
        {
            status = TradePlanStatus.Rejected;
            evidence.Add(new TradePlanEvidence(
                "InvalidReferencePrice",
                "A positive, valid reference price is required for structural plan geometry."));
        }
        else if (snapshot.MarketStructureStatus == MarketStructureStatus.InvalidPriceData)
        {
            status = TradePlanStatus.Rejected;
            evidence.Add(new TradePlanEvidence(
                "InvalidStructurePrices",
                "Market structure contains invalid trade prices."));
        }
        else if (snapshot.MarketStructureStatus != MarketStructureStatus.Available)
        {
            evidence.Add(new TradePlanEvidence(
                "InsufficientStructure",
                "Confirmed market structure is not available for a candidate plan."));
        }
        else
        {
            var referencePrice = snapshot.ReferencePrice.Value;
            anchor = direction == TradeDirection.Long
                ? snapshot.NearestSupportCandidate
                : snapshot.NearestResistanceCandidate;
            var opposingLevel = direction == TradeDirection.Long
                ? snapshot.NearestResistanceCandidate
                : snapshot.NearestSupportCandidate;
            invalidationSwing = FindInvalidationSwing(snapshot, direction.Value, anchor);

            if (anchor is null)
            {
                evidence.Add(new TradePlanEvidence(
                    "EntryStructureUnavailable",
                    direction == TradeDirection.Long
                        ? "No confirmed support at or below the reference price defines a long entry zone."
                        : "No confirmed resistance at or above the reference price defines a short entry zone."));
            }
            else if (!IsValidLevel(anchor, snapshot, direction == TradeDirection.Long
                         ? StructuralLevelType.Support
                         : StructuralLevelType.Resistance))
            {
                status = TradePlanStatus.Rejected;
                evidence.Add(new TradePlanEvidence(
                    "InvalidEntryStructure",
                    "The entry level does not match this symbol, session, or plan direction.",
                    anchor.SourceSwingId,
                    anchor.Price,
                    anchor.OriginEventIdentity,
                    anchor.ConfirmationEventIdentity));
            }
            else
            {
                var anchorIsOnExpectedSide = direction == TradeDirection.Long
                    ? anchor.Price <= referencePrice
                    : anchor.Price >= referencePrice;
                if (!anchorIsOnExpectedSide)
                {
                    status = TradePlanStatus.Rejected;
                    evidence.Add(new TradePlanEvidence(
                        "EntryStructureOutsideReference",
                        "The confirmed entry structure is on the wrong side of the reference price.",
                        anchor.SourceSwingId,
                        anchor.Price,
                        anchor.OriginEventIdentity,
                        anchor.ConfirmationEventIdentity));
                }
                else
                {
                    entryZone = direction == TradeDirection.Long
                        ? new PriceZone(anchor.Price, referencePrice)
                        : new PriceZone(referencePrice, anchor.Price);
                    entryReference = entryZone.Low + (entryZone.High - entryZone.Low) / 2m;
                    evidence.Add(new TradePlanEvidence(
                        direction == TradeDirection.Long
                            ? "EntryZoneFromConfirmedSupport"
                            : "EntryZoneFromConfirmedResistance",
                        "The entry zone spans the nearest confirmed structural level and the current reference price; its midpoint is used for risk and reward arithmetic.",
                        anchor.SourceSwingId,
                        anchor.Price,
                        anchor.OriginEventIdentity,
                        anchor.ConfirmationEventIdentity));
                }
            }

            if (opposingLevel is null)
            {
                evidence.Add(new TradePlanEvidence(
                    "TargetStructureUnavailable",
                    direction == TradeDirection.Long
                        ? "No confirmed resistance is available for a structural long target."
                        : "No confirmed support is available for a structural short target."));
            }
            else if (!IsValidLevel(opposingLevel, snapshot, direction == TradeDirection.Long
                         ? StructuralLevelType.Resistance
                         : StructuralLevelType.Support))
            {
                status = TradePlanStatus.Rejected;
                evidence.Add(new TradePlanEvidence(
                    "InvalidTargetStructure",
                    "The opposing structural level does not match this symbol, session, or plan direction.",
                    opposingLevel.SourceSwingId,
                    opposingLevel.Price,
                    opposingLevel.OriginEventIdentity,
                    opposingLevel.ConfirmationEventIdentity));
            }
            else
            {
                target1 = new TradeTarget(
                    opposingLevel.Price,
                    opposingLevel.SourceSwingId,
                    opposingLevel.OriginEventIdentity,
                    opposingLevel.ConfirmationEventIdentity,
                    opposingLevel.AlgorithmVersion);
                evidence.Add(new TradePlanEvidence(
                    direction == TradeDirection.Long
                        ? "TargetFromConfirmedResistance"
                        : "TargetFromConfirmedSupport",
                    "Target 1 uses the nearest confirmed opposing structural level.",
                    opposingLevel.SourceSwingId,
                    opposingLevel.Price,
                    opposingLevel.OriginEventIdentity,
                    opposingLevel.ConfirmationEventIdentity));
            }

            if (invalidationSwing is null || anchor is null)
            {
                evidence.Add(new TradePlanEvidence(
                    "InvalidationStructureUnavailable",
                    "A separate confirmed swing beyond the entry structure is required for numeric invalidation."));
            }
            else
            {
                invalidationPrice = invalidationSwing.Price;
                evidence.Add(new TradePlanEvidence(
                    direction == TradeDirection.Long
                        ? "InvalidationFromLowerConfirmedSwing"
                        : "InvalidationFromHigherConfirmedSwing",
                    "Numeric invalidation uses the nearest confirmed swing beyond the entry structure, with no synthetic price buffer.",
                    invalidationSwing.Id,
                    invalidationSwing.Price,
                    invalidationSwing.OriginEventIdentity,
                    invalidationSwing.ConfirmationEventIdentity));
            }

            if (status != TradePlanStatus.Rejected)
            {
                if (entryZone is null || entryReference is null
                    || invalidationPrice is null || target1 is null)
                {
                    status = entryZone is null
                        ? TradePlanStatus.InsufficientStructure
                        : TradePlanStatus.PlanForming;
                }
                else
                {
                    var targetIsProfitable = direction == TradeDirection.Long
                        ? target1.Price > entryZone.High
                        : target1.Price < entryZone.Low;
                    var invalidationIsBeyondEntryStructure = direction == TradeDirection.Long
                        ? invalidationPrice.Value < entryZone.Low
                        : invalidationPrice.Value > entryZone.High;
                    var invalidationIsValid = invalidationSwing is not null
                        && invalidationSwing.Price > 0
                        && invalidationSwing.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
                        && invalidationSwing.SessionId.Equals(snapshot.SessionId, StringComparison.Ordinal)
                        && invalidationSwing.FeatureSetVersion.Equals(snapshot.FeatureSetVersion, StringComparison.Ordinal)
                        && invalidationSwing.EventTimeUtc <= invalidationSwing.ConfirmedAtEventTimeUtc
                        && invalidationSwing.ConfirmedAtEventTimeUtc <= snapshot.AsOfUtc
                        && !string.IsNullOrWhiteSpace(invalidationSwing.AlgorithmVersion)
                        && !string.IsNullOrWhiteSpace(invalidationSwing.OriginEventIdentity)
                        && !string.IsNullOrWhiteSpace(invalidationSwing.ConfirmationEventIdentity)
                        && invalidationSwing.Type == (direction == TradeDirection.Long
                            ? ConfirmedSwingType.ConfirmedSwingLow
                            : ConfirmedSwingType.ConfirmedSwingHigh);
                    if (!targetIsProfitable || !invalidationIsBeyondEntryStructure || !invalidationIsValid)
                    {
                        status = TradePlanStatus.Rejected;
                        evidence.Add(new TradePlanEvidence(
                            !targetIsProfitable
                                ? "TargetInsideEntryZone"
                                : "InvalidationInsideEntryZone",
                            !targetIsProfitable
                                ? "The structural target must be beyond the profitable edge of the entry zone."
                                : "Numeric invalidation must be a valid confirmed swing beyond the entry structure."));
                    }
                    else
                    {
                        riskPerShare = direction == TradeDirection.Long
                            ? entryReference.Value - invalidationPrice.Value
                            : invalidationPrice.Value - entryReference.Value;
                        rewardPerShare = direction == TradeDirection.Long
                            ? target1.Price - entryReference.Value
                            : entryReference.Value - target1.Price;
                        if (riskPerShare <= 0 || rewardPerShare <= 0)
                        {
                            status = TradePlanStatus.Rejected;
                            evidence.Add(new TradePlanEvidence(
                                "NonPositiveRiskOrReward",
                                "Risk and reward must both be positive for defined plan geometry."));
                        }
                        else
                        {
                            rewardRiskRatio = rewardPerShare.Value / riskPerShare.Value;
                            status = snapshot.HasCompleteSessionData
                                ? TradePlanStatus.PlanDefined
                                : TradePlanStatus.PlanForming;
                            if (!snapshot.HasCompleteSessionData)
                            {
                                evidence.Add(new TradePlanEvidence(
                                    "PartialRetainedState",
                                    "The bounded event state is incomplete; the calculated structure is retained as forming, not defined."));
                            }
                        }
                    }
                }
            }
        }

        var structureVersion = StructureVersion(
            snapshot,
            entryZone,
            target1,
            anchor?.AlgorithmVersion,
            invalidationSwing?.AlgorithmVersion);
        var evidenceSnapshot = Array.AsReadOnly(evidence.ToArray());
        var id = CreateIdentity(
            snapshot,
            opportunity,
            direction,
            status,
            entryZone,
            entryReference,
            invalidationPrice,
            target1,
            riskPerShare,
            rewardPerShare,
            rewardRiskRatio,
            structureVersion,
            evidenceSnapshot);
        return new TradePlan(
            id,
            snapshot.Symbol,
            snapshot.SessionId,
            snapshot.AsOfUtc,
            opportunity.ValidUntilUtc,
            direction,
            status,
            entryZone,
            entryReference,
            invalidationPrice,
            target1,
            null,
            riskPerShare,
            rewardPerShare,
            rewardRiskRatio,
            opportunity.SetupType,
            opportunity.Id,
            structureVersion,
            snapshot.FeatureSetVersion,
            opportunity.SetupDefinitionVersion,
            opportunity.ConfigurationVersion,
            DefinitionVersion,
            snapshot.MarketData,
            evidenceSnapshot);
    }

    private static ConfirmedSwing? FindInvalidationSwing(
        FeatureSnapshot snapshot,
        TradeDirection direction,
        StructuralLevelCandidate? anchor)
    {
        if (anchor is null)
        {
            return null;
        }

        var swings = (direction == TradeDirection.Long
                ? new[] { snapshot.LatestConfirmedSwingLow, snapshot.PreviousConfirmedSwingLow }
                : new[] { snapshot.LatestConfirmedSwingHigh, snapshot.PreviousConfirmedSwingHigh })
            .OfType<ConfirmedSwing>()
            .Where(swing => swing.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
                && swing.SessionId.Equals(snapshot.SessionId, StringComparison.Ordinal)
                && swing.FeatureSetVersion.Equals(snapshot.FeatureSetVersion, StringComparison.Ordinal)
                && swing.EventTimeUtc <= swing.ConfirmedAtEventTimeUtc
                && swing.ConfirmedAtEventTimeUtc <= snapshot.AsOfUtc
                && swing.Type == (direction == TradeDirection.Long
                    ? ConfirmedSwingType.ConfirmedSwingLow
                    : ConfirmedSwingType.ConfirmedSwingHigh)
                && swing.Id != anchor.SourceSwingId
                && swing.Price > 0)
            .ToArray();
        var beyondAnchor = swings
            .Where(swing => direction == TradeDirection.Long
                ? swing.Price < anchor.Price
                : swing.Price > anchor.Price)
            .ToArray();
        return (beyondAnchor.Length > 0 ? beyondAnchor : swings)
            .OrderBy(swing => Math.Abs(swing.Price - anchor.Price))
            .ThenByDescending(swing => swing.EventTimeUtc)
            .ThenBy(swing => swing.Id)
            .FirstOrDefault();
    }

    private static bool IsValidLevel(
        StructuralLevelCandidate level,
        FeatureSnapshot snapshot,
        StructuralLevelType expectedType) =>
        level.Type == expectedType
        && level.Price > 0
        && level.SourceSwingId != Guid.Empty
        && level.Symbol.Equals(snapshot.Symbol, StringComparison.OrdinalIgnoreCase)
        && level.SessionId.Equals(snapshot.SessionId, StringComparison.Ordinal)
        && level.FeatureSetVersion.Equals(snapshot.FeatureSetVersion, StringComparison.Ordinal)
        && level.EventTimeUtc <= level.ConfirmedAtEventTimeUtc
        && level.ConfirmedAtEventTimeUtc <= snapshot.AsOfUtc
        && !string.IsNullOrWhiteSpace(level.AlgorithmVersion)
        && !string.IsNullOrWhiteSpace(level.OriginEventIdentity)
        && !string.IsNullOrWhiteSpace(level.ConfirmationEventIdentity);

    private static string StructureVersion(
        FeatureSnapshot snapshot,
        PriceZone? entryZone,
        TradeTarget? target,
        string? anchorVersion,
        string? invalidationVersion) =>
        new[]
            {
                anchorVersion,
                invalidationVersion,
                snapshot.LatestConfirmedSwingHigh?.AlgorithmVersion,
                snapshot.LatestConfirmedSwingLow?.AlgorithmVersion,
                snapshot.PreviousConfirmedSwingHigh?.AlgorithmVersion,
                snapshot.PreviousConfirmedSwingLow?.AlgorithmVersion,
                target?.StructureAlgorithmVersion
            }
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .DefaultIfEmpty(entryZone is null ? "unavailable" : "unknown")
            .Aggregate((left, right) => $"{left}+{right}");

    private static Guid CreateIdentity(
        FeatureSnapshot snapshot,
        Opportunity opportunity,
        TradeDirection? direction,
        TradePlanStatus status,
        PriceZone? entryZone,
        decimal? entryReference,
        decimal? invalidationPrice,
        TradeTarget? target1,
        decimal? riskPerShare,
        decimal? rewardPerShare,
        decimal? rewardRiskRatio,
        string structureVersion,
        IReadOnlyList<TradePlanEvidence> evidence)
    {
        var values = new List<string>
        {
            DefinitionVersion,
            snapshot.Id.ToString("N"),
            opportunity.Id.ToString("N"),
            snapshot.Symbol,
            snapshot.SessionId,
            snapshot.AsOfUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            opportunity.ValidUntilUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            direction?.ToString() ?? "",
            status.ToString(),
            Format(entryZone?.Low),
            Format(entryZone?.High),
            Format(entryReference),
            Format(invalidationPrice),
            target1?.SourceSwingId.ToString("N") ?? "",
            Format(target1?.Price),
            Format(riskPerShare),
            Format(rewardPerShare),
            Format(rewardRiskRatio),
            opportunity.SetupType,
            structureVersion,
            snapshot.FeatureSetVersion,
            opportunity.SetupDefinitionVersion,
            opportunity.ConfigurationVersion,
            snapshot.MarketData.Provider,
            snapshot.MarketData.Feed,
            snapshot.MarketData.Coverage.ToString(),
            snapshot.MarketData.IsRealTime.ToString(CultureInfo.InvariantCulture),
            snapshot.MarketData.SupportsTrades.ToString(CultureInfo.InvariantCulture),
            snapshot.MarketData.SupportsQuotes.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var item in evidence)
        {
            values.AddRange(
            [
                item.Code,
                item.Description,
                item.SourceSwingId?.ToString("N") ?? "",
                Format(item.Price),
                item.OriginEventIdentity ?? "",
                item.ConfirmationEventIdentity ?? ""
            ]);
        }

        return DeterministicId.From(string.Join("\u001f", values));
    }

    private static string Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
}
