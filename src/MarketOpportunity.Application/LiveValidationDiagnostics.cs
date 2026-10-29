using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using MarketOpportunity.Domain;

namespace MarketOpportunity.Application;

public sealed record ProviderEventMetadata(
    string? BidExchange,
    string? AskExchange,
    IReadOnlyList<string> Conditions,
    string? Tape,
    bool Incomplete = false,
    bool ConditionsSupplied = false);

public sealed record EventDiagnosticTiming(
    Guid ConnectionId,
    DateTimeOffset MessageReceivedTimeUtc,
    DateTimeOffset ParsedTimeUtc,
    long CreatedTimestamp,
    ProviderEventMetadata Metadata,
    long? MessageReceivedTimestamp = null);

public sealed record DiagnosticTransition(
    Guid ConnectionId, string State, DateTimeOffset Utc, string? Reason);

public sealed record DiagnosticSample(
    Guid ConnectionId,
    string Symbol,
    string EventType,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedTimeUtc,
    DateTimeOffset MessageReceivedTimeUtc,
    DateTimeOffset ParsedTimeUtc,
    string EventIdentity,
    ProviderEventMetadata Metadata);

public sealed record MetadataSummary(
    Guid ConnectionId,
    string Symbol,
    string EventType,
    ProviderEventMetadata Metadata,
    long Events,
    long ZeroAskQuotes,
    DiagnosticSample? FirstZeroAsk,
    DiagnosticSample? LastZeroAsk);

public sealed record TimingSummary(
    long Count,
    double? MinimumMs,
    double? MaximumMs,
    double? MedianLowerBoundMs,
    double? MedianUpperBoundMs,
    double? P95LowerBoundMs,
    double? P95UpperBoundMs,
    long CountAbove1000Ms = 0,
    long CountAbove5000Ms = 0,
    long CountAbove10000Ms = 0,
    long CountAbove15000Ms = 0);

public enum ShutdownTrigger
{
    Unknown,
    DurationElapsed,
    OperatorCancellation,
    ApplicationFailure,
    GracefulDrainTimeout
}

public sealed record ShutdownTriggerEvent(string Trigger, DateTimeOffset Utc);

public sealed record LiveValidationSnapshot(
    DateTimeOffset StartedTimeUtc,
    DateTimeOffset CapturedTimeUtc,
    double RuntimeSeconds,
    string? SessionId,
    string? Feed,
    IReadOnlyList<string> RequestedSymbols,
    IReadOnlyList<string> ConfirmedTradeSymbols,
    IReadOnlyList<string> ConfirmedQuoteSymbols,
    long ConnectionAttempts,
    long ConnectionsOpened,
    long ReconnectAttempts,
    long MessagesReceived,
    long MarketDataMessages,
    long CanonicalEventsProduced,
    long TradesProduced,
    long QuotesProduced,
    long ParserFailures,
    long AcceptedEvents,
    long DequeuedEvents,
    long DuplicateEvents,
    long EventsAnalyzed,
    long ProcessingFailures,
    long PersistenceAcknowledgedEvents,
    long PersistenceBatches,
    long PersistenceFailures,
    long PersistenceFailedEvents,
    long ChannelBackpressureWaits,
    long CancelledWrites,
    int ChannelDepth,
    int MaximumSampledChannelDepth,
    double LongestChannelWaitMs,
    long ZeroAskQuotes,
    long UnattributedMetadataEvents,
    long DroppedTransitions,
    string ShutdownDrainStatus,
    string? ShutdownDrainFailure,
    long RemainingEvents,
    IReadOnlyDictionary<string, TimingSummary> Timings,
    IReadOnlyList<DiagnosticTransition> Transitions,
    IReadOnlyList<MetadataSummary> ProviderMetadata,
    int? ConfiguredDurationSeconds,
    string ShutdownTrigger,
    DateTimeOffset? ShutdownRequestedUtc,
    IReadOnlyList<ShutdownTriggerEvent> ShutdownTriggerEvents,
    string? ApplicationLogPath);

