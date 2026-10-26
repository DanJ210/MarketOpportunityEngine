# MarketOpportunityEngine

Milestone A defaults to a deterministic recorded-fixture scanner and supports optional read-only live ingestion from Alpaca Market Data or Massive. It does not place orders or use a broker API.

## Requirements

- .NET 10 SDK
- Docker Desktop with the SQL Server container available for local app and SQL integration tests

## Fixture behavior

`src\MarketOpportunity.Web\Fixtures\momentum-continuation.json` contains synthetic trades and quotes for `MOE` and `QNT` in two explicit fixture sessions: `fixture-2026-10-22` and `fixture-2026-10-23`. Every event carries its event time, receive time, provider, sequence ID, symbol, and session ID.

The source preserves fixture arrival order, including interleaved symbols and late/out-of-order events. Every source must provide a non-empty symbol, provider, and explicit session ID; the scanner rejects events missing any of these identities rather than inferring them. The bounded state deduplicates stable identities and orders events deterministically by event time and identity before computing features. State and session VWAP are isolated by symbol and explicit session ID; no session is inferred from a UTC calendar date. The pipeline creates and persists one immutable feature/opportunity result for each of the four symbol/session pairs.

The `features-v9` snapshot retains session VWAP slope over the existing `MomentumWindowSeconds` window. At each distinct trade event time, the calculator samples cumulative session VWAP; slope is the change from the first to last in-window sample divided by elapsed event-time minutes, so irregularly spaced observations are time-normalized. The snapshot reports the signed price-per-minute value and `Rising`, `Falling`, `Flat`, or `Insufficient` direction. `Flat` means exactly zero slope; fewer than two distinct in-window timestamps with a positive cumulative volume yields a null slope and `Insufficient`. This is descriptive feature output only and does not affect setup classification, scoring, publication, or vetoes.

