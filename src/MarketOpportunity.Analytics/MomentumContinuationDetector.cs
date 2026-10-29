using MarketOpportunity.Domain;
using System.Globalization;

namespace MarketOpportunity.Analytics;

public sealed class MomentumContinuationDetector
{
    public Opportunity Evaluate(FeatureSnapshot snapshot, ScannerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(configuration);

        var vetoes = new List<string>();
        var supporting = new List<OpportunityEvidence>();
        var contradictions = new List<OpportunityEvidence>();
        var isStale = snapshot.QuoteAge is null
            || snapshot.QuoteAge.Value > TimeSpan.FromSeconds(configuration.MaximumQuoteAgeSeconds)
            || snapshot.QuoteAge.Value < TimeSpan.Zero;

        if (isStale)
        {
            vetoes.Add("StaleQuote");
        }

        if (!snapshot.IsQuoteValid || !snapshot.IsPriceDataValid || snapshot.ReferencePrice is null or <= 0)
        {
            vetoes.Add("InvalidQuoteOrPrice");
        }

        if (snapshot.SpreadBasisPoints is null
            || snapshot.SpreadBasisPoints > configuration.MaximumSpreadBasisPoints)
        {
            vetoes.Add("ExcessiveSpread");
        }

        if (!snapshot.HasRequiredData || !snapshot.HasCompleteSessionData)
        {
            vetoes.Add("InsufficientRequiredFeatureData");
        }

        switch (snapshot.SessionRealizedVolatilityStatus)
        {
            case RealizedVolatilityStatus.Available
                when snapshot.SessionRealizedVolatilityPercent is null or < 0:
                vetoes.Add("InvalidVolatilityData");
                break;
            case RealizedVolatilityStatus.Available
                when snapshot.HasCompleteSessionData
                    && snapshot.SessionRealizedVolatilityPercent is decimal volatilityPercent
                    && volatilityPercent >= configuration.MaximumSessionRealizedVolatilityPercent:
                vetoes.Add("ExcessiveRealizedVolatility");
                break;
            case RealizedVolatilityStatus.Available:
                break;
            case RealizedVolatilityStatus.Insufficient
                when snapshot.SessionRealizedVolatilityPercent is null:
                vetoes.Add("VolatilityUnavailable");
                break;
            default:
                vetoes.Add("InvalidVolatilityData");
                break;
        }

        switch (snapshot.ShortHorizonDollarVolumeStatus)
        {
            case ShortHorizonDollarVolumeStatus.Available
                when snapshot.ShortHorizonDollarVolumeUsd is null or < 0:
                vetoes.Add("InvalidDollarVolumeData");
                break;
            case ShortHorizonDollarVolumeStatus.Available
                when snapshot.HasCompleteSessionData
                    && snapshot.ShortHorizonDollarVolumeUsd is decimal dollarVolumeUsd
                    && dollarVolumeUsd < configuration.MinimumShortHorizonDollarVolumeUsd:
                vetoes.Add("InsufficientDollarVolume");
                break;
            case ShortHorizonDollarVolumeStatus.Available:
                break;
            case ShortHorizonDollarVolumeStatus.Insufficient
                when snapshot.ShortHorizonDollarVolumeUsd is null:
                vetoes.Add("DollarVolumeUnavailable");
                break;
            default:
                vetoes.Add("InvalidDollarVolumeData");
                break;
        }

        var momentum = snapshot.ShortHorizonReturnPercent;
        var vwapPosition = snapshot.PriceVsVwapPercent;
        var direction = momentum >= configuration.MinimumDirectionalReturnPercent
            ? SetupBias.Long
            : momentum <= -configuration.MinimumDirectionalReturnPercent
                ? SetupBias.Short
                : SetupBias.Neutral;
        var invalidationCondition = direction switch
        {
            SetupBias.Long when snapshot.SessionVwap is > 0 =>
                $"Long invalidated if the last trade moves below session VWAP ({snapshot.SessionVwap.Value:F4}).",
            SetupBias.Short when snapshot.SessionVwap is > 0 =>
                $"Short invalidated if the last trade moves above session VWAP ({snapshot.SessionVwap.Value:F4}).",
            _ => "No directional invalidation level is available from the current features."
        };
        if (direction == SetupBias.Neutral || snapshot.SessionVwap is null or <= 0)
        {
            vetoes.Add("NoDefensibleInvalidation");
        }

        var score = 0;
        if (direction != SetupBias.Neutral)
        {
            score += configuration.MomentumScoreWeight;
            supporting.Add(new OpportunityEvidence(
                "DirectionalMomentum",
                $"Short-horizon return is {momentum:F3}% in the {direction.ToString().ToLowerInvariant()} direction."));
        }
        else
        {
            contradictions.Add(new OpportunityEvidence(
                "WeakMomentum",
                "Short-horizon return does not meet the configured directional threshold."));
        }

        var alignedWithVwap = direction switch
        {
            SetupBias.Long => vwapPosition > 0,
            SetupBias.Short => vwapPosition < 0,
            _ => false
        };
        if (alignedWithVwap)
        {
            score += configuration.VwapScoreWeight;
            supporting.Add(new OpportunityEvidence(
                "VwapAlignment",
                $"Price is {Math.Abs(vwapPosition!.Value):F3}% {(vwapPosition > 0 ? "above" : "below")} session VWAP."));
        }
        else if (direction != SetupBias.Neutral)
        {
            contradictions.Add(new OpportunityEvidence(
                "VwapConflict",
                "Price is not on the momentum-aligned side of session VWAP."));
        }

        if (snapshot.ShortHorizonVolume is > 0)
        {
            score += configuration.ActivityScoreWeight;
            supporting.Add(new OpportunityEvidence(
                "RecentTradingActivity",
                $"Observed {snapshot.ShortHorizonVolume} shares in the configured momentum window."));
        }

        if (!snapshot.HasCompleteSessionData)
        {
            contradictions.Add(new OpportunityEvidence(
                "TruncatedEventState",
                "The bounded event state evicted earlier session events; session features may be incomplete."));
        }

        if (!snapshot.IsQuoteValid)
        {
            contradictions.Add(new OpportunityEvidence(
                "InvalidQuote",
                "The latest quote has non-positive prices or its ask is below its bid."));
        }

        if (!snapshot.IsPriceDataValid)
        {
            contradictions.Add(new OpportunityEvidence(
                "InvalidTradeData",
                "At least one available trade has a non-positive price or size."));
        }

        score = Math.Clamp(score, 0, 100);
        var classification = !vetoes.Count.Equals(0)
            ? OpportunityClassification.AvoidNoTrade
            : direction switch
            {
                SetupBias.Long when score >= configuration.ProbableScoreThreshold =>
                    OpportunityClassification.ProbableLong,
                SetupBias.Short when score >= configuration.ProbableScoreThreshold =>
                    OpportunityClassification.ProbableShort,
                _ when score >= configuration.WatchScoreThreshold => OpportunityClassification.Watch,
                _ => OpportunityClassification.AvoidNoTrade
            };

        var asOfUtc = snapshot.AsOfUtc;
        var validUntilUtc = asOfUtc.AddSeconds(configuration.OpportunityExpirationSeconds);
        var opportunityIdentity = string.Join(
            "\u001f",
            snapshot.Id.ToString("N"),
            snapshot.SessionId,
            configuration.SetupDefinitionVersion,
            configuration.Version,
            direction.ToString(),
            classification.ToString(),
            score.ToString(CultureInfo.InvariantCulture),
            validUntilUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            invalidationCondition);
        return new Opportunity(
            DeterministicId.From(opportunityIdentity),
            snapshot.Symbol,
            snapshot.SessionId,
            asOfUtc,
            validUntilUtc,
            "Momentum Continuation",
            direction,
            classification,
            score,
            supporting,
            contradictions,
            vetoes,
            invalidationCondition,
            snapshot.ReferencePrice,
            snapshot.FeatureSetVersion,
            configuration.SetupDefinitionVersion,
            configuration.Version,
            snapshot.MarketData);
    }
}