// Sidecar state deliberately never participates in MarketEvent equality, serialization, or identity.
public sealed class LiveValidationDiagnostics : IDisposable
{
    public const string MeterName = "MarketOpportunity.LiveValidation";
    private const int MaximumGroups = 256;
    private const int MaximumTransitions = 256;
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly Action<string>? _log;
    private readonly ConditionalWeakTable<MarketEvent, EventDiagnosticTiming> _events = new();
    private readonly Meter _meter = new(MeterName);
    private readonly Dictionary<string, Counter<long>> _counters = [];
    private readonly Dictionary<string, (Distribution Distribution, Histogram<double> Instrument)> _timings = [];
    private readonly Dictionary<string, MetadataSummary> _metadata = new(StringComparer.Ordinal);
    private readonly List<DiagnosticTransition> _transitions = [];
    private readonly Dictionary<string, long> _counts = [];
    private readonly Dictionary<string, long> _lastAnomaly = [];
    private readonly DateTimeOffset _startedUtc;
    private readonly long _started;
    private string? _session;
    private string? _feed;
    private string[] _requestedSymbols = [];
    private string[] _tradeSymbols = [];
    private string[] _quoteSymbols = [];
    private int _depth;
    private int _maxDepth;
    private double _longestWait;
    private string _shutdownDrainStatus = "NotStarted";
    private const int MaximumShutdownEvents = 16;
    private readonly List<ShutdownTriggerEvent> _shutdownEvents = [];
    private ShutdownTrigger? _shutdownTrigger;
    private DateTimeOffset? _shutdownRequestedUtc;
    private int? _configuredDurationSeconds;
    private string? _applicationLogPath;
    private string? _shutdownDrainFailure;
    private readonly ObservableGauge<int> _depthGauge;

    public LiveValidationDiagnostics(TimeProvider clock, Action<string>? log = null)
    {
        _clock = clock;
        _log = log;
        _startedUtc = clock.GetUtcNow();
        _started = clock.GetTimestamp();
        foreach (var name in new[]
        {
            "connection_attempts", "connections_opened", "reconnect_attempts", "messages_received", "market_data_messages",
            "canonical_events_produced", "trades_produced", "quotes_produced", "parser_failures",
            "accepted_events", "dequeued_events", "duplicate_events", "events_analyzed",
            "processing_failures",
            "persistence_acknowledged_events", "persistence_batches", "persistence_failures",
            "persistence_failed_events",
            "channel_backpressure_waits", "cancelled_writes", "zero_ask_quotes",
            "unattributed_metadata_events", "dropped_transitions"
        })
        {
            _counters.Add(name, _meter.CreateCounter<long>("moe.live." + name));
        }

        foreach (var name in new[]
        {
            "provider_to_message", "message_to_creation", "creation_to_analysis",
            "analysis_to_persistence", "persistence_batch", "channel_write_wait"
        })
        {
            _timings.Add(name, (new Distribution(), _meter.CreateHistogram<double>(
                "moe.live." + name, "ms")));
        }

        _depthGauge = _meter.CreateObservableGauge("moe.live.channel_depth", () =>
        {
            lock (_sync) { return _depth; }
        });
    }

    public void Configure(string session, string feed, IReadOnlyList<string> symbols)
    {
        lock (_sync)
        {
            _session = session;
            _feed = feed;
            _requestedSymbols = symbols.Take(MaximumGroups).Select(SafeText).ToArray();
        }
    }

    public Guid Connecting()
    {
        var id = Guid.NewGuid();
        lock (_sync) { Count("connection_attempts"); }
        Transition(id, "Connecting");
        return id;
    }

    public void Connected(Guid id)
    {
        lock (_sync) { Count("connections_opened"); }
        Transition(id, "Connected");
    }

    public void Transition(Guid id, string state, string? reason = null)
    {
        lock (_sync)
        {
            var transition = new DiagnosticTransition(id, state, _clock.GetUtcNow(), reason);
            if (_transitions.Count < MaximumTransitions) { _transitions.Add(transition); }
            else { Count("dropped_transitions"); }
            _log?.Invoke(System.Text.Json.JsonSerializer.Serialize(transition));
        }
    }

    public void Subscription(IReadOnlyList<string> trades, IReadOnlyList<string> quotes)
    {
        lock (_sync)
        {
            _tradeSymbols = trades.Take(MaximumGroups).Select(SafeText).ToArray();
            _quoteSymbols = quotes.Take(MaximumGroups).Select(SafeText).ToArray();
        }
    }

    public void MessageReceived()
    {
        lock (_sync) { Count("messages_received"); }
    }

    public void MarketDataMessage()
    {
        lock (_sync) { Count("market_data_messages"); }
    }

    public void ParserFailed(Guid connection, Exception error)
    {
        lock (_sync) { Count("parser_failures"); }
        Transition(connection, "ParserFailure", error.GetType().Name);
    }