`features-v9` adds fast/medium EMA values, their signed relationship, and event-time slopes as descriptive features. `Scanner:EmaFastPeriod` defaults to **5 trades** and `Scanner:EmaMediumPeriod` to **20 trades**; both must be positive integers and the fast period must be strictly smaller than the medium period. These are observation counts, not elapsed-time periods. For every retained trade in the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`, in the existing canonical event order, the calculator ignores trade size and updates the EMA once per trade observation. Quotes, receive time, and wall-clock time do not contribute.

For period `N`, the first EMA is the arithmetic mean of the first `N` positive eligible trade prices (SMA seed); subsequent observations use `EMA = price * (2 / (N + 1)) + prior EMA * (1 - 2 / (N + 1))`, with decimal arithmetic. Fewer than `N` eligible observations yields null for that EMA. Any eligible trade price `<= 0` makes all EMA values, relationship, and slopes null, matching the strict retained-session extrema contract; invalid trades are not discarded. `FastMediumEmaSpreadPercent` is `(FastEma - MediumEma) / MediumEma * 100m` and is null until both EMAs are available. Slopes use the existing `MomentumWindowSeconds` (default **60 seconds**) and are signed price-per-minute changes between the first and last initialized EMA samples whose timestamps are in the inclusive interval `[AsOfUtc - MomentumWindowSeconds, AsOfUtc]`. At a duplicate timestamp, all trades update the EMA in canonical order and the timestamp sample is its last ordered EMA. Fewer than two distinct in-window timestamps yields a null slope; equal endpoints yield zero. Earlier eligible trades still warm up the EMA even when they precede the slope interval.

Read EMA values with `HasCompleteSessionData`: `false` means any non-null value may describe only retained partial state, and is not a complete-session indicator. A future event does not affect an earlier value/identity when retained eligible state and completeness are unchanged, but can evict eligible trades from bounded state and thereby change a later historical-as-of result. New fields are nullable JSON decimals (`fastEma`, `mediumEma`, `fastMediumEmaSpreadPercent`, `fastEmaSlopePerMinute`, `mediumEmaSlopePerMinute`) and participate explicitly in deterministic feature identity. They do not affect detector qualification, scoring, classifications, publication, vetoes, or the unchanged `momentum-continuation-v1` setup. The configuration and feature versions advance to `scanner-config-v9` and `features-v9`; prior persisted snapshots, configurations, and decisions are not rewritten.

`SecondaryHorizonReturnPercent` adds a second descriptive ROC alongside the unchanged `ShortHorizonReturnPercent`. `Scanner:SecondaryMomentumWindowSeconds` defaults to **30 seconds** and must be positive and strictly less than `MomentumWindowSeconds` (default **60 seconds**). For the same symbol and explicit session, select trades in the inclusive interval `[AsOfUtc - SecondaryMomentumWindowSeconds, AsOfUtc]` and calculate `(last price - first price) / first price * 100` using decimal arithmetic. Fewer than two observations, or any non-positive price in that window, yields **null**, not zero; equal positive prices yield zero. Trades at equal timestamps remain separate observations, ordered by the existing event-time/provider/ordinal-sequence/canonical-identity ordering, so two same-time trades can produce ROC even though VWAP slope remains insufficient. Quote prices, receive time, and wall-clock time do not enter the calculation.

`SessionCumulativeVolume` is a nullable signed 64-bit integer: the checked sum of trade sizes across **all retained same-symbol, explicit-session trades with `EventTimeUtc <= AsOfUtc`**, including trades outside both ROC windows. It is not a rolling-window value. Quotes (including quote sizes), receive time, and wall-clock time do not contribute. Canonical duplicates retain the first observation; input ordering and equal timestamps use the existing deterministic state ordering.

- No eligible trades yields **null**, never a valid-looking zero. One positive-size trade is enough for a descriptive volume; `MinimumRequiredTrades` does not apply to this field.
- Any eligible size `<= 0` makes volume **null**, without silently removing that trade. Existing `IsPriceDataValid`, data quality, and detector veto behavior remain unchanged. Positive sizes are summed independently of price validity: a non-positive price still invalidates existing price data but does not itself make share volume unavailable.
- The existing checked `Enumerable.Sum(long)` behavior throws `OverflowException`; totals are not wrapped or clamped. `ShortHorizonVolume` retains its original primary-window calculation and empty/invalid-input behavior.
- Always read this field **together with `HasCompleteSessionData`**. True means no event has been evicted from this state; a non-null value is the total of the observed retained session. False means any non-null value is only a **retained partial sum**, not complete session volume. The sticky flag is conservative even if an evicted event was a quote. No retained eligible trades or any invalid retained size still yields null after eviction. This is not a guarantee of upstream feed completeness or session-start coverage.
- Future events cannot affect an earlier value/identity while retained eligible state and completeness are unchanged. However, future arrivals can evict earlier events; subsequent historical-as-of calculations then reflect only remaining state and incomplete status. Bounded state is not unlimited historical replay; no backfill is added.

`SessionHighPrice` and `SessionLowPrice` are nullable decimals: the maximum and minimum positive trade prices across **all retained same-symbol, explicit-session trades with `EventTimeUtc <= AsOfUtc`**, not either ROC window. HTTP and persisted JSON use `sessionHighPrice` and `sessionLowPrice`.

- No eligible trades yields **null for both**. One positive-price trade yields its price for both; equal prices are valid. `MinimumRequiredTrades` does not apply to these descriptive values.
- Any eligible price `<= 0` makes **both null**, including invalid observations outside both ROC windows. Invalid prices are not silently discarded.
- Sizes do not weight or filter extrema. A positive price with zero/negative size still contributes to high/low, even though it makes `SessionCumulativeVolume` unavailable and existing `IsPriceDataValid`, data-quality and detector veto semantics still apply. Conversely, positive sizes remain summable when prices are invalid. No existing validity/veto logic changes.
- Quotes and quote prices, receive time, and wall-clock time do not contribute. Canonical duplicates retain their first observation, including conflicting payloads; existing deterministic ordering and symbol/explicit-session isolation are preserved.
- Read extrema **together with `HasCompleteSessionData`**: true means no state event has been evicted and the values cover the observed retained session, not proof of upstream session-start coverage. False means any non-null values are **retained partial extrema**, not complete session high/low. The flag remains sticky and conservative, including quote eviction. Empty/invalid retained prices still yield null after eviction.
- Future trades, including extreme/invalid prices, cannot change an earlier snapshot or identity when retained eligible state and completeness remain unchanged. Future arrivals can nevertheless evict earlier trades, changing subsequent historical-as-of extrema/completeness. Bounded state is not unlimited historical replay; no backfill is added.

`SessionRangePositionPercent` is a nullable decimal describing the latest eligible trade within the retained session range: `(ReferencePrice - SessionLowPrice) / (SessionHighPrice - SessionLowPrice) * 100m`. It reuses the same retained, same-symbol, explicit-session trades at/before `AsOfUtc`, extrema, and deterministic latest-trade ordering. The value is not clamped or rounded and is independent of both ROC windows and `MinimumRequiredTrades`.

- Empty retained trades, unavailable/non-positive reference price, unavailable extrema, or a zero-width range yields **null**; one trade and multiple equal-price trades yield null rather than zero or 50.
- Any eligible non-positive trade price makes both extrema and range position null, including observations outside both ROC windows. Invalid prices are not discarded.
- Trade sizes do not weight or filter the range: positive prices with zero/negative sizes still define the extrema and latest reference. Existing price validity, quality, cumulative/rolling volume and veto behavior is unchanged.
- Quotes, quote prices, receive time, and wall-clock time do not contribute. Canonical first-occurrence duplicate handling, including conflicting payloads, and symbol/session isolation are unchanged.
- Always interpret this value with `HasCompleteSessionData`: false means a non-null position describes only retained partial-session state (including after quote eviction); true means no observed state event has been evicted, not proof of upstream session-start coverage. Future events do not affect the earlier result if eligible retained state and completeness are unchanged, but may evict earlier events from bounded state. No historical backfill is added.
- HTTP and persisted JSON use `sessionRangePositionPercent`; null is serialized explicitly. The value participates explicitly in deterministic feature identity, in addition to event identities and serialized configuration.

The preceding range-position slice advanced defaults from `features-v7` / `scanner-config-v7` to **`features-v8` / `scanner-config-v8`**. The current EMA slice below advances them to `features-v9` / `scanner-config-v9`. Prior persisted configurations, snapshots, and decisions remain immutable; changing analysis creates a new version/record. The setup remains `momentum-continuation-v1`. Session volume's empty/invalid-size/checked-overflow/completeness contract, rolling volume, both ROC horizons and validation, exact VWAP-slope behavior, and detector outcomes remain unchanged. No schema migration, detector/scoring/classification/publication/veto, dependency or UI change is part of either descriptive slice.

## Local run

From the repository root, start SQL Server and the app:

```powershell
docker compose up -d sqlserver
dotnet run --project src\MarketOpportunity.Web --launch-profile http
```

The `http` launch profile in `src\MarketOpportunity.Web\Properties\launchSettings.json` sets the development environment, opens the browser, and configures the app to listen at `http://localhost:5228`. You do not need to set `ASPNETCORE_URLS` yourself. Use the `https` profile instead if you want HTTPS and have the local development certificate trusted.

