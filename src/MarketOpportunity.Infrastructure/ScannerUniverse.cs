using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.Infrastructure;

public static class ScannerUniverse
{
    public static IReadOnlyList<string> FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configuredSymbols = configuration["Scanner:Universe:Symbols"];
        if (string.IsNullOrWhiteSpace(configuredSymbols))
        {
            throw new InvalidOperationException(
                "Scanner:Universe:Symbols must be a non-empty comma-separated list. "
                + "Provider-specific Symbols settings are no longer read.");
        }

        var symbols = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuredSymbols.Split(','))
        {
            var symbol = entry.Trim();
            if (symbol.Length == 0)
            {
                throw new InvalidOperationException(
                    "Scanner:Universe:Symbols must not contain empty entries.");
            }

            symbol = symbol.ToUpperInvariant();
            if (seen.Add(symbol))
            {
                symbols.Add(symbol);
            }
        }

        return symbols.AsReadOnly();
    }
}