    public void Produced(
        MarketEvent marketEvent,
        Guid connection,
        DateTimeOffset messageReceivedUtc,
        ProviderEventMetadata metadata,
        long? messageReceivedTimestamp = null)
    {
        var parsedUtc = _clock.GetUtcNow();
        var timing = new EventDiagnosticTiming(
            connection, messageReceivedUtc, parsedUtc, _clock.GetTimestamp(), metadata, messageReceivedTimestamp);
        _events.Add(marketEvent, timing);
        lock (_sync)
        {
            Count("canonical_events_produced");
            Count(marketEvent is QuoteEvent ? "quotes_produced" : "trades_produced");
            var providerLag = (messageReceivedUtc - marketEvent.EventTimeUtc).TotalMilliseconds;
            var parsingLag = messageReceivedTimestamp.HasValue
                ? _clock.GetElapsedTime(messageReceivedTimestamp.Value, timing.CreatedTimestamp).TotalMilliseconds
                : (parsedUtc - messageReceivedUtc).TotalMilliseconds;
            Observe("provider_to_message", providerLag);
            Observe("message_to_creation", parsingLag);
            var zeroAsk = marketEvent is QuoteEvent { AskPrice: 0 };
            if (zeroAsk) { Count("zero_ask_quotes"); }
            var key = System.Text.Json.JsonSerializer.Serialize(new
            {
                connection,
                marketEvent.Symbol,
                Type = marketEvent is QuoteEvent ? "Quote" : "Trade",
                metadata
            });
            if (_metadata.TryGetValue(key, out var group))
            {
                var sample = zeroAsk ? Sample(marketEvent, timing) : null;
                _metadata[key] = group with
                {
                    Events = group.Events + 1,
                    ZeroAskQuotes = group.ZeroAskQuotes + (zeroAsk ? 1 : 0),
                    FirstZeroAsk = group.FirstZeroAsk ?? sample,
                    LastZeroAsk = sample ?? group.LastZeroAsk
                };
            }
            else if (_metadata.Count < MaximumGroups)
            {
                var sample = zeroAsk ? Sample(marketEvent, timing) : null;
                _metadata.Add(key, new MetadataSummary(
                    connection, SafeText(marketEvent.Symbol), marketEvent is QuoteEvent ? "Quote" : "Trade",
                    metadata, 1, zeroAsk ? 1 : 0, sample, sample));
            }
            else { Count("unattributed_metadata_events"); }

            if (providerLag > 5000 || parsingLag > 1000)
            {
                Anomaly("Timing", () => new
                {
                    Sample = Sample(marketEvent, timing),
                    ProviderToMessageMs = providerLag,
                    MessageToCreationMs = parsingLag
                });
            }
        }
    }

    public bool TryGetTiming(MarketEvent marketEvent, out EventDiagnosticTiming? timing) =>
        _events.TryGetValue(marketEvent, out timing);

    public void Accepted(int depth)
    {
        lock (_sync) { Count("accepted_events"); Depth(depth); }
    }

    public void Dequeued(int depth, bool duplicate)
    {
        lock (_sync)
        {
            Count("dequeued_events");
            if (duplicate) { Count("duplicate_events"); }
            Depth(depth);
        }
    }

    public void WriteCompleted(long start, bool waited, bool cancelled)
    {
        var elapsed = _clock.GetElapsedTime(start).TotalMilliseconds;
        lock (_sync)
        {
            if (waited)
            {
                Count("channel_backpressure_waits");
                Observe("channel_write_wait", elapsed);
                _longestWait = Math.Max(_longestWait, elapsed);
                if (elapsed > 1000) { Anomaly("Backpressure", () => new { WaitMs = elapsed, Depth = _depth }); }
            }
            if (cancelled) { Count("cancelled_writes"); }
        }
    }

    public long AnalysisStarted(IReadOnlyCollection<MarketEvent> events)
    {
        var start = _clock.GetTimestamp();
        lock (_sync)
        {
            Count("events_analyzed", events.Count);
            foreach (var marketEvent in events)
            {
                if (_events.TryGetValue(marketEvent, out var timing))
                {
                    Observe("creation_to_analysis",
                        _clock.GetElapsedTime(timing.CreatedTimestamp, start).TotalMilliseconds);
                }
            }
        }
        return start;
    }

    public void PersistenceCompleted(long analysisStart, long persistenceStart, int events)
    {
        lock (_sync)
        {
            Count("persistence_acknowledged_events", events);
            Count("persistence_batches");
            Observe("analysis_to_persistence", _clock.GetElapsedTime(analysisStart).TotalMilliseconds);
            var duration = _clock.GetElapsedTime(persistenceStart).TotalMilliseconds;
            Observe("persistence_batch", duration);
            if (duration > 1000) { Anomaly("Persistence", () => new { DurationMs = duration, Events = events }); }
        }
    }