Development startup creates the application schema with EF Core `EnsureCreated`; if an earlier schema already exists, it applies the explicit-session and opportunity-provenance migrations. Pre-migration records are labeled `legacy-unknown` rather than assigned an inferred session. The current session-scoped feature contract is versioned as `features-v9`, and the effective scanner configuration as `scanner-config-v9`; prior immutable decisions are not rewritten. The development connection string targets SQL Server at `localhost,1433`. The checked-in password is only for local Docker development; override `MSSQL_SA_PASSWORD` when needed and keep credentials out of source control.

In a second PowerShell terminal, verify the running app:

```powershell
Invoke-RestMethod http://localhost:5228/health
Invoke-RestMethod http://localhost:5228/api/v1/system/status
Invoke-RestMethod http://localhost:5228/api/v1/opportunities
Invoke-RestMethod "http://localhost:5228/api/v1/symbols/MOE/snapshot?sessionId=fixture-2026-10-22"
```

The dashboard at `http://localhost:5228/` shows all symbol/session opportunities and keeps classification, publication lifecycle/reason, provider/feed/coverage, score, event-time freshness, evidence, contradictions, vetoes, invalidation, expiry, and feature/setup/configuration versions visible. Publication state is a current application read model; stale, offline, connecting, or errored source health suspends a candidate without overwriting the immutable analytical evidence. `GET /api/v1/opportunities` returns opportunity plus publication state; the snapshot endpoint returns the latest snapshot for a symbol or a specific session when `sessionId` is supplied.

## Optional Alpaca Market Data

Alpaca is available for development/live-stream pipeline testing, including its always-available synthetic `test` feed and `FAKEPACA` symbol. This is market-data-only: the adapter does not use Alpaca's Broker API or place orders. Configure credentials through .NET User Secrets, never `appsettings.json` or browser code. See [docs/providers/alpaca-websocket.md](./docs/providers/alpaca-websocket.md) for setup, feed coverage, and the separately invoked opt-in connectivity test.

The default provider remains `Fixture`. To select Alpaca's test stream, configure a non-empty explicit session ID and User Secrets values for `MarketData:Alpaca:ApiKey` and `MarketData:Alpaca:SecretKey`; set `MarketData:Provider` to `Alpaca`. `MarketData:Alpaca:Feed` defaults to `test`, and `Symbols` defaults to `FAKEPACA`. `iex`, `sip`, and `delayed_sip` feeds are also represented in capability metadata; confirm subscription entitlements before selecting live-market feeds. IEX is limited coverage, SIP is consolidated, and delayed SIP is 15 minutes delayed.

