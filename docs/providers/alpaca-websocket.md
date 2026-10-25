# Alpaca Market Data WebSocket

This adapter reads Alpaca stock trades and quotes only. It does not call the Alpaca Broker API, link brokerage accounts, or place orders. The official [WebSocket Stream](https://docs.alpaca.markets/us/docs/streaming-market-data) and [Real-time Stock Data](https://docs.alpaca.markets/us/docs/real-time-stock-pricing-data) documentation is authoritative for current feed availability, authentication, schemas, connection limits, and terms.

## Supported feeds

| `MarketData:Alpaca:Feed` | WebSocket path | Capability coverage | Real-time |
|---|---|---|---|
| `test` | `wss://stream.data.alpaca.markets/v2/test` | Synthetic test stream | Yes; not exchange market data |
| `iex` | `wss://stream.data.alpaca.markets/v2/iex` | Limited IEX coverage | Yes |
| `sip` | `wss://stream.data.alpaca.markets/v2/sip` | Consolidated SIP coverage | Yes |
| `delayed_sip` | `wss://stream.data.alpaca.markets/v2/delayed_sip` | Consolidated SIP coverage | No; 15-minute delayed |

The test feed is available outside market hours and uses `FAKEPACA`. Do not describe the test feed as real U.S. market data or IEX-only activity as complete U.S. volume. Capability metadata is attached to feature snapshots and opportunities and is not inferred from provider-name conditions in Analytics.

## Local configuration

The fixture remains the default source. The Alpaca adapter reads:

- `MarketData:Alpaca:ApiKey`
- `MarketData:Alpaca:SecretKey`
- `MarketData:Alpaca:Feed`
- `MarketData:Alpaca:Symbols`
- `MarketData:Alpaca:SessionId`

Set non-secret development selection values:

```powershell
dotnet user-secrets set "MarketData:Provider" "Alpaca" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:Feed" "test" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:Symbols" "FAKEPACA" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:SessionId" "alpaca-test-session" --project src\MarketOpportunity.Web
```

Use secure prompts for both credentials so neither value is typed as a command argument:

```powershell
$secureKey = Read-Host "Alpaca API key ID" -AsSecureString
$keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    dotnet user-secrets set "MarketData:Alpaca:ApiKey" $apiKey --project src\MarketOpportunity.Web
}
finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    Remove-Variable apiKey, secureKey, keyPointer -ErrorAction SilentlyContinue
}

$secureSecret = Read-Host "Alpaca secret key" -AsSecureString
$secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
try {
    $secretKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
    dotnet user-secrets set "MarketData:Alpaca:SecretKey" $secretKey --project src\MarketOpportunity.Web
}
finally {
    if ($secretPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer)
    }
    Remove-Variable secretKey, secureSecret, secretPointer -ErrorAction SilentlyContinue
}
```

Then run the app using the local SQL Server steps in the [README](../../README.md#local-run). User Secrets are for local development, are not encrypted, and must not be treated as a deployed secret store. The WebSocket adapter sends credentials only in Alpaca's documented authentication message and does not log them or include provider error text in exceptions.

## Adapter behavior and limitations

`AlpacaMarketDataSource` connects to `wss://stream.data.alpaca.markets/v2/{feed}`, authenticates with the configured key ID and secret, then subscribes to the configured `trades` and `quotes` symbol lists. It maps Alpaca `t` and `q` messages into canonical `TradeEvent` and `QuoteEvent` records, preserves provider event timestamps and trade IDs where supplied, stamps receive time through `TimeProvider`, and uses the configured session ID verbatim.

Alpaca quote sizes are passed through as reported round-lot counts; no conversion to shares is inferred. Alpaca quote messages do not expose a stable quote sequence identity in the documented schema, so their canonical `SequenceId` is null and the existing content-derived event identity applies. Invalid numeric market values are preserved for the shared data-quality rules to reject; malformed/missing required fields and unsupported event types fail explicitly.

The adapter currently has no reconnect, historical warm-up, correction processing, or gap backfill. A disconnected/error source updates the application publication lifecycle so existing candidates cannot remain active. This does not implement a comprehensive recovery workflow.

## Optional test-stream connectivity check

The network test is in `tests\MarketOpportunity.AlpacaConnectivityTests`, deliberately outside the solution and default offline unit-test project. It is skipped clearly when key/secret are absent. Configure the same Web-project User Secrets shown above, with `Feed=test`, `Symbols=FAKEPACA`, and a non-empty `SessionId`, then run:

```powershell
dotnet test tests\MarketOpportunity.AlpacaConnectivityTests\MarketOpportunity.AlpacaConnectivityTests.csproj
```

This check uses no brokerage endpoints and never prints credentials. Do not run it as part of deterministic/offline unit-test validation.
