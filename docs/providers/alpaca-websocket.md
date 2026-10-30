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
- `MarketData:Alpaca:SessionId`
- `Scanner:Universe:Symbols` (shared by both live adapters)

The [Alpaca Market Data getting-started guide](https://docs.alpaca.markets/us/docs/getting-started-with-alpaca-market-data) directs users to generate API credentials and save them; it also says to regenerate credentials if the secret was lost. For this adapter, `ApiKey` is the key ID and `SecretKey` is required: Alpaca's [WebSocket authentication guide](https://docs.alpaca.markets/us/docs/streaming-market-data#authenticate-via-message) authenticates the test stream with both `key` and `secret`. A URL is an endpoint, not a replacement for the secret. This adapter currently constructs `wss://stream.data.alpaca.markets/v2/{Feed}` itself and does not read `MarketData:Alpaca:Url`.

Set non-secret development selection values:

```powershell
dotnet user-secrets set "MarketData:Provider" "Alpaca" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:Feed" "test" --project src\MarketOpportunity.Web
dotnet user-secrets set "Scanner:Universe:Symbols" "FAKEPACA" --project src\MarketOpportunity.Web
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

The saved scanner universe is shared with Massive; setting it above replaces any previously saved live list. For a test-only override that leaves the saved list intact, use the [one-run command](../../README.md#override-selection-for-one-run). Migrate old provider-specific symbol keys using the [scanner-universe instructions](../../README.md#scanner-owned-universe-and-user-secrets-migration). Then run the app using the local SQL Server steps in the [README](../../README.md#local-run). User Secrets are for local development, are not encrypted, and must not be treated as a deployed secret store. The WebSocket adapter sends credentials only in Alpaca's documented authentication message and does not log them or include provider error text in exceptions.

## Adapter behavior and limitations

`AlpacaMarketDataSource` connects to `wss://stream.data.alpaca.markets/v2/{feed}`, authenticates with the configured key ID and secret, then subscribes to the configured `trades` and `quotes` symbol lists. It maps Alpaca `t` and `q` messages into canonical `TradeEvent` and `QuoteEvent` records, preserves provider event timestamps and trade IDs where supplied, stamps receive time through `TimeProvider`, and uses the configured session ID verbatim.

Alpaca quote sizes are passed through as reported round-lot counts; no conversion to shares is inferred. Alpaca quote messages do not expose a stable quote sequence identity in the documented schema, so their canonical `SequenceId` is null and the existing content-derived event identity applies. Invalid numeric market values are preserved for the shared data-quality rules to reject; malformed/missing required fields and unsupported event types fail explicitly.

The application retries an unexpected Alpaca stream close or source failure after `MarketData:Recovery:ReconnectDelayMilliseconds` (default 5000 ms; valid range 10-300000 ms). Authentication/subscription failures are also retried with the same delay. Shutdown cancellation stops the retry loop. Failures are logged with the provider and exception type only; provider error text and credentials are not logged.

Feed health becomes unhealthy immediately on disconnect, suspending current publications. It remains `Connecting` until a valid market event is received after reconnect. Since this checkpoint does not backfill or reconcile missed events, a disconnect sets an unresolved-gap flag; the dashboard displays the limitation and the publication gate keeps candidates suspended after the stream resumes. Historical warm-up, correction processing, and gap backfill are not implemented.

## Opt-in final A.1 diagnostics

The [instrumented launcher procedure](../../README.md#one-final-instrumented-a1-observation) enables `MarketData:Diagnostics:Enabled` for one run only. Default provider/analytics behavior is unchanged.

For each complete text WebSocket message, receipt UTC and monotonic time are sampled immediately after the final `ReceiveAsync` fragment and before decoding/parsing. Each canonical event retains its original `t` and per-item `ReceivedTimeUtc`; diagnostic `ParsedTimeUtc` is sampled after canonical construction and metadata extraction. A weak-reference sidecar associates connection ID, receipt/parsed times, and provider metadata without changing record equality, serialization, canonical identity, or event ordering.

The diagnostic metadata reader captures `bx`, `ax`, `c`, and `z`, including trade `c`/`z`. These fields still do not enter the canonical contract or database mappings. Bounded metadata groups and first/last zero-ask samples are exported separately; `conditionsSupplied`, `incomplete`, and omitted-attribution counters make missing/truncated evidence explicit. No field is interpreted as permission to accept a zero ask, no size is converted, and no price is filled or corrected.

Each connection attempt gets a fresh diagnostic GUID and sanitized Connecting/Authenticated/Subscribed/Live/Disconnected transitions; retries additionally record ReconnectAttempt and, on their first canonical event, Reconnected. Requested symbols and acknowledgement lists remain distinct. Complete-message counts include protocol/control messages, not individual fragments; market-data-message counts and canonical production counts are separate. JSON/schema parse failures are counted and still fail/recover through the existing pipeline.

Metrics use the `MarketOpportunity.LiveValidation` meter, `moe.live.*` instrument names, and a fixed `provider=Alpaca` label, never connection IDs/symbols as metric labels. Local diagnostic files/logs carry bounded connection/symbol evidence for correlation. Message-to-creation, creation-to-analysis, channel waits, and analysis/persistence durations use monotonic time; provider-to-message lag uses the two UTC clocks and can be negative. No histogram percentile should be described as exact; the local export reports quantile intervals and exact observed min/max.

Complete-message receipt is an adapter observation, not a wire timestamp. If the application has stopped reading under backpressure or a host pause, socket/provider buffering can precede that observation. Clock status, connection transitions, and channel/persistence evidence must be considered together before assigning causation. See the [consolidated A.1 validation history](../../VALIDATION_HISTORY.md#consolidated-a1-validation-record-2026-10-29).

## Optional test-stream connectivity check

The network test is in `tests\MarketOpportunity.AlpacaConnectivityTests`, deliberately outside the solution and default offline unit-test project. It is skipped clearly when key/secret are absent. Configure the same Web-project User Secrets shown above, with `Feed=test`, `Scanner:Universe:Symbols=FAKEPACA`, and a non-empty `SessionId`, then run:

```powershell
dotnet test tests\MarketOpportunity.AlpacaConnectivityTests\MarketOpportunity.AlpacaConnectivityTests.csproj
```

This check uses no brokerage endpoints and never prints credentials. Do not run it as part of deterministic/offline unit-test validation.