To explicitly run the separate Alpaca test-stream connectivity check, configure the credentials and test-feed settings as described in [docs/providers/alpaca-websocket.md](./docs/providers/alpaca-websocket.md), then run:

```powershell
dotnet test tests\MarketOpportunity.AlpacaConnectivityTests\MarketOpportunity.AlpacaConnectivityTests.csproj
```

This project is intentionally outside the solution and default offline unit-test run. It skips when credentials are absent; when configured, it makes a network connection to Alpaca's synthetic test feed. It does not use brokerage endpoints or place orders.

## Optional live market data (Massive)

The recorded fixture remains the default. Massive remains a supported live market-data provider, and Alpaca is the current development/live-stream testing alternative; neither is an execution integration. Massive live mode consumes trade and quote WebSocket events and analyzes/persists changed symbol/session state at most once every five seconds. The status API and dashboard show the most recent receive time and receive age; the feed changes from `Live` to `Stale` when no event arrives within `Scanner:MaximumLiveFeedStalenessSeconds` (30 seconds by default). Receive freshness is distinct from event-time analysis. Both live adapters start cold without historical warm-up or gap backfill and do not automatically reconnect after a disconnect; the scanner therefore needs to accumulate enough live events before its analysis becomes usable.

For local development, store the API key in .NET User Secrets rather than a checked-in file or shell command history. User Secrets keep the value outside the repository but are not encrypted; use a protected local account and never treat them as a production secret store. The provider, watchlist, session label, API key, and SQL connection string are read through `IConfiguration`, so deployment can later supply the same settings from Azure Key Vault. The configured session ID is applied verbatim; there is no exchange-calendar lookup or date-based inference. Change it explicitly when starting a different session.

```powershell
dotnet user-secrets set "MarketData:Provider" "Massive" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Massive:Symbols" "AAPL,MSFT,SPY" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Massive:SessionId" "operator-session-2026-10-23" --project src\MarketOpportunity.Web
```

The Web project already has a `UserSecretsId`; no initialization command is needed.

Enter the API key with a secure PowerShell prompt so the value is not part of the command you type into shell history:

```powershell
$secureKey = Read-Host "Massive API key" -AsSecureString
$keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    dotnet user-secrets set "MarketData:Massive:ApiKey" $apiKey --project src\MarketOpportunity.Web
}
finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    Remove-Variable apiKey, secureKey, keyPointer -ErrorAction SilentlyContinue
}

dotnet run --project src\MarketOpportunity.Web --launch-profile http
```

User Secrets are stored outside the repository but are not encrypted. They are for local development only; Azure deployments should use Key Vault. The same configuration pattern can store `ConnectionStrings:MarketOpportunity` if desired; the existing local development configuration is used unless overridden. Massive must authorize the key for the requested real-time feed; check its current plan and data-use terms before relying on live data. This is read-only market-data ingestion: no orders are placed and no Fidelity account is accessed. Stop the app to end the feed. Restore fixture mode with `dotnet user-secrets set "MarketData:Provider" "Fixture" --project src\MarketOpportunity.Web`.

### Massive authentication troubleshooting

For a protocol overview, current adapter behavior, and links to Massive's official WebSocket documentation, see [docs/providers/massive-websocket.md](./docs/providers/massive-websocket.md).

