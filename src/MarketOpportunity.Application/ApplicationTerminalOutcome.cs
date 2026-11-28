namespace MarketOpportunity.Application;

public enum ApplicationTerminalOutcomeCategory
{
    UnknownFailure,
    CompletedNormally,
    CancelledByRequestedShutdown,
    ValidationDurationReached,
    ApplicationFailure,
    BackgroundServiceFailure,
    ProviderTerminalFailure,
    StartupFailure,
    ConfigurationFailure,
    PersistenceTerminalFailure
}

public sealed record ApplicationTerminalOutcome(
    ApplicationTerminalOutcomeCategory Category,
    string ReasonCode,
    string Component,
    DateTimeOffset FirstObservedUtc,
    bool Expected,
    bool HostShutdownRequested,
    bool ValidationSuccessful,
    int ProcessExitCode);

public sealed class ApplicationTerminalOutcomeTracker
{
    private const string UnknownComponent = "Application";
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly Action<int> _setExitCode;
    private ApplicationTerminalOutcome? _outcome;

    public ApplicationTerminalOutcomeTracker(
        TimeProvider clock,
        Action<int>? setExitCode = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _setExitCode = setExitCode ?? (exitCode => Environment.ExitCode = exitCode);
    }

    public ApplicationTerminalOutcome RecordFailure(
        ApplicationTerminalOutcomeCategory category,
        string reasonCode,
        string component)
    {
        ValidateFailure(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        component = string.IsNullOrWhiteSpace(component) ? UnknownComponent : component;
        lock (_sync)
        {
            if (_outcome is { Expected: false })
            {
                return _outcome;
            }

            _outcome = new ApplicationTerminalOutcome(
                category,
                Limit(reasonCode, 80),
                Limit(component, 80),
                _clock.GetUtcNow(),
                false,
                true,
                false,
                1);
            _setExitCode(1);
            return _outcome;
        }
    }

    public ApplicationTerminalOutcome RecordExpected(
        ApplicationTerminalOutcomeCategory category,
        string reasonCode,
        string component,
        bool validationSuccessful,
        bool hostShutdownRequested = true)
    {
        if (IsFailure(category))
        {
            throw new ArgumentException(
                $"'{category}' is not an expected terminal outcome.",
                nameof(category));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        component = string.IsNullOrWhiteSpace(component) ? UnknownComponent : component;
        lock (_sync)
        {
            if (_outcome is not null)
            {
                return _outcome;
            }

            _outcome = new ApplicationTerminalOutcome(
                category,
                Limit(reasonCode, 80),
                Limit(component, 80),
                _clock.GetUtcNow(),
                true,
                hostShutdownRequested,
                validationSuccessful,
                0);
            _setExitCode(0);
            return _outcome;
        }
    }

    public ApplicationTerminalOutcome? Read()
    {
        lock (_sync)
        {
            return _outcome;
        }
    }

    private static void ValidateFailure(ApplicationTerminalOutcomeCategory category)
    {
        if (!IsFailure(category))
        {
            throw new ArgumentException(
                $"'{category}' is not an application failure outcome.",
                nameof(category));
        }
    }

    private static bool IsFailure(ApplicationTerminalOutcomeCategory category) =>
        category is not (
            ApplicationTerminalOutcomeCategory.CompletedNormally
            or ApplicationTerminalOutcomeCategory.CancelledByRequestedShutdown
            or ApplicationTerminalOutcomeCategory.ValidationDurationReached);

    private static string Limit(string value, int maximumLength) =>
        value[..Math.Min(value.Length, maximumLength)];
}
