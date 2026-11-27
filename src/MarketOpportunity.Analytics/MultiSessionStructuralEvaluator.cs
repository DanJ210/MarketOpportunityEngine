using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Analytics;

public sealed class MultiSessionStructuralEvaluator
{
    public const string DefinitionVersion = "multi-session-structure-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MultiSessionStructuralAssessment Evaluate(
        StructuralAssessmentInput input,
        ScannerConfiguration scannerConfiguration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(scannerConfiguration);
        scannerConfiguration.Validate();
        ValidateInputIdentity(input);

        var configuration = scannerConfiguration.MultiSessionStructure;
        var sessions = CanonicalSessions(input.HistoricalSessions);
        var completeness = EvaluateCompleteness(input, sessions, configuration);
        if (completeness.Status != StructuralAssessmentCompleteness.Available)
        {
            return CreateUnavailableAssessment(
                input,
                sessions,
                scannerConfiguration,
                completeness);
        }

        var asOfSessions = sessions
            .Where(item => item.EvidenceKnownAtUtc <= input.AssessmentAsOfUtc)
            .ToArray();
        var observations = CreateSessionExtrema(
            asOfSessions,
            input.AssessmentAsOfUtc,
            scannerConfiguration.Version,
            configuration);
        var recurringPivotObservations = CreateRecurringPivotObservations(
            asOfSessions,
            input.AssessmentAsOfUtc,
            scannerConfiguration.Version,
            configuration);
        observations.AddRange(recurringPivotObservations);
        observations = observations
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderBy(item => item.Role)
            .ThenBy(item => item.Price)
            .ThenBy(item => item.SourceSessionId, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToList();

        var supportZones = CreateZones(
            observations,
            StructuralRole.Support,
            scannerConfiguration.Version,
            configuration);
        var resistanceZones = CreateZones(
            observations,
            StructuralRole.Resistance,
            scannerConfiguration.Version,
            configuration);
        var projectedSupport = ProjectZones(
            supportZones,
            configuration.MaximumProjectedSupportZones,
            StructuralRole.Support);
        var projectedResistance = ProjectZones(
            resistanceZones,
            configuration.MaximumProjectedResistanceZones,
            StructuralRole.Resistance);
        var trend = EvaluateTrend(asOfSessions, configuration.StructuralEqualityTolerancePercent);
        var evidenceKnownAtUtc = observations.Count == 0
            ? trend.EvidenceKnownAtUtc
            : observations.Max(item => item.EvidenceKnownAtUtc);
        evidenceKnownAtUtc = Later(evidenceKnownAtUtc, trend.EvidenceKnownAtUtc);
        var assessmentContent = new
        {
            input.Symbol,
            input.CurrentSessionId,
            input.ImmediatePriorSessionId,
            input.AssessmentAsOfUtc,
            DefinitionVersion,
            ConfigurationVersion = scannerConfiguration.Version,
            Configuration = MaterialConfiguration(configuration),
            HistoricalSessionIds = sessions.Select(item => item.SessionId).ToArray(),
            Completeness = completeness,
            Trend = trend,
            AllSupportZoneIds = supportZones.Select(item => item.Id).ToArray(),
            AllResistanceZoneIds = resistanceZones.Select(item => item.Id).ToArray()
        };

        return new MultiSessionStructuralAssessment(
            DeterministicId(assessmentContent),
            input.Symbol,
            input.CurrentSessionId,
            input.ImmediatePriorSessionId,
            input.AssessmentAsOfUtc,
            evidenceKnownAtUtc,
            DefinitionVersion,
            scannerConfiguration.Version,
            sessions.Select(item => item.SessionId).ToArray(),
            completeness,
            trend,
            projectedSupport,
            projectedResistance,
            supportZones.Count - projectedSupport.Count,
            resistanceZones.Count - projectedResistance.Count);
    }

    public static decimal? PercentagePriceDistance(decimal firstPrice, decimal secondPrice)
    {
        if (firstPrice <= 0 || secondPrice <= 0)
        {
            return null;
        }

        var lowerPrice = Math.Min(firstPrice, secondPrice);
        return Math.Abs(firstPrice - secondPrice) / lowerPrice * 100m;
    }