`auth_failed` means the provider rejected access before subscriptions; it does not always mean
the API key is invalid. A WebSocket `connected` status confirms only the transport connection,
not authenticated access. A successful REST request also does not establish WebSocket access.
When Massive reports that the account plan does not include WebSocket access, the app reports
that restriction explicitly. Verify the plan for the account owning the configured key at
[Massive pricing](https://massive.com/pricing), or contact Massive support. The plan must cover
the adapter's real-time stocks trades and quotes; replacing a valid key does not fix a plan restriction.

For other authentication failures, verify the active `MarketData:Massive:ApiKey`; replace an
invalid or revoked key using the secure prompt above, then restart the app.
Do not paste the key into source, chat, or diagnostic logs.
The structured environment variable `MarketData__Massive__ApiKey`, if set, overrides User Secrets.
The old `MARKETOPPORTUNITY_MASSIVE_API_KEY` variable is no longer read by the adapter.
User Secrets are loaded by the Development launch profiles.

A connection/subscription error is separate: check that your Massive plan includes real-time
stocks trades **and** quotes, and that another app is not using the allowed WebSocket connection.
The adapter uses `wss://socket.massive.com/stocks`; it does not silently fall back to delayed data.

## Build and offline unit tests

These commands do not require SQL Server or network credentials:

```powershell
dotnet build MarketOpportunityEngine.sln
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj
```

The standalone Alpaca connectivity test above is not part of this offline validation.

## SQL Server integration tests

The SQL test project is separate so normal unit tests remain independent of SQL Server. Integration tests use the real EF Core SQL Server provider, require an explicit test connection, and throw a clear configuration error rather than skip when it is missing. Configure a dedicated SQL Server database whose name contains `test`; do not point this variable at the development or production database. The persistence test creates its schema if needed or migrates an existing test schema using the same startup branches as the application, and deletes only rows written with its unique test symbols/configuration versions. Migration tests create uniquely named temporary databases and drop only those databases after each test, so the configured SQL login must be allowed to create and drop databases. No existing database or user-owned rows are dropped or cleaned up.

For the local Docker SQL Server, run:

```powershell
$env:MSSQL_SA_PASSWORD = "LocalDev_Only!Moe2026"
docker compose up -d sqlserver
$env:MARKETOPPORTUNITY_SQL_TEST_CONNECTION = "Server=localhost,1433;Database=MarketOpportunityEngine_IntegrationTests;User Id=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj
Remove-Item Env:MARKETOPPORTUNITY_SQL_TEST_CONNECTION
```

Run only the explicit-session migration tests with:

```powershell
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~ExplicitSessionMigrationTests
```

The migration tests exercise the clean-schema `EnsureCreated` and existing-schema `Migrate` branches used by development startup. They verify non-null `SessionId` columns and symbol/session/time indexes, and confirm that pre-existing market-event, feature-snapshot, and opportunity values remain unchanged while existing rows receive `legacy-unknown`. No session is inferred and no historical analysis is rewritten. The remaining SQL tests verify event, feature, opportunity, evidence, and configuration persistence; replay idempotency; immutable results for changed inputs/configuration; and symbol/session separation. If these commands are not run with a configured SQL Server, SQL integration coverage has **not** been run.

The hosted fixture regression test also requires this guarded SQL test connection. It starts the real Web application in an isolated `Testing` environment (no Development User Secrets), supplies provider/connection settings before `Program` captures them, and explicitly registers the fixture source, checked-in scanner configuration, and guarded SQL connection. Test setup creates or migrates only that configured test schema; the application's Development startup branch is not used by this host.

The test verifies:

- Exactly `MOE` and `QNT`, each in `fixture-2026-10-22` and `fixture-2026-10-23`, with the expected long/short classifications and analytical versions.
- The effective configuration HTTP payload equals the checked-in configuration, including the 30-second secondary ROC window.
- All four detail payloads equal their list entries; all four session-specific HTTP snapshots equal deterministic fixture replay.
- Once fixture results are ready, a valid but absent opportunity GUID and an absent symbol each return exactly `404 Not Found`. An explicit unknown session returns exactly `404 Not Found` for both `MOE` and `QNT`, rather than falling back to either symbol's other session; the valid session-specific payload checks above remain exact.
- Exact opportunity/snapshot IDs, persisted analytical fields, evidence/invalidation, provenance, and deserialized snapshot payloads equal HTTP results.
- All **26** canonical event identities and trade/quote fields, including event/receive times, provider, sequence, and explicit session; group counts are **7/6/6/7** for MOE-22/MOE-23/QNT-22/QNT-23. Canonical identity deduplication retains the first occurrence.
- Publication state/reason at fixture replay time (`2026-10-23T14:30:42Z`): both October 22 opportunities are `Expired` / `OpportunityExpired`; both October 23 opportunities are `Active` / `PublicationAllowed`. This is current in-memory publication state, not persisted lifecycle history.

SQL comparisons select the expected fixture identities and analytical versions, not every historical row in a fixture session:

```powershell
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests
```

It writes the deterministic fixture records to the configured test database and does not delete those records afterward. Never point the test connection at development or production data.

### Fixture runtime verification order

Before running any SQL command, check whether `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` is configured without printing its value. Validate it with `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`: `InitialCatalog` must be non-empty and contain `test` (case-insensitive), and the database must be dedicated to testing. A matching name alone does not establish that it is disposable. If the variable is absent, invalid, or targets a non-test database, stop: do not start the app, create/migrate a database, or run integration tests. Configure the connection locally; never paste credentials into chat or logs.

An absent variable does not require another SQL Server instance: with operator approval, reuse the local Compose `sqlserver` service and target a separate database such as `MarketOpportunityEngine_IntegrationTests`. Compose supplies the server and persistent volume, not the application/test database schema. The tests create the dedicated database/schema as needed. Build the guarded connection in process memory from the local SQL credentials without displaying or checking them in; do not retarget or delete the development database. Preserve any container that was already running.

After that safety check, use this order. Repeat the hosted test against the **same database**, without deleting/resetting fixture records, to check replay-safe assertions:

```powershell
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj
dotnet build MarketOpportunityEngine.sln
```

### Current verification: descriptive EMA slice (2026-10-25)

Focused replay **98/98 passed** and full offline units **162/162 passed**. Against the same dedicated SQL Server test database, the hosted fixture regression passed **1/1 on each of two consecutive runs** without reset; persistence/migration tests passed **3/3**. The solution build succeeded with **0 warnings and 0 errors**. All runs had 0 failures and 0 skips.

The environment connection variable was absent. The already-running healthy Compose SQL Server and only its dedicated `MarketOpportunityEngine_IntegrationTests` database were reused. The catalog was checked using `SqlConnectionStringBuilder` in an isolated PowerShell 7 child and the database identity was verified with a read-only query. No SqlClient runtime was loaded in the parent process that launched `dotnet`; temporary process overrides were cleared. Fixture records were retained. No browser, live-provider, User Secrets, or Azure verification was performed.

Hosted HTTP snapshot payloads and raw persisted SQL snapshot JSON independently assert these fast EMA values; the remaining nullable outputs are null because the fixture sessions contain fewer than 20 trades and fewer than two initialized fast samples in the 60-second slope interval:

| Symbol/session | Ordered eligible prices | Fast EMA (5) | Medium EMA (20) | Spread % | Fast slope / price-min | Medium slope / price-min |
|---|---|---:|---:|---:|---:|---:|
| MOE / fixture-2026-10-22 | 149.90, 150.08, 150.23, 150.40, 150.48 | 150.218 | null | null | null | null |
| MOE / fixture-2026-10-23 | 150.50, 150.30, 150.08, 149.90 | null | null | null | null | null |
| QNT / fixture-2026-10-22 | 80.40, 80.20, 80.08, 79.90 | null | null | null | null | null |
| QNT / fixture-2026-10-23 | 79.80, 80.00, 80.20, 80.40, 80.48 | 80.176 | null | null | null | null |

The persistence regression confirms that later input creates a new feature snapshot and opportunity decision without rewriting the original records; the new snapshot JSON independently asserts fast EMA **102.1** and fast slope **12 price/minute**, while the original four-trade snapshot remains null for fast/medium EMA.

Exact offline commands used:

```powershell
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj
```

The guarded SQL sequence was run in the order documented above (hosted fixture twice, persistence/migrations, full unit suite, solution build).

**Failed attempts, not passing evidence:** The first focused build attempt exposed a nullable conditional typing error; fixed before tests ran. A dirty positional `FeatureSnapshot` test fixture needed the new nullable fields. Initial EMA assertions then exposed incorrect expected slopes/warm-up values and helper session mismatches; corrected before the passing **98/98** focused run. SQL guard attempts involving malformed encoded-runner construction and an invalid connection-string setter stopped before tests; a subsequent connection attempt using a mismatched credential failed login. These were not passing SQL evidence and did not change data. The eventual guard reused the existing healthy container, verified the dedicated database read-only, and successfully validated the catalog via the isolated builder; then the complete guarded SQL sequence passed. No database or fixture reset was performed.

### Historical validation: retained-session range-position slice

The range-position slice passed focused replay **83/83**, full offline units **147/147**, hosted fixture **1/1 twice**, persistence/migrations **3/3**, and a solution build with **0 warnings and 0 errors**. Fixture HTTP payloads and persisted SQL JSON asserted exact range positions alongside extrema, volume and completeness.

Fixture values were independently derived from the checked-in retained trades' reference prices and extrema:

| Symbol/session | ReferencePrice | SessionHighPrice | SessionLowPrice | SessionRangePositionPercent | SessionCumulativeVolume | HasCompleteSessionData |
|---|---:|---:|---:|---:|---:|---|
| MOE / fixture-2026-10-22 | 150.48 | 150.48 | 149.90 | 100 | 5600 | true |
| MOE / fixture-2026-10-23 | 149.90 | 150.50 | 149.90 | 0 | 5450 | true |
| QNT / fixture-2026-10-22 | 79.90 | 80.40 | 79.90 | 0 | 5050 | true |
| QNT / fixture-2026-10-23 | 80.48 | 80.48 | 79.80 | 100 | 5650 | true |

The SQL persistence regression retains the original snapshot at high/low/reference `103/100/103` and position **100**, then persists a new result after later input at high/low/reference `103.50/100/102.50` with the exact decimal position `(102.50 - 100) / (103.50 - 100) * 100m`. It checks the original snapshot JSON and original persisted opportunity fields/evidence remain unchanged.

The SQL connection environment variable was absent. Reused only the already-running healthy Compose SQL Server and `MarketOpportunityEngine_IntegrationTests`; an isolated encoded PowerShell 7 child loaded the built Windows SQL-client assembly with `Assembly.LoadFrom`, verified the dedicated catalog through `SqlConnectionStringBuilder`, and returned no secret text. No SQL-client runtime was loaded into the parent launching `dotnet`; temporary overrides were cleared. No service/database was started, stopped, reset, or dropped, and fixture rows were retained. No browser, live-provider, User Secrets, or Azure verification is claimed.

**Failed attempts, not passing evidence:** The first focused run had **80 passed / 2 failed**: one incorrect expected 25% value for a 50% midpoint and a changed-configuration test using the new default version instead of a distinct version. Both test expectations were corrected; focused and full suites then passed as reported. Initial isolated SQL guard attempts used the root assembly (unsupported on that platform), passed the DLL path as a script, or assigned an unsupported builder property; each failed before running SQL tests. The successful guard invoked an encoded PowerShell 7 child correctly, loaded the built Windows runtime assembly using `Assembly.LoadFrom`, and populated `SqlConnectionStringBuilder` through its indexer. These were guard/runner setup issues, not evidence SQL was unavailable.

Historical validation (2026-10-25, prior session cumulative-volume slice): focused replay tests passed **46/46**, then the full offline suite passed **110/110**, both exit 0. The ordered SQL sequence above then passed: hosted fixture **1 passed, 0 failed, 0 skipped** on each of two same-database runs; persistence/migrations **3 passed, 0 failed, 0 skipped**; units **110 passed, 0 failed, 0 skipped**; solution build succeeded with **no warnings or errors reported**, all commands completed successfully. Hosted HTTP and raw persisted snapshot JSON assert exact volume/completeness: MOE-22 **5600**, MOE-23 **5450**, QNT-22 **5050**, QNT-23 **5650**. All prior analytical, negative-lookup, canonical-event, and SQL immutability assertions remain in place.

The existing healthy Compose server was reused with this request's authorization, targeting only `MarketOpportunityEngine_IntegrationTests` (plus migration tests' unique temporary databases). The absent connection variable was supplied only in process memory and validated with `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`; fixture records were retained, the configured database was not dropped, and temporary overrides were cleared. No provider User Secrets or live connectivity were used. Browser validation was not repeated because no UI changed.

