namespace MarketOpportunity.Application;

public sealed record LivePipelineOptions
{
    public const int DefaultGracefulShutdownTimeoutSeconds = 20;

    public int GracefulShutdownTimeoutSeconds { get; init; } =
        DefaultGracefulShutdownTimeoutSeconds;

    public TimeSpan GracefulShutdownTimeout =>
        TimeSpan.FromSeconds(GracefulShutdownTimeoutSeconds);

    public void Validate()
    {
        if (GracefulShutdownTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException(
                "Graceful shutdown timeout must be between 1 and 300 seconds.");
        }
    }
}