    public static bool IsWithinPercentageDistance(
        decimal firstPrice,
        decimal secondPrice,
        decimal maximumDistancePercent)
    {
        if (maximumDistancePercent < 0 || firstPrice <= 0 || secondPrice <= 0)
        {
            return false;
        }

        return PercentagePriceDistance(firstPrice, secondPrice) <= maximumDistancePercent;
    }

    private static void ValidateInputIdentity(StructuralAssessmentInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CurrentSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ImmediatePriorSessionId);
        ArgumentNullException.ThrowIfNull(input.HistoricalSessions);
    }

    private static StructuralSessionSummary[] CanonicalSessions(
        IReadOnlyList<StructuralSessionSummary> sessions) =>
        sessions
            .GroupBy(item => item.SessionId, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(item => SessionFingerprint(item), StringComparer.Ordinal)
                    .ToArray();
                if (ordered.Skip(1).Any(item => SessionFingerprint(item) != SessionFingerprint(ordered[0])))
                {
                    return ordered[0] with
                    {
                        Completeness = StructuralSessionCompleteness.Invalid
                    };
                }

                return ordered[0];
            })
            .OrderBy(item => item.StartUtc)
            .ThenBy(item => item.EndUtc)
            .ThenBy(item => item.SessionId, StringComparer.Ordinal)
            .ToArray();

    private static MultiSessionStructuralCompleteness EvaluateCompleteness(
        StructuralAssessmentInput input,
        IReadOnlyList<StructuralSessionSummary> sessions,
        MultiSessionStructureConfiguration configuration)
    {
        var sessionIds = sessions.Select(item => item.SessionId).ToArray();
        var contiguous = IsContiguous(sessions);
        var uniform = HasUniformProvenance(sessions);
        if (sessions.Count != configuration.HistoricalSessionCount)
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Insufficient,
                contiguous,
                uniform,
                sessionIds,
                $"Exactly {configuration.HistoricalSessionCount} historical sessions are required.");
        }

        if (sessions.Any(item => item.Completeness == StructuralSessionCompleteness.Invalid))
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Invalid,
                contiguous,
                uniform,
                sessionIds,
                "At least one required historical session is invalid.");
        }

        if (sessions.Any(item => item.Completeness == StructuralSessionCompleteness.Partial))
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Partial,
                contiguous,
                uniform,
                sessionIds,
                "At least one required historical session is partial.");
        }

        if (sessions.Any(item => item.Completeness == StructuralSessionCompleteness.Insufficient))
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Insufficient,
                contiguous,
                uniform,
                sessionIds,
                "At least one required historical session has insufficient evidence.");
        }

        if (sessions.Any(item =>
                item.Completeness != StructuralSessionCompleteness.Available
                || !item.DeclaresCompleteCoverage
                || item.HasKnownGaps
                || item.High is not > 0
                || item.Low is not > 0
                || item.Low > item.High
                || item.EvidenceKnownAtUtc is null
                || item.EvidenceKnownAtUtc < item.EndUtc
                || item.HighEventTimeUtc is null
                || item.LowEventTimeUtc is null
                || item.HighEventTimeUtc < item.StartUtc
                || item.HighEventTimeUtc > item.EndUtc
                || item.LowEventTimeUtc < item.StartUtc
                || item.LowEventTimeUtc > item.EndUtc
                || string.IsNullOrWhiteSpace(item.HighSourceEventIdentity)
                || string.IsNullOrWhiteSpace(item.LowSourceEventIdentity)
                || !item.Symbol.Equals(input.Symbol, StringComparison.OrdinalIgnoreCase)
                || input.CurrentSessionId.Equals(item.SessionId, StringComparison.Ordinal)))
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Invalid,
                contiguous,
                uniform,
                sessionIds,
                "A required session lacks valid complete-session structural evidence.");
        }

        if (!uniform)
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Invalid,
                contiguous,
                false,
                sessionIds,
                "Historical provider, feed, and source provenance must be uniform across the approved horizon.");
        }

        if (!contiguous
            || !sessions[^1].SessionId.Equals(input.ImmediatePriorSessionId, StringComparison.Ordinal))
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Insufficient,
                false,
                uniform,
                sessionIds,
                "The required sessions must be contiguous and the newest must be the immediate prior session.");
        }

        if (sessions.Any(item => item.EvidenceKnownAtUtc > input.AssessmentAsOfUtc))
        {
            return new MultiSessionStructuralCompleteness(
                StructuralAssessmentCompleteness.Insufficient,
                contiguous,
                uniform,
                sessionIds,
                "The complete three-session horizon is not known at AssessmentAsOfUtc.");
        }

        return new MultiSessionStructuralCompleteness(
            StructuralAssessmentCompleteness.Available,
            true,
            true,
            sessionIds,
            "The complete contiguous three-session horizon is usable at AssessmentAsOfUtc.");
    }

    private static bool HasUniformProvenance(IReadOnlyList<StructuralSessionSummary> sessions) =>
        sessions.Count > 0
        && sessions.All(item =>
            !string.IsNullOrWhiteSpace(item.HistoricalProvider)
            && !string.IsNullOrWhiteSpace(item.HistoricalFeed)
            && !string.IsNullOrWhiteSpace(item.HistoricalSourceKind))
        && sessions.Select(item => (
                item.HistoricalProvider,
                item.HistoricalFeed,
                item.HistoricalSourceKind))
            .Distinct()
            .Count() == 1;

    private static bool IsContiguous(IReadOnlyList<StructuralSessionSummary> sessions)
    {
        if (sessions.Count == 0)
        {
            return false;
        }

        for (var index = 1; index < sessions.Count; index++)
        {
            var earlier = sessions[index - 1];
            var later = sessions[index];
            if (!string.Equals(
                    later.PreviousSessionId,
                    earlier.SessionId,
                    StringComparison.Ordinal)
                || later.StartUtc <= earlier.StartUtc
                || earlier.EndUtc >= later.EndUtc
                || later.StartUtc <= earlier.EndUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static List<StructuralObservation> CreateSessionExtrema(
        IReadOnlyList<StructuralSessionSummary> sessions,
        DateTimeOffset assessmentAsOfUtc,
        string configurationVersion,
        MultiSessionStructureConfiguration configuration)
    {
        var observations = new List<StructuralObservation>();
        foreach (var session in sessions)
        {
            observations.Add(CreateExtremeObservation(
                session,
                StructuralRole.Resistance,
                StructuralObservationKind.CompleteSessionHigh,
                session.High!.Value,
                session.HighEventTimeUtc!.Value,
                session.HighSourceEventIdentity!,
                configurationVersion,
                configuration));
            observations.Add(CreateExtremeObservation(
                session,
                StructuralRole.Support,
                StructuralObservationKind.CompleteSessionLow,
                session.Low!.Value,
                session.LowEventTimeUtc!.Value,
                session.LowSourceEventIdentity!,
                configurationVersion,
                configuration));
        }

        return observations
            .Where(item => item.EvidenceKnownAtUtc <= assessmentAsOfUtc)
            .ToList();
    }

    private static StructuralObservation CreateExtremeObservation(
        StructuralSessionSummary session,
        StructuralRole role,
        StructuralObservationKind kind,
        decimal price,
        DateTimeOffset eventTimeUtc,
        string sourceEventIdentity,
        string configurationVersion,
        MultiSessionStructureConfiguration configuration)
    {
        var evidenceKnownAtUtc = session.EvidenceKnownAtUtc!.Value;
        var id = DeterministicId(new
        {
            DefinitionVersion,
            configurationVersion,
            Configuration = MaterialConfiguration(configuration),
            session.Symbol,
            session.SessionId,
            Role = role,
            Kind = kind,
            Price = price,
            SourceEventIdentity = sourceEventIdentity,
            EventTimeUtc = eventTimeUtc,
            EvidenceKnownAtUtc = evidenceKnownAtUtc,
            session.HistoricalProvider,
            session.HistoricalFeed,
            session.HistoricalSourceKind
        });
        return new StructuralObservation(
            id,
            session.Symbol,
            session.SessionId,
            role,
            kind,
            StructuralEligibilityReason.CompleteSessionExtreme,
            price,
            null,
            sourceEventIdentity,
            null,
            eventTimeUtc,
            null,
            evidenceKnownAtUtc,
            session.HistoricalProvider,
            session.HistoricalFeed,
            session.HistoricalSourceKind,
            null,
            null);
    }

    private static List<StructuralObservation> CreateRecurringPivotObservations(
        IReadOnlyList<StructuralSessionSummary> sessions,
        DateTimeOffset assessmentAsOfUtc,
        string configurationVersion,
        MultiSessionStructureConfiguration configuration)
    {
        var confirmed = new List<StructuralObservation>();
        foreach (var session in sessions)
        {
            foreach (var pivot in session.ConfirmablePivots
                         .Where(item =>
                             item.ConfirmedAtEventTimeUtc is { } confirmation
                             && item.ConfirmationEventIdentity is not null
                             && confirmation >= item.CandidateEventTimeUtc
                             && confirmation <= session.EndUtc
                             && confirmation <= assessmentAsOfUtc
                             && item.CandidateEventTimeUtc >= session.StartUtc
                             && item.Price > 0
                             && item.Symbol.Equals(session.Symbol, StringComparison.OrdinalIgnoreCase)
                             && item.SessionId.Equals(session.SessionId, StringComparison.Ordinal)
                             && !string.IsNullOrWhiteSpace(item.OriginEventIdentity))
                         .OrderBy(item => item.CandidateEventTimeUtc)
                         .ThenBy(item => item.SourceSwingId))
            {
                confirmed.Add(CreatePivotObservation(
                    session,
                    pivot,
                    configurationVersion,
                    configuration));
            }
        }

        var eligibleIds = new HashSet<Guid>();
        foreach (var role in new[] { StructuralRole.Support, StructuralRole.Resistance })
        {
            foreach (var cluster in CompleteLinkClusters(
                         confirmed.Where(item => item.Role == role),
                         configuration.ZoneMergeDistancePercent))
            {
                if (cluster.Select(item => item.SourceSessionId).Distinct().Count()
                    >= configuration.MinimumDistinctSessionsForRecurringZone)
                {
                    foreach (var observation in cluster)
                    {
                        eligibleIds.Add(observation.Id);
                    }
                }
            }
        }

        return confirmed.Where(item => eligibleIds.Contains(item.Id)).ToList();
    }

    private static StructuralObservation CreatePivotObservation(
        StructuralSessionSummary session,
        StructuralPivotCandidate pivot,
        string configurationVersion,
        MultiSessionStructureConfiguration configuration)
    {
        var kind = pivot.Role == StructuralRole.Support
            ? StructuralObservationKind.RecurringConfirmedSwingLow
            : StructuralObservationKind.RecurringConfirmedSwingHigh;
        var confirmationTime = pivot.ConfirmedAtEventTimeUtc!.Value;
        var id = DeterministicId(new
        {
            DefinitionVersion,
            configurationVersion,
            Configuration = MaterialConfiguration(configuration),
            session.Symbol,
            session.SessionId,
            pivot.Role,
            pivot.Price,
            pivot.SourceSwingId,
            pivot.OriginEventIdentity,
            pivot.ConfirmationEventIdentity,
            pivot.CandidateEventTimeUtc,
            pivot.ConfirmedAtEventTimeUtc,
            pivot.FeatureSetVersion,
            pivot.AlgorithmVersion,
            session.HistoricalProvider,
            session.HistoricalFeed,
            session.HistoricalSourceKind
        });
        return new StructuralObservation(
            id,
            session.Symbol,
            session.SessionId,
            pivot.Role,
            kind,
            StructuralEligibilityReason.RecurringConfirmedSwingAcrossDistinctSessions,
            pivot.Price,
            pivot.SourceSwingId,
            pivot.OriginEventIdentity,
            pivot.ConfirmationEventIdentity,
            pivot.CandidateEventTimeUtc,
            confirmationTime,
            confirmationTime,
            session.HistoricalProvider,
            session.HistoricalFeed,
            session.HistoricalSourceKind,
            pivot.FeatureSetVersion,
            pivot.AlgorithmVersion);
    }

    private static IReadOnlyList<StructuralZone> CreateZones(
        IReadOnlyList<StructuralObservation> observations,
        StructuralRole role,
        string configurationVersion,
        MultiSessionStructureConfiguration configuration) =>
        CompleteLinkClusters(
                observations.Where(item => item.Role == role),
                configuration.ZoneMergeDistancePercent)
            .Select(cluster => CreateZone(
                cluster,
                role,
                configurationVersion,
                configuration))
            .OrderBy(item => item.LowerBound)
            .ThenBy(item => item.Id)
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<StructuralObservation>> CompleteLinkClusters(
        IEnumerable<StructuralObservation> observations,
        decimal maximumSpanPercent)
    {
        var ordered = observations
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.Price)
            .ThenBy(item => item.EvidenceKnownAtUtc)
            .ThenBy(item => item.SourceSessionId, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToArray();
        var clusters = new List<IReadOnlyList<StructuralObservation>>();
        var current = new List<StructuralObservation>();
        foreach (var observation in ordered)
        {
            if (current.Count == 0
                || IsWithinPercentageDistance(
                    current[0].Price,
                    observation.Price,
                    maximumSpanPercent))
            {
                current.Add(observation);
                continue;
            }

            clusters.Add(current);
            current = [observation];
        }

        if (current.Count > 0)
        {
            clusters.Add(current);
        }

        return clusters;
    }

    private static StructuralZone CreateZone(
        IReadOnlyList<StructuralObservation> observations,
        StructuralRole role,
        string configurationVersion,
        MultiSessionStructureConfiguration configuration)
    {
        var ordered = observations
            .OrderBy(item => item.EvidenceKnownAtUtc)
            .ThenBy(item => item.Price)
            .ThenBy(item => item.Id)
            .ToArray();
        var lower = ordered.Min(item => item.Price);
        var upper = ordered.Max(item => item.Price);
        var representatives = ordered
            .GroupBy(item => item.SourceSessionId, StringComparer.Ordinal)
            .OrderBy(group => group.Min(item => item.EvidenceKnownAtUtc))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderBy(item => item.EvidenceKnownAtUtc)
                .ThenBy(item => item.Id)
                .Take(configuration.MaximumRepresentativesPerSessionPerZone))
            .ToArray();
        var material = new
        {
            DefinitionVersion,
            configurationVersion,
            configuration.HistoricalSessionCount,
            configuration.MinimumDistinctSessionsForRecurringZone,
            configuration.ZoneMergeDistancePercent,
            Role = role,
            LowerBound = lower,
            UpperBound = upper,
            Observations = ordered.Select(item => new
            {
                item.Id,
                item.SourceSessionId,
                item.Price,
                item.Kind,
                item.SourceSwingId,
                item.OriginEventIdentity,
                item.ConfirmationEventIdentity,
                item.CandidateEventTimeUtc,
                item.ConfirmedAtEventTimeUtc,
                item.EvidenceKnownAtUtc
            }).ToArray()
        };
        return new StructuralZone(
            DeterministicId(material),
            role,
            lower,
            upper,
            ordered.Max(item => item.EvidenceKnownAtUtc),
            ordered.Select(item => item.SourceSessionId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            representatives,
            ordered.Select(item => item.EligibilityReason)
                .Distinct()
                .OrderBy(item => item)
                .ToArray(),
            ordered.Select(item => item.SourceSessionId).Distinct(StringComparer.Ordinal).Count(),
            ordered.Length,
            ordered.Length - representatives.Length,
            DefinitionVersion,
            configurationVersion);
    }

    private static IReadOnlyList<StructuralZone> ProjectZones(
        IReadOnlyList<StructuralZone> zones,
        int maximumZones,
        StructuralRole role)
    {
        var ordered = role == StructuralRole.Support
            ? zones.OrderByDescending(item => item.UpperBound)
                .ThenByDescending(item => item.LowerBound)
                .ThenBy(item => item.Id)
            : zones.OrderBy(item => item.LowerBound)
                .ThenBy(item => item.UpperBound)
                .ThenBy(item => item.Id);
        return ordered.Take(maximumZones).ToArray();
    }

    private static MultiSessionStructuralTrend EvaluateTrend(
        IReadOnlyList<StructuralSessionSummary> sessions,
        decimal equalityTolerancePercent)
    {
        var intervals = new[]
        {
            EvaluateInterval(sessions[0], sessions[1], equalityTolerancePercent),
            EvaluateInterval(sessions[1], sessions[2], equalityTolerancePercent)
        };
        var state = intervals.Any(item => item.State == StructuralIntervalTrend.Conflicting)
            ? MultiSessionStructuralTrendState.Conflicting
            : intervals[0].State == intervals[1].State
                ? intervals[0].State switch
                {
                    StructuralIntervalTrend.Bullish => MultiSessionStructuralTrendState.Bullish,
                    StructuralIntervalTrend.Bearish => MultiSessionStructuralTrendState.Bearish,
                    StructuralIntervalTrend.Range => MultiSessionStructuralTrendState.Range,
                    _ => MultiSessionStructuralTrendState.Conflicting
                }
                : MultiSessionStructuralTrendState.Transitioning;

        return new MultiSessionStructuralTrend(
            state,
            intervals,
            sessions.Max(item => item.EvidenceKnownAtUtc));
    }

    private static StructuralTrendInterval EvaluateInterval(
        StructuralSessionSummary earlier,
        StructuralSessionSummary later,
        decimal equalityTolerancePercent)
    {
        var highRelationship = Compare(earlier.High!.Value, later.High!.Value, equalityTolerancePercent);
        var lowRelationship = Compare(earlier.Low!.Value, later.Low!.Value, equalityTolerancePercent);
        var state = (highRelationship, lowRelationship) switch
        {
            (> 0, > 0) => StructuralIntervalTrend.Bullish,
            (< 0, < 0) => StructuralIntervalTrend.Bearish,
            (0, 0) => StructuralIntervalTrend.Range,
            _ => StructuralIntervalTrend.Conflicting
        };
        return new StructuralTrendInterval(
            earlier.SessionId,
            later.SessionId,
            state,
            earlier.High!.Value,
            later.High!.Value,
            earlier.Low!.Value,
            later.Low!.Value);
    }

    private static int Compare(decimal earlier, decimal later, decimal equalityTolerancePercent)
    {
        if (IsWithinPercentageDistance(earlier, later, equalityTolerancePercent))
        {
            return 0;
        }

        return later.CompareTo(earlier);
    }

    private static MultiSessionStructuralAssessment CreateUnavailableAssessment(
        StructuralAssessmentInput input,
        IReadOnlyList<StructuralSessionSummary> sessions,
        ScannerConfiguration scannerConfiguration,
        MultiSessionStructuralCompleteness completeness)
    {
        var trend = new MultiSessionStructuralTrend(
            MultiSessionStructuralTrendState.Insufficient,
            [],
            null);
        var id = DeterministicId(new
        {
            input.Symbol,
            input.CurrentSessionId,
            input.ImmediatePriorSessionId,
            input.AssessmentAsOfUtc,
            DefinitionVersion,
            ConfigurationVersion = scannerConfiguration.Version,
            Configuration = MaterialConfiguration(scannerConfiguration.MultiSessionStructure),
            HistoricalSessions = sessions.Select(SessionFingerprint).ToArray(),
            Completeness = completeness,
            Trend = trend
        });
        return new MultiSessionStructuralAssessment(
            id,
            input.Symbol,
            input.CurrentSessionId,
            input.ImmediatePriorSessionId,
            input.AssessmentAsOfUtc,
            null,
            DefinitionVersion,
            scannerConfiguration.Version,
            sessions.Select(item => item.SessionId).ToArray(),
            completeness,
            trend,
            [],
            [],
            0,
            0);
    }

    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is null || first >= second ? first : second;
    }

    private static object MaterialConfiguration(MultiSessionStructureConfiguration configuration) =>
        new
        {
            configuration.HistoricalSessionCount,
            configuration.MinimumDistinctSessionsForRecurringZone,
            configuration.ZoneMergeDistancePercent,
            configuration.StructuralEqualityTolerancePercent
        };

    private static string SessionFingerprint(StructuralSessionSummary session)
    {
        var canonicalPivots = session.ConfirmablePivots
            .Select(item => JsonSerializer.Serialize(item, JsonOptions))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            session.Symbol,
            session.SessionId,
            session.PreviousSessionId,
            session.StartUtc,
            session.EndUtc,
            session.Completeness,
            session.DeclaresCompleteCoverage,
            session.HasKnownGaps,
            session.High,
            session.Low,
            session.HighSourceEventIdentity,
            session.LowSourceEventIdentity,
            session.HighEventTimeUtc,
            session.LowEventTimeUtc,
            session.EvidenceKnownAtUtc,
            session.HistoricalProvider,
            session.HistoricalFeed,
            session.HistoricalSourceKind,
            ConfirmablePivots = canonicalPivots
        }, JsonOptions);
    }

    private static Guid DeterministicId<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return new Guid(hash.AsSpan(0, 16));
    }
}