Earlier attempts in this slice are **not passing SQL evidence**: the Windows PowerShell child guard could not resolve SQL-client dependencies; the terminal runner subsequently returned `program not found` before launching validation. Recovery used a temporary VS Code task, loading the SQL-client runtime with `Assembly.LoadFrom` only inside a PowerShell 7 child for the builder guard. The parent then ran the unchanged documented commands. The helpers/task were removed afterward. Prior second-ROC (**30 replay / 94 units**), VWAP-slope (**8 replay / 72 units**), native-SNI/wrapper failures, and browser runs remain historical, not results of this slice. Exact commands and evidence boundaries are separated in [PROJECT_STATUS.md](./PROJECT_STATUS.md#20-test-status).

Run these offline commands first, before the guarded SQL sequence:

```powershell
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj
```

Historical validation (2026-10-25, descriptive retained-session extrema slice): the focused offline replay command above passed **72/72**, then full units **136/136**, all exit 0. The documented guarded SQL sequence passed in order: hosted fixture **1 passed, 0 failed, 0 skipped** twice against the same database without reset; persistence/migrations **3 passed, 0 failed, 0 skipped**; final units **136 passed, 0 failed, 0 skipped**; solution build **0 warnings, 0 errors**. All commands exited 0. The 26 replay cases preserve all prior volume, ROC and exact VWAP-slope assertions.

Exact new values were derived from the checked-in trade fixture and asserted in hosted HTTP snapshot payloads **and raw persisted SQL snapshot JSON**, alongside the unchanged volume/completeness assertions:

| Symbol/session | SessionHighPrice | SessionLowPrice | SessionCumulativeVolume | HasCompleteSessionData |
|---|---:|---:|---:|---|
| MOE / fixture-2026-10-22 | 150.48 | 149.90 | 5600 | true |
| MOE / fixture-2026-10-23 | 150.50 | 149.90 | 5450 | true |
| QNT / fixture-2026-10-22 | 80.40 | 79.90 | 5050 | true |
| QNT / fixture-2026-10-23 | 80.48 | 79.80 | 5650 | true |

The connection variable was absent. With this request's explicit authorization, validation reused only the existing healthy Compose server and `MarketOpportunityEngine_IntegrationTests` (plus migration tests' unique temporary databases). The process-only connection was guarded using `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` in an isolated encoded PowerShell 7 child with `Assembly.LoadFrom`; the catalog was non-empty and contained `test`. No SQL-client runtime was loaded into the parent launching `dotnet`. Credentials/settings stayed in memory, temporary overrides were cleared, fixture records retained, and no configured database/server was dropped, reset, started or stopped. No helper/task files were needed.