    public void PersistenceFailed(int events)
    {
        lock (_sync)
        {
            Count("persistence_failures");
            Count("persistence_failed_events", events);
        }
    }

    public void ProcessingFailed()
    {
        lock (_sync) { Count("processing_failures"); }
    }

    public void ShutdownDrainStarted()
    {
        lock (_sync)
        {
            _shutdownDrainStatus = "InProgress";
            _shutdownDrainFailure = null;
        }
    }

    public bool ShutdownDrainCompleted()
    {
        lock (_sync)
        {
            if (Value("accepted_events") != Value("dequeued_events")
                || Value("events_analyzed")
                    != Value("accepted_events") - Value("duplicate_events")
                || Value("accepted_events") != Value("duplicate_events")
                + Value("persistence_acknowledged_events")
                || Value("persistence_failures") != 0
                || Value("processing_failures") != 0)
            {
                _shutdownDrainStatus = "Failed";
                _shutdownDrainFailure =
                    "Accepted work did not reconcile with dequeue, duplicate, analysis, and persistence accounting.";
                return false;
            }

            _shutdownDrainStatus = "Completed";
            _shutdownDrainFailure = null;
            return true;
        }
    }

    public void ShutdownDrainFailed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_sync)
        {
            _shutdownDrainStatus = "Failed";
            _shutdownDrainFailure = reason;
            RecordShutdownTriggerLocked(ShutdownTrigger.ApplicationFailure);
        }
    }

    public void ShutdownDrainTimedOut()
    {
        lock (_sync)
        {
            _shutdownDrainStatus = "TimedOut";
            _shutdownDrainFailure =
                "The graceful shutdown timeout expired before accepted work completed.";
            RecordShutdownTriggerLocked(ShutdownTrigger.GracefulDrainTimeout);
        }
    }

    public void ConfigureRun(int configuredDurationSeconds, string? applicationLogPath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(configuredDurationSeconds);
        lock (_sync)
        {
            _configuredDurationSeconds = configuredDurationSeconds;
            _applicationLogPath = applicationLogPath;
        }
    }

    // Records the cause at the moment it is observed; the trigger is never inferred from runtime.
    // A requested stop (duration or operator) is kept unless a later abnormal outcome
    // (drain timeout or failure) supersedes it; every recorded cause stays in the event history.
    public void RecordShutdownTrigger(ShutdownTrigger trigger)
    {
        lock (_sync) { RecordShutdownTriggerLocked(trigger); }
    }

    public void ShutdownRequested()
    {
        lock (_sync) { _shutdownRequestedUtc ??= _clock.GetUtcNow(); }
    }

    private void RecordShutdownTriggerLocked(ShutdownTrigger trigger)
    {
        if (trigger == ShutdownTrigger.Unknown) { return; }
        var now = _clock.GetUtcNow();
        _shutdownRequestedUtc ??= now;
        if (_shutdownEvents.Count < MaximumShutdownEvents)
        {
            _shutdownEvents.Add(new(trigger.ToString(), now));
        }
        static bool Abnormal(ShutdownTrigger? value) =>
            value is ShutdownTrigger.ApplicationFailure or ShutdownTrigger.GracefulDrainTimeout;
        if (_shutdownTrigger is null || (Abnormal(trigger) && !Abnormal(_shutdownTrigger)))
        {
            _shutdownTrigger = trigger;
        }
    }

    public void ReconnectAttempt()
    {
        lock (_sync) { Count("reconnect_attempts"); }
    }

    public LiveValidationSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new LiveValidationSnapshot(
                _startedUtc, _clock.GetUtcNow(), _clock.GetElapsedTime(_started).TotalSeconds,
                _session, _feed, _requestedSymbols.ToArray(), _tradeSymbols.ToArray(), _quoteSymbols.ToArray(),
                Value("connection_attempts"), Value("connections_opened"), Value("reconnect_attempts"), Value("messages_received"),
                Value("market_data_messages"), Value("canonical_events_produced"),
                Value("trades_produced"), Value("quotes_produced"), Value("parser_failures"),
                Value("accepted_events"), Value("dequeued_events"), Value("duplicate_events"),
                Value("events_analyzed"), Value("processing_failures"),
                Value("persistence_acknowledged_events"),
                Value("persistence_batches"), Value("persistence_failures"),
                Value("persistence_failed_events"),
                Value("channel_backpressure_waits"), Value("cancelled_writes"), _depth, _maxDepth,
                _longestWait, Value("zero_ask_quotes"), Value("unattributed_metadata_events"),
                Value("dropped_transitions"), _shutdownDrainStatus, _shutdownDrainFailure,
                Math.Max(0, Value("accepted_events") - Value("duplicate_events")
                    - Value("persistence_acknowledged_events")),
                _timings.ToDictionary(pair => pair.Key, pair => pair.Value.Distribution.Summary()),
                _transitions.ToArray(), _metadata.Values.ToArray(),
                _configuredDurationSeconds, (_shutdownTrigger ?? ShutdownTrigger.Unknown).ToString(),
                _shutdownRequestedUtc, _shutdownEvents.ToArray(), _applicationLogPath);
        }
    }

    private void Depth(int depth)
    {
        _depth = depth;
        _maxDepth = Math.Max(_maxDepth, depth);
    }

    private long Value(string name) => _counts.GetValueOrDefault(name);

    private void Count(string name, long count = 1)
    {
        _counts[name] = Value(name) + count;
        _counters[name].Add(count, new KeyValuePair<string, object?>("provider", "Alpaca"));
    }

    private void Observe(string name, double value)
    {
        var timing = _timings[name];
        timing.Distribution.Add(value);
        timing.Instrument.Record(value, new KeyValuePair<string, object?>("provider", "Alpaca"));
    }

    private void Anomaly(string category, Func<object> detail)
    {
        var now = _clock.GetUtcNow();
        var timestamp = _clock.GetTimestamp();
        if (_lastAnomaly.TryGetValue(category, out var last)
            && _clock.GetElapsedTime(last, timestamp) < TimeSpan.FromMinutes(1))
        {
            return;
        }
        _lastAnomaly[category] = timestamp;
        if (_log is not null)
        {
            _log(System.Text.Json.JsonSerializer.Serialize(new { Category = category, Utc = now, Detail = detail() }));
        }
    }

    private static DiagnosticSample Sample(MarketEvent marketEvent, EventDiagnosticTiming timing) => new(
        timing.ConnectionId, SafeText(marketEvent.Symbol), marketEvent is QuoteEvent ? "Quote" : "Trade",
        marketEvent.EventTimeUtc, marketEvent.ReceivedTimeUtc, timing.MessageReceivedTimeUtc,
        timing.ParsedTimeUtc, MarketEventIdentity.Create(marketEvent), timing.Metadata);

    public static string SafeText(string value) =>
        new(value.Take(64).Where(character => !char.IsControl(character)).ToArray());

    public void Dispose() => _meter.Dispose();

    private sealed class Distribution
    {
        private static readonly double[] PositiveBounds =
        [
            0, .01, .05, .1, .25, .5, 1, 2, 5, 10, 20, 50, 100, 200, 500,
            1000, 2000, 5000, 10000, 30000, 60000, 120000, 300000, 600000,
            1800000, 3600000
        ];
        private static readonly double[] Bounds = PositiveBounds.Where(value => value > 0)
            .Reverse().Select(value => -value).Concat(PositiveBounds).ToArray();
        private readonly long[] _buckets = new long[Bounds.Length + 1];
        private long _count;
        private long _above1000;
        private long _above5000;
        private long _above10000;
        private long _above15000;
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;

        public void Add(double value)
        {
            var index = Array.BinarySearch(Bounds, value);
            _buckets[index < 0 ? ~index : index]++;
            _count++;
            if (value > 1000) { _above1000++; }
            if (value > 5000) { _above5000++; }
            if (value > 10000) { _above10000++; }
            if (value > 15000) { _above15000++; }
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);
        }

        public TimingSummary Summary()
        {
            if (_count == 0) { return new(0, null, null, null, null, null, null); }
            var median = Quantile(.5);
            var p95 = Quantile(.95);
            return new(_count, _min, _max, median.Lower, median.Upper, p95.Lower, p95.Upper,
                _above1000, _above5000, _above10000, _above15000);
        }

        private (double Lower, double Upper) Quantile(double percentile)
        {
            var target = (long)Math.Ceiling(_count * percentile);
            long total = 0;
            for (var index = 0; index < _buckets.Length; index++)
            {
                total += _buckets[index];
                if (total >= target)
                {
                    return (index == 0 ? _min : Math.Max(_min, Bounds[index - 1]),
                        index == Bounds.Length ? _max : Math.Min(_max, Bounds[index]));
                }
            }
            throw new InvalidOperationException("Diagnostic histogram count is inconsistent.");
        }
    }
}
