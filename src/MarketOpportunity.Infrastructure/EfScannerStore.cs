using System.Text.Json;
using MarketOpportunity.Application;
using MarketOpportunity.Domain;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.Infrastructure;

public sealed class EfScannerStore(MarketOpportunityDbContext dbContext) : IScannerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PersistRunAsync(
        ScannerConfiguration configuration,
        IReadOnlyCollection<MarketEvent> marketEvents,
        FeatureSnapshot featureSnapshot,
        Opportunity opportunity,
        CancellationToken cancellationToken)
    {
        var configurationJson = JsonSerializer.Serialize(configuration, JsonOptions);
        var existingConfiguration = await dbContext.ConfigurationSnapshots
            .SingleOrDefaultAsync(item => item.Version == configuration.Version, cancellationToken);
        if (existingConfiguration is null)
        {
            dbContext.ConfigurationSnapshots.Add(new ConfigurationSnapshotEntity
            {
                Version = configuration.Version,
                CreatedAtUtc = featureSnapshot.AsOfUtc,
                ConfigurationJson = configurationJson
            });
        }
        else if (!string.Equals(existingConfiguration.ConfigurationJson, configurationJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration version '{configuration.Version}' is immutable and already has different values.");
        }

        var existingIdentities = await dbContext.MarketEvents
            .Where(item => item.Symbol == featureSnapshot.Symbol
                && item.SessionId == featureSnapshot.SessionId)
            .Select(item => item.EventIdentity)
            .ToListAsync(cancellationToken);
        var existingIdentitySet = existingIdentities.ToHashSet(StringComparer.Ordinal);
        foreach (var marketEvent in marketEvents)
        {
            var eventIdentity = MarketEventIdentity.Create(marketEvent);
            if (!existingIdentitySet.Add(eventIdentity))
            {
                continue;
            }

            dbContext.MarketEvents.Add(ToEntity(marketEvent, eventIdentity));
        }

        if (!await dbContext.FeatureSnapshots.AnyAsync(
                item => item.Id == featureSnapshot.Id,
                cancellationToken))
        {
            dbContext.FeatureSnapshots.Add(new FeatureSnapshotEntity
            {
                Id = featureSnapshot.Id,
                Symbol = featureSnapshot.Symbol,
                SessionId = featureSnapshot.SessionId,
                AsOfUtc = featureSnapshot.AsOfUtc,
                FeatureSetVersion = featureSnapshot.FeatureSetVersion,
                SnapshotJson = JsonSerializer.Serialize(featureSnapshot, JsonOptions)
            });
        }

        if (!await dbContext.Opportunities.AnyAsync(
                item => item.Id == opportunity.Id,
                cancellationToken))
        {
            var opportunityEntity = new OpportunityEntity
            {
                Id = opportunity.Id,
                Symbol = opportunity.Symbol,
                SessionId = opportunity.SessionId,
                AsOfUtc = opportunity.AsOfUtc,
                ValidUntilUtc = opportunity.ValidUntilUtc,
                SetupType = opportunity.SetupType,
                Bias = opportunity.Bias.ToString(),
                Classification = opportunity.Classification.ToString(),
                QualityScore = opportunity.QualityScore,
                ReferencePrice = opportunity.ReferencePrice,
                FeatureSetVersion = opportunity.FeatureSetVersion,
                SetupDefinitionVersion = opportunity.SetupDefinitionVersion,
                ConfigurationVersion = opportunity.ConfigurationVersion,
                DataProvider = opportunity.MarketData.Provider,
                DataFeed = opportunity.MarketData.Feed,
                DataCoverage = opportunity.MarketData.Coverage.ToString(),
                IsRealTimeData = opportunity.MarketData.IsRealTime,
                SupportsTrades = opportunity.MarketData.SupportsTrades,
                SupportsQuotes = opportunity.MarketData.SupportsQuotes
            };
            foreach (var evidence in opportunity.SupportingEvidence)
            {
                opportunityEntity.Evidence.Add(
                    ToEvidenceEntity(opportunity.Id, "Supporting", evidence.Code, evidence.Description));
            }

            foreach (var evidence in opportunity.ContradictoryEvidence)
            {
                opportunityEntity.Evidence.Add(
                    ToEvidenceEntity(opportunity.Id, "Contradictory", evidence.Code, evidence.Description));
            }

            foreach (var veto in opportunity.ActiveVetoes)
            {
                opportunityEntity.Evidence.Add(
                    ToEvidenceEntity(opportunity.Id, "Veto", veto, VetoDescription(veto)));
            }

            opportunityEntity.Evidence.Add(
                ToEvidenceEntity(opportunity.Id, "Invalidation", "InvalidationCondition", opportunity.InvalidationCondition));
            dbContext.Opportunities.Add(opportunityEntity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MarketEventEntity ToEntity(MarketEvent marketEvent, string identity) =>
        marketEvent switch
        {
            TradeEvent trade => new MarketEventEntity
            {
                Id = MarketEventIdentity.ToGuid(identity),
                Symbol = trade.Symbol,
                SessionId = trade.SessionId,
                EventType = "Trade",
                EventTimeUtc = trade.EventTimeUtc,
                ReceivedTimeUtc = trade.ReceivedTimeUtc,
                Provider = trade.Provider,
                SequenceId = trade.SequenceId,
                EventIdentity = identity,
                TradePrice = trade.Price,
                TradeSize = trade.Size
            },
            QuoteEvent quote => new MarketEventEntity
            {
                Id = MarketEventIdentity.ToGuid(identity),
                Symbol = quote.Symbol,
                SessionId = quote.SessionId,
                EventType = "Quote",
                EventTimeUtc = quote.EventTimeUtc,
                ReceivedTimeUtc = quote.ReceivedTimeUtc,
                Provider = quote.Provider,
                SequenceId = quote.SequenceId,
                EventIdentity = identity,
                BidPrice = quote.BidPrice,
                AskPrice = quote.AskPrice,
                BidSize = quote.BidSize,
                AskSize = quote.AskSize
            },
            _ => throw new NotSupportedException($"Unsupported normalized market event type: {marketEvent.GetType().Name}.")
        };

    private static OpportunityEvidenceEntity ToEvidenceEntity(
        Guid opportunityId,
        string kind,
        string code,
        string description) =>
        new()
        {
            Id = MarketEventIdentity.ToGuid(
                $"{opportunityId:N}\u001f{kind}\u001f{code}\u001f{description}"),
            OpportunityId = opportunityId,
            Kind = kind,
            Code = code,
            Description = description
        };

    private static string VetoDescription(string veto) => veto switch
    {
        "StaleQuote" => "Quote age exceeds the configured maximum or has invalid event-time ordering.",
        "InvalidQuoteOrPrice" => "The quote is invalid or the reference trade price is not positive.",
        "ExcessiveSpread" => "Spread exceeds the configured maximum or is unavailable.",
        "InsufficientRequiredFeatureData" => "Required features are missing or the bounded state is incomplete.",
        "NoDefensibleInvalidation" => "The current feature snapshot does not support a defensible directional invalidation level.",
        _ => throw new ArgumentOutOfRangeException(nameof(veto), veto, "Unknown veto code.")
    };
}