New failed attempt (not passing evidence): the initial focused run had **66 passed / 6 failed** because a test expected cumulative volume 130 instead of 160 (the three older sizes are 10+20+30). Only that expected constant was corrected; the successful focused/full runs above followed. No SQL/runner failures occurred in this slice. All payload, negative-lookup, canonical-event, analytical, versioning and immutability assertions remain. No SQL blocker remains; no new browser, live-provider/User Secrets or Azure checks were run or claimed. Prior failures and historical runs above are not new evidence.

At the historical extrema checkpoint, the next-slice recommendation was retained-session range position; that slice is now implemented and verified above.

An earlier attempt exposed that the old factory's late configuration override allowed local settings to select Massive; access was denied. The isolated environment and explicit fixture registration above fix that test-isolation defect. Successful final runs used Fixture only. No runtime/application behavior changed.

Only when the guarded connection points to a safely available **local** SQL Server should you manually verify the dashboard. In the launch terminal, temporarily set `ConnectionStrings__MarketOpportunity` to the validated test connection and `MarketData__Provider` to `Fixture`, then use the documented `http` launch profile. These environment overrides prevent existing User Secrets from selecting a live provider or another database. Do not display the connection value. Remove the temporary overrides afterward and stop only the app/container processes you started.

Check all four `MOE`/`QNT` and `fixture-2026-10-22`/`fixture-2026-10-23` combinations, publication state/reason, and `RecordedFixture`/`Synthetic` provenance in the dashboard. Record SQL/API and browser verification separately; a passing build or hosted API test does not prove the interactive dashboard works. A new walkthrough is unnecessary for test-only changes; verify the affected running path when runtime/UI behavior changes. No Alpaca API key or live-provider connection is needed.

## Dependency vulnerability scan

```powershell
dotnet list MarketOpportunityEngine.sln package --vulnerable --include-transitive
```

## API

- `GET /health`
- `GET /api/v1/system/status`
- `GET /api/v1/opportunities`
- `GET /api/v1/opportunities/{id}`
- `GET /api/v1/symbols/{symbol}/snapshot`
- `GET /api/v1/symbols/{symbol}/snapshot?sessionId={sessionId}`
- `GET /api/v1/configuration/effective`

Opportunity list/detail responses include the immutable analytical `Opportunity` and a separate `PublicationDecision` (`Active`, `Suspended`, `Expired`, or `NotQualified`) with a reason and evaluation time. Market-data provider/feed/coverage metadata is recorded with each analysis; a provider failure changes current publication status to suspended/error without rewriting setup evidence.

Feature snapshots expose descriptive `SessionVwapSlopePerMinute`, `SessionVwapSlopeDirection`, `SecondaryHorizonReturnPercent`, `SessionCumulativeVolume`, `SessionHighPrice`, `SessionLowPrice`, and `SessionRangePositionPercent`. Read retained-session values with `HasCompleteSessionData` as described above; none of these descriptive additions is used for setup scoring or qualification.

## Current limits

- Two synthetic fixture symbols and two explicit fixture sessions; this is not a market recommendation.
- One Momentum Continuation detector; the remaining three Milestone A detectors are not implemented.
- Session VWAP and momentum use bounded in-memory events per symbol/session. If bounded state evicts events, the analysis is explicitly marked incomplete and vetoed.
- No exchange calendar, automatic live-feed reconnect, benchmark/sector context, SignalR application hub, outcome labeling, or production migration pipeline.
- Stale/offline/error/connecting feed health suspends publication through the application lifecycle gate, but reconnect/recovery and gap detection/backfill are not implemented.
- Setup quality is a deterministic 0–100 rules score, not a probability of profit.
- Azure deployment is not configured yet; Azure SQL Database remains the required deployment database.

## Decisions

See [docs/adr/](./docs/adr/) for render mode, persistence, in-process transport, event retention, and provider-selection decisions.
