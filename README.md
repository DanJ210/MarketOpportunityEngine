# MarketOpportunityEngine

Milestone A defaults to a deterministic recorded-fixture scanner and supports optional read-only live ingestion from Alpaca Market Data or Massive. It does not place orders or use a broker API.

**Current checkpoint: Milestone A.1 - Live Scanner Validation.** Feature expansion is stopped, including relative volume. See [current status](./PROJECT_STATUS.md) for verified live-source behavior and remaining exit criteria, and [validation history](./VALIDATION_HISTORY.md) for prior runs. Older slice recommendations below are historical, not authorization for new analytics.

## Requirements

- .NET 10 SDK
- Docker Desktop with the SQL Server container available for local app and SQL integration tests

## Fixture behavior

`src\MarketOpportunity.Web\Fixtures\momentum-continuation.json` contains synthetic trades and quotes for `MOE` and `QNT` in two explicit fixture sessions: `fixture-2026-10-22` and `fixture-2026-10-23`. Every event carries its event time, receive time, provider, sequence ID, symbol, and session ID.

The source preserves fixture arrival order, including interleaved symbols and late/out-of-order events. Every source must provide a non-empty symbol, provider, and explicit session ID; the scanner rejects events missing any of these identities rather than inferring them. The bounded state deduplicates stable identities and orders events deterministically by event time and identity before computing features. State and session VWAP are isolated by symbol and explicit session ID; no session is inferred from a UTC calendar date. The pipeline creates and persists one immutable feature/opportunity result for each of the four symbol/session pairs.

The `features-v11` snapshot retains session VWAP slope over the existing `MomentumWindowSeconds` window. At each distinct trade event time, the calculator samples cumulative session VWAP; slope is the change from the first to last in-window sample divided by elapsed event-time minutes, so irregularly spaced observations are time-normalized. The snapshot reports the signed price-per-minute value and `Rising`, `Falling`, `Flat`, or `Insufficient` direction. `Flat` means exactly zero slope; fewer than two distinct in-window timestamps with a positive cumulative volume yields a null slope and `Insufficient`. This is descriptive feature output only and does not affect setup classification, scoring, publication, or vetoes.

`features-v9` introduced fast/medium EMA values, their signed relationship, and event-time slopes, which remain in `features-v11`. `Scanner:EmaFastPeriod` defaults to **5 trades** and `Scanner:EmaMediumPeriod` to **20 trades**; both must be positive integers and the fast period must be strictly smaller than the medium period. These are observation counts, not elapsed-time periods. For every retained trade in the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`, in the existing canonical event order, the calculator ignores trade size and updates the EMA once per trade observation. Quotes, receive time, and wall-clock time do not contribute.

For period `N`, the first EMA is the arithmetic mean of the first `N` positive eligible trade prices (SMA seed); subsequent observations use `EMA = price * (2 / (N + 1)) + prior EMA * (1 - 2 / (N + 1))`, with decimal arithmetic. Fewer than `N` eligible observations yields null for that EMA. Any eligible trade price `<= 0` makes all EMA values, relationship, and slopes null, matching the strict retained-session extrema contract; invalid trades are not discarded. `FastMediumEmaSpreadPercent` is `(FastEma - MediumEma) / MediumEma * 100m` and is null until both EMAs are available. Slopes use the existing `MomentumWindowSeconds` (default **60 seconds**) and are signed price-per-minute changes between the first and last initialized EMA samples whose timestamps are in the inclusive interval `[AsOfUtc - MomentumWindowSeconds, AsOfUtc]`. At a duplicate timestamp, all trades update the EMA in canonical order and the timestamp sample is its last ordered EMA. Fewer than two distinct in-window timestamps yields a null slope; equal endpoints yield zero. Earlier eligible trades still warm up the EMA even when they precede the slope interval.

Read EMA values with `HasCompleteSessionData`: `false` means any non-null value may describe only retained partial state, and is not a complete-session indicator. A future event does not affect an earlier value/identity when retained eligible state and completeness are unchanged, but can evict eligible trades from bounded state and thereby change a later historical-as-of result. New fields are nullable JSON decimals (`fastEma`, `mediumEma`, `fastMediumEmaSpreadPercent`, `fastEmaSlopePerMinute`, `mediumEmaSlopePerMinute`) and participate explicitly in deterministic feature identity. They do not affect detector qualification, scoring, classifications, publication, vetoes, or the unchanged `momentum-continuation-v1` setup.

`features-v10` adds nullable `SessionPriceStructure` (`sessionPriceStructure`) as a descriptive retained-session value. A fixed, versioned two-sided fractal examines every eligible trade in the existing canonical order; it has no new configuration parameter or time-duration threshold. For each candidate at observation index `i`, a high pivot must be strictly greater than each of the previous two prices and greater than or equal to each of the next two; a low pivot must be strictly less than each previous two and less than or equal to each next two. The strict comparison on the left and inclusive comparison on the right select the earliest canonical observation on an equal-price plateau. Exact decimal comparisons are used; there is no tolerance. Each candidate is confirmed only after two later eligible trade observations are present. Its pivot time is the candidate trade's `EventTimeUtc`, not the later confirmation time. Equal event timestamps remain separate observations in canonical event order, so confirmation counts observations rather than elapsed time.

After confirmation, compare the latest two pivot highs and latest two pivot lows. If both latest pivots are higher, the enum is `HigherHighHigherLow`; if both are lower, it is `LowerHighLowerLow`; opposing directions yield `Mixed`; an equal high or low comparison yields `Flat`. `sessionPriceStructure` is null when any eligible trade price is non-positive or there are fewer than two confirmed highs or fewer than two confirmed lows. Eligibility is only retained trades for the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`; quotes and trade sizes (including zero/negative sizes) do not affect structure. Interpret any non-null value with `HasCompleteSessionData`: false means the pivots describe only retained partial state. Future events do not contribute unless their arrival evicts an eligible retained event, which can change a historical-as-of result.

The API uses the existing string-enum convention for non-null structure values; persisted snapshot JSON follows the existing default enum encoding. The field participates explicitly in deterministic feature identity, including when null. It is descriptive only and does not affect detector qualification, scoring, classification, publication, or vetoes. The structure slice advanced configuration and feature versions to `scanner-config-v10` and `features-v10`; the serialized configuration changes through `FeatureSetVersion`, and configuration snapshots remain immutable per version. The setup remains `momentum-continuation-v1`; prior persisted snapshots, configurations, and decisions are not rewritten.

`features-v11` adds descriptive trade-point ADX output: nullable decimal `SessionAdx` (`sessionAdx`) plus `SessionAdxStatus` (`sessionAdxStatus`). It is not bar-based ADX. The canonical events contain trades and quotes but no OHLC bars, so this calculation does not synthesize bars; each retained trade price is one observation. For consecutive prices `P[t-1]` and `P[t]`, `TR = abs(P[t] - P[t-1])`, `up = P[t] - P[t-1]`, and `down = P[t-1] - P[t]`. `+DM = up` only when `up > down` and `up > 0`; `-DM = down` only when `down > up` and `down > 0`; otherwise that movement is zero. Equal consecutive prices therefore contribute zero range and directional movement.

`Scanner:TrendStrengthPeriod` defaults to **14 trade movements** and must be a positive integer. For period `N`, the first `N` movements seed arithmetic means for TR, +DM and -DM. Later movement means use Wilder smoothing: `smoothed = prior + (current - prior) / N`. `+DI = 100 * smoothed(+DM) / smoothed(TR)` and `-DI = 100 * smoothed(-DM) / smoothed(TR)`. `DX = 100 * abs(plusDI - minusDI) / (plusDI + minusDI)`; a zero smoothed true range or zero DI sum defines DX as zero. The first ADX is the arithmetic mean of the first `N` DX values; later ADX values use the same Wilder recurrence. The result is a nondirectional decimal strength on a 0–100 scale, not a probability.

Eligibility is all retained trades for the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`, in the existing canonical event-time/provider/ordinal-sequence/canonical-identity order. There is no elapsed-time interval: older eligible retained trades still contribute. Equal event timestamps are separate observations. Quotes, sizes (including zero/negative), receive time and wall-clock time do not contribute. The first value is available at **2 × N eligible trade observations**: `N` movements seed the directional indices, then `N` DX observations seed ADX. No eligible trade or fewer than `2 × N` eligible observations returns `sessionAdx: null`, `Insufficient`; any eligible price `<= 0` returns `sessionAdx: null`, `InvalidPriceData` without discarding the invalid event. `Available` carries the non-null decimal. Read it with `HasCompleteSessionData`: `false` means a non-null value reflects only retained partial state; it does not itself null or otherwise change the status. A future event is excluded unless its arrival evicts eligible retained state, which can change a later historical-as-of value and completeness.

HTTP uses the existing string-enum convention for `sessionAdxStatus`; persisted snapshot JSON uses its existing numeric enum encoding. Both value and status—including null/insufficient and null/invalid cases—are explicit in deterministic feature identity. The period is part of serialized configuration. The snapshot/configuration versions advance to `features-v11` / `scanner-config-v11`; prior persisted configurations, snapshots and decisions are not rewritten. `momentum-continuation-v1` remains unchanged. ADX is descriptive only and does not affect detector qualification, scoring, classifications, publication or vetoes.

`features-v12` adds non-null `FeatureSnapshot.SessionTrendDirection` of type `TrendDirectionContext` (`sessionTrendDirection`) alongside the existing retained-session signals. It uses only `FastMediumEmaSpreadPercent`, `FastEmaSlopePerMinute`, `MediumEmaSlopePerMinute`, the exact signed `SessionVwapSlopePerMinute` / `SessionVwapSlopeDirection`, `SessionPriceStructure`, and the existing `SessionAdx` / `SessionAdxStatus`. Each positive EMA spread/slope votes bullish; each negative one votes bearish; zero is an available neutral signal. VWAP `Rising`/`Falling` votes bullish/bearish and `Flat` is neutral; `Insufficient` contributes no vote. `HigherHighHigherLow` / `LowerHighLowerLow` vote bullish/bearish; `Flat` is neutral; `Mixed` is a conflict.

The enum values are `Bullish`, `Bearish`, `Neutral`, `Conflicting`, `Insufficient`, and `Invalid`. Any invalid input has precedence: a non-positive non-null EMA, inconsistent EMA availability/spread/slope, a VWAP direction that does not match its signed slope (including an unknown enum), an unknown price-structure enum, or inconsistent/out-of-range ADX state produces `Invalid`; the existing ADX `InvalidPriceData` state also produces `Invalid`. Otherwise any opposing bullish and bearish votes, or `Mixed` price structure, produces `Conflicting`; a single direction produces `Bullish` or `Bearish`; available directional values that are all zero/flat produce `Neutral`; no available directional signals produces `Insufficient`. Missing EMA/slope values, VWAP `Insufficient`, null structure, and ADX `Insufficient` are not votes. ADX is validated and retained as its separate 0–100 nondirectional strength/status; its magnitude never casts a direction vote.

This is a fixed rule, so no new configuration parameter is needed. The output is always a non-null enum: HTTP uses the existing string-enum convention, while persisted snapshot JSON uses the default numeric enum encoding. It is included explicitly in deterministic feature identity for every state. Like the inputs, it is computed only from this symbol's explicit-session feature snapshot at its `AsOfUtc`; `HasCompleteSessionData=false` does not alter or suppress a result, but means any direction is based on retained partial-session features and must not be described as complete-session context. The defaults advance to `features-v12` / `scanner-config-v12`; the setup remains `momentum-continuation-v1`. Earlier persisted configurations, snapshots, and decisions remain unchanged. This context does not affect detector qualification, score, classification, evidence, contradictions, publication, or vetoes.

`features-v13` adds a descriptive, retained-session trade-point realized-volatility value: nullable decimal `SessionRealizedVolatilityPercent` (`sessionRealizedVolatilityPercent`) and non-null `RealizedVolatilityStatus` (`sessionRealizedVolatilityStatus`). It is not bar-based ATR and does not synthesize OHLC bars. For canonical ordered eligible trade prices `P[0..n-1]`, use every consecutive pair to compute `r[i] = ln(P[i] / P[i-1])`; the output is `100 * sqrt(sum(r[i]^2))`, unannualized and expressed as percent points. It is not divided by the number of movements or normalized by elapsed time. `Math.Log`/`Math.Sqrt` use double precision; the result is converted to decimal and rounded to **9 fractional decimal places, midpoint-to-even**.

Eligible observations are all retained trades for the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`, including older events outside the ROC windows. The existing deterministic order (event time, provider, ordinal sequence ID, canonical identity) governs consecutive pairs; equal-time trades remain separate observations. Existing first-occurrence duplicate handling is unchanged. Quotes, trade sizes (including zero/negative), receive time and wall-clock time do not contribute. The contract has no missing trade-price representation: an empty eligible window yields null/`Insufficient`; one positive price yields null/`Insufficient`; any eligible price `<= 0` (including a mixed valid/invalid set or a single invalid price) yields null/`InvalidPriceData`; two or more valid prices yield a numeric value and `Available`. Repeated equal prices yield the neutral numeric value **0**, not null.

Interpret available results with `HasCompleteSessionData`: false means the value uses only retained partial-session observations and does not claim complete-session volatility; it does not change the status. Eviction can reduce a partial result to insufficient. Future events are excluded unless their arrival evicts an eligible retained event, which may change a later historical-as-of result and completeness. The HTTP enum is serialized as a string; persisted snapshot JSON uses the default numeric enum encoding. Both value (including null) and status participate explicitly in deterministic feature identity. The descriptive feature itself does not change the formula or `features-v13` contract.

The separate `scanner-config-v14` risk gate uses `Scanner:MaximumSessionRealizedVolatilityPercent`, default **1.00 percentage point** (`1.00` means 1%). It must be a positive decimal; no unsupported upper cap is imposed. For every evaluated detector snapshot with `HasCompleteSessionData == true` and a consistent `Available` status/non-negative value, `value >= threshold` adds the hard veto `ExcessiveRealizedVolatility`; values below it do not add this veto. This 1.00 cutoff is an explicitly provisional risk-tolerance choice, not statistically market-calibrated by synthetic fixtures. An `Insufficient`/null result adds `VolatilityUnavailable`; `InvalidPriceData`, unknown statuses, or inconsistent/missing value/status combinations add `InvalidVolatilityData`. For partial retained-session data, a valid available value is not compared with the threshold; the existing `InsufficientRequiredFeatureData` hard veto remains authoritative. Prior configuration snapshots remain immutable: changing the threshold requires a new configuration version. `features-v13` and `momentum-continuation-v1` stay unchanged; the detector adds only these explicit veto outcomes, without changing volatility calculation, scoring, or other feature calculations.

`features-v14` adds nullable decimal `ShortHorizonDollarVolumeUsd` (`shortHorizonDollarVolumeUsd`) and the dedicated `ShortHorizonDollarVolumeStatus` (`shortHorizonDollarVolumeStatus`: `Available`, `Insufficient`, or `InvalidTradeData`). Dollar volume is the decimal sum of `TradeEvent.Price * TradeEvent.Size`, in USD, for eligible trades in the inclusive event-time interval `[AsOfUtc - MomentumWindowSeconds, AsOfUtc]`; the existing configured primary momentum window is the only duration setting. Eligibility is limited to the retained same-symbol, explicit-session snapshot at `AsOfUtc`, using its canonical event order and first-occurrence duplicate handling. Quotes, receive time, wall-clock time, other symbols/sessions and future events do not contribute. Decimal multiplication and summation overflow is surfaced rather than caught or clamped.

No eligible window trades yields null/`Insufficient`. Any eligible trade with `Price <= 0` or `Size <= 0` yields null/`InvalidTradeData`; the invalid observation is never dropped to publish a partial sum. Positive inputs produce the numeric sum and `Available`, including one trade. Interpret the value with `HasCompleteSessionData`: a partial-retention value only describes retained observations and is not compared with the full-window minimum; the detector's existing incomplete-session veto remains, and retained state does not establish upstream/session-start completeness. Both value (including null) and status (including status-only changes) participate explicitly in deterministic feature identity. HTTP uses the existing string-enum convention; persisted snapshot JSON uses the default numeric enum encoding.

`scanner-config-v15` adds positive decimal `Scanner:MinimumShortHorizonDollarVolumeUsd`, default **100,000 USD**. On every Momentum Continuation evaluation, a complete snapshot with a consistent `Available` non-negative value is vetoed with `InsufficientDollarVolume` only when `value < minimum`; equality passes. `Insufficient`/null adds `DollarVolumeUnavailable`; `InvalidTradeData`, unknown statuses, negative available values and all inconsistent value/status combinations add `InvalidDollarVolumeData`, including for partial state. Partial `Available` values skip only the numeric comparison. The cutoff is an explicitly provisional risk-tolerance choice, **not market-calibrated**; synthetic fixture results are regression expectations, not cutoff calibration or profitability evidence. The threshold is part of serialized configuration, so the existing immutable same-version persistence check rejects changing it without a new configuration version. The setup remains `momentum-continuation-v1`; scores, weights, feature calculations other than the new output, and unrelated detector behavior remain unchanged. No schema migration is required because the feature snapshot and configuration are serialized JSON.

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

The preceding range-position slice advanced defaults from `features-v7` / `scanner-config-v7` to **`features-v8` / `scanner-config-v8`**. The EMA slice advanced them to **`scanner-config-v9` / `features-v9`**, the structure slice to **`scanner-config-v10` / `features-v10`**, the trade-point ADX slice to **`scanner-config-v11` / `features-v11`**, trend direction to **`scanner-config-v12` / `features-v12`**, and retained-session realized volatility to **`scanner-config-v13` / `features-v13`**. The separate volatility-risk gate advanced only configuration to **`scanner-config-v14`**. The dollar-volume feature/gate now advances defaults to **`features-v14` / `scanner-config-v15`**; `momentum-continuation-v1` remains unchanged. Prior persisted configurations, snapshots, and decisions remain immutable; changing analysis creates a new version/record. Session volume's empty/invalid-size/checked-overflow/completeness contract, rolling volume, both ROC horizons and validation, exact VWAP-slope behavior, and unrelated detector outcomes remain unchanged. No schema migration, dependency or UI change is part of this slice.

## Local run

From the repository root, start SQL Server and the app:

```powershell
docker compose up -d sqlserver
dotnet run --project src\MarketOpportunity.Web --launch-profile http
```

**SQL Server data persistence**

[Docker Compose](./docker-compose.yml) mounts the named volume `market-opportunity-sql-data` at `/var/opt/mssql`. All databases in this SQL Server instance, including the development database and `MarketOpportunityEngine_LiveValidationTests`, persist there. The `sqlserver-test-init` service is only a database-initialization client; it uses the same SQL Server and does not need its own volume.

Data survives container restarts, recreation, and `docker compose down`. Run `docker compose up -d sqlserver` to reuse it. **Do not run `docker compose down --volumes` (or `down -v`) unless you intend to delete the database volume and its data.** A volume is not a backup.

Compose normally prefixes the volume name with the project name (currently `marketopportunityengine_market-opportunity-sql-data`). Keep the same Compose project name when restarting; changing it selects a different volume rather than migrating existing data.

The `http` launch profile in `src\MarketOpportunity.Web\Properties\launchSettings.json` sets the development environment, opens the browser, and configures the app to listen at `http://localhost:5228`. You do not need to set `ASPNETCORE_URLS` yourself. Use the `https` profile instead if you want HTTPS and have the local development certificate trusted.

Development startup creates the application schema with EF Core `EnsureCreated`; if an earlier schema already exists, it applies the explicit-session and opportunity-provenance migrations. Pre-migration records are labeled `legacy-unknown` rather than assigned an inferred session. The current session-scoped feature contract is versioned as `features-v14`, and the effective scanner configuration as `scanner-config-v15`; prior immutable decisions are not rewritten. The development connection string targets SQL Server at `localhost,1433`. The checked-in password is only for local Docker development; override `MSSQL_SA_PASSWORD` when needed and keep credentials out of source control.

In a second PowerShell terminal, verify the running app:

```powershell
Invoke-RestMethod http://localhost:5228/health
Invoke-RestMethod http://localhost:5228/api/v1/system/status
Invoke-RestMethod http://localhost:5228/api/v1/opportunities
Invoke-RestMethod "http://localhost:5228/api/v1/symbols/MOE/snapshot?sessionId=fixture-2026-10-22"
```

The dashboard at `http://localhost:5228/` shows all symbol/session opportunities and keeps classification, publication lifecycle/reason, provider/feed/coverage, score, event-time freshness, evidence, contradictions, vetoes, invalidation, expiry, and feature/setup/configuration versions visible. Publication state is a current application read model; stale, offline, connecting, or errored source health suspends a candidate without overwriting the immutable analytical evidence. `GET /api/v1/opportunities` returns opportunity plus publication state; the snapshot endpoint returns the latest snapshot for a symbol or a specific session when `sessionId` is supplied.

## Provider selection and configuration precedence

`MarketData:Provider` is the single provider selector: `Fixture`, `Alpaca`, or `Massive`. The app reads its effective value through ASP.NET Core configuration at startup; adding a provider's credentials does not select that provider. Stop and restart the app after changing selection, feed, symbols, or session settings.

Keep `"MarketData": { "Provider": "Fixture" }` in the shared [appsettings.json](./src/MarketOpportunity.Web/appsettings.json) so a clean checkout defaults to offline replay. Select a live provider intentionally through local Development User Secrets rather than changing the shared default.

For the current application, configuration precedence is **highest first**, applied per key:

1. Command-line arguments, such as `--MarketData:Provider Alpaca`.
2. Environment variables, such as `MarketData__Provider=Alpaca` (double underscores represent nested keys).
3. .NET User Secrets, loaded in **Development**.
4. `appsettings.{Environment}.json`, such as `appsettings.Development.json`.
5. Shared `appsettings.json`.

The `http` and `https` launch profiles select Development but do not select a provider. Environment variables supplied by a launch profile or IDE participate in the environment-variable layer. A higher-priority `Massive` selection will override `Fixture` or `Alpaca` in a JSON file.

### Select Alpaca for normal local launches

From the repository root, with the Alpaca key ID and secret already configured using the [credential setup guide](./docs/providers/alpaca-websocket.md#local-configuration), set these **non-secret** values:

```powershell
dotnet user-secrets set "MarketData:Provider" "Alpaca" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:Feed" "test" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:Symbols" "FAKEPACA" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Alpaca:SessionId" "alpaca-manual-validation" --project src\MarketOpportunity.Web
dotnet run --project src\MarketOpportunity.Web --launch-profile http
```

These settings persist locally outside the repository. Use an explicit session ID appropriate to the run; it is not inferred from the date. `test` / `FAKEPACA` is synthetic Alpaca traffic, not exchange market data. For Milestone A.1 IEX validation, use the [dedicated validation commands](#milestone-a1-live-iex-validation) below to reuse the existing configured symbol universe rather than maintaining a second list. IEX is limited/non-consolidated coverage and may produce no events outside market availability. A connection alone does not establish Live health.

### Override selection for one run

To force Alpaca's test stream without changing persistent selection:

```powershell
dotnet run --project src\MarketOpportunity.Web --launch-profile http -- --MarketData:Provider Alpaca --MarketData:Alpaca:Feed test --MarketData:Alpaca:Symbols FAKEPACA --MarketData:Alpaca:SessionId alpaca-manual-validation
```

Arguments after `--` are passed to the application. This is how provider selection was forced during Milestone A.1 validation; it does **not** make a subsequent normal launch use Alpaca. This command also does **not** change the SQL connection: normal launches use the effective `ConnectionStrings:MarketOpportunity`. Dedicated-test validation requires the separate [guarded SQL procedure](#fixture-runtime-verification-order); do not assume a provider override selects a test database.

### Milestone A.1 live IEX validation

Run these commands from the repository root in **PowerShell 7.6+** (`pwsh`, running .NET 10), with Docker Desktop running. Alpaca credentials must already be configured as described in the [provider guide](./docs/providers/alpaca-websocket.md#local-configuration). Neither command changes User Secrets, analytics, thresholds, or the offline default.

**Prepare the dedicated database (safe to repeat):**

```powershell
docker compose up -d --wait sqlserver
docker compose run --rm --no-deps sqlserver-test-init
if ($LASTEXITCODE -ne 0) { throw 'Validation database initialization failed.' }
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 -Feed iex -PreflightOnly
if ($LASTEXITCODE -ne 0) { throw 'Validation preflight failed.' }
```

The opt-in `sqlserver-test-init` Compose service creates **only** `MarketOpportunityEngine_LiveValidationTests` if missing on the existing local SQL Server. It does not drop/reset data, change the development catalog, or need a second SQL Server container. Existing rows and volumes are preserved. If the SQL service uses a custom password, set `MSSQL_SA_PASSWORD` securely in your terminal before invoking Compose so its initializer uses that same password; never recreate a persisted SQL instance with a different assumed password.

The launcher builds the Web project and verifies `DB_NAME()` with SQL Server before starting anything. By default it reads the running Compose SQL container's password into process memory only. If `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` is already set, it must target **exactly** `localhost,1433` / `MarketOpportunityEngine_LiveValidationTests`; an invalid/different target is rejected, not replaced. The explicit test-connection environment override reaches the application without putting credentials in command arguments. Development startup creates/migrates the schema inside that test database. Preflight does **not** open an Alpaca connection or create the application schema.

**Connect to actual exchange data:**

```powershell
$sessionId = 'a1-iex-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 -Feed iex -SessionId $sessionId
```

`-Feed iex` selects **`wss://stream.data.alpaca.markets/v2/iex`**, with `Alpaca / iex / Limited` provenance. It reads only the existing `MarketData:Massive:Symbols` universe from Web-project User Secrets and passes its non-empty, distinct symbols as a one-run Alpaca subscription setting. It does not print unrelated secrets, require re-entry, save another symbol list, connect to Massive, or couple analytics to that configuration key. Configured-entry and distinct-symbol counts are printed; these are **not** subscription acknowledgements or proof of activity. Not every symbol must produce events.

Run during a normal **09:30-16:00 America/New_York** equity session, excluding holidays and accounting for early closes. The launcher does not enforce an exchange calendar or delay execution until open. Observe a bounded **5-10 minute** initial run; this is operational validation, not a full-session soak or a profitability test.

**Synthetic alternative (not Milestone A.1 exchange evidence):**

```powershell
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 -Feed test
```

`-Feed test` selects **`wss://stream.data.alpaca.markets/v2/test`**, subscribes `FAKEPACA`, and reports `Alpaca / test / Synthetic`. It uses the same dedicated validation database but a new session ID. Stop one run before starting the other; the port and provider connection allowance may already be in use.

**Observe the running app in a second terminal:**

```powershell
Start-Process 'http://localhost:5291/'
Invoke-RestMethod http://localhost:5291/health
$before = Invoke-RestMethod http://localhost:5291/api/v1/system/status
Start-Sleep -Seconds 30
$after = Invoke-RestMethod http://localhost:5291/api/v1/system/status
$before, $after | Format-List
Invoke-RestMethod http://localhost:5291/api/v1/opportunities | ConvertTo-Json -Depth 20
```

Check increasing **unique canonical** event counts, event/receive timestamps, truthful feed health and gap state, and existing feature/opportunity/publication results. In the browser, confirm values change **without refreshing** and provenance is `Alpaca / iex / Limited`. `Avoid / No Trade` is acceptable; do not change thresholds to force a candidate. `/health` HTTP 200 alone does not prove a healthy feed. Normal mode does not expose message/parser/reconnect counters. The opt-in instrumentation below records complete messages (not transport frames), subscription acknowledgement symbol lists, and connection lifecycle; symbol-specific provider rejection details are still not retained.

**Read exact-session persistence before and after normal shutdown:**

In the second terminal, set `$sessionId` to the **SessionId printed by the launcher**, not a new value. The following query is read-only and refuses other database names:

```powershell
$sessionId = '<paste the printed SessionId>'
if ($sessionId -notmatch '^[A-Za-z0-9_-]{1,64}$') { throw 'Invalid session ID.' }
Get-Content .\scripts\Read-AlpacaValidation.sql -Raw |
    docker compose exec -T sqlserver bash -ec 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d MarketOpportunityEngine_LiveValidationTests -v SessionId="$1"' -- $sessionId
if ($LASTEXITCODE -ne 0) { throw 'Validation persistence query failed.' }
```

Record final status (`Invoke-RestMethod .../api/v1/system/status`), persisted counts per symbol, provenance, warnings/errors, and elapsed run time **before** stopping. Then press **Ctrl+C in the app terminal** for normal host shutdown, wait for exit, and repeat the SQL query. The scanner stops input, completes the channel, and drains queued and pending analysis/persistence work within `Operations:GracefulShutdownTimeoutSeconds` (20 seconds by default; validated range 1-300). The host shutdown deadline allows an additional 10 seconds for hosted-service reporting. A timeout or processing/persistence failure is reported with produced/accepted/dequeued/duplicate/analyzed/acknowledged/failed/cancelled/remaining counts; it is not reported as a successful drain. Compare the diagnostic accounting with exact-session SQL identities. Do not use `Stop-Process` or `docker compose down -v` to end this run. The launcher restores its process environment on exit; persistent provider settings and the default development connection are unchanged.

#### One final instrumented A.1 observation

See [final checkpoint evidence and decision](./FINAL_A1_VALIDATION.md) and [the shutdown correction report](./A1_SHUTDOWN_FIX.md). Instrumentation does not authorize analytical changes. Run only during market availability; an after-hours empty connection or the synthetic test feed does not replace the required IEX observation.

```powershell
$sessionId = 'a1-final-iex-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$evidence = Join-Path $env:LOCALAPPDATA "MarketOpportunityEngine\Validation\$sessionId"
$artifacts = Join-Path $env:LOCALAPPDATA 'MarketOpportunityEngine\ValidationBuild'
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 `
    -Feed iex -SessionId $sessionId -Instrument -DurationSeconds 300 `
    -DiagnosticsDirectory $evidence -ArtifactsPath $artifacts -Port 5292
```

The alternate port/output path avoids overwriting a running development host; it does **not** make a second Alpaca connection safe if the existing host already uses the account's feed allowance. Coordinate ownership before starting; never stop an unrelated process. The existing `MarketData:Massive:Symbols` universe remains the launcher input. Moving it to `Scanner:Universe:Symbols` is explicitly post-A.1 housekeeping.

`-Instrument` is off by default. It sets only one-run diagnostic options, writes a sanitized `<session>.clock.json` before startup, and uses the same dedicated SQL guard. `w32tm /query /status /verbose` is read-only, time-bounded, and does not synchronize the clock or request elevation. Missing/failed clock status is explicit; time-service status alone is not an authoritative UTC offset/error bound. The app requests normal host shutdown after the bounded duration (five-second timer granularity); the scanner then applies the configured graceful-drain budget.

During the run:

```powershell
Invoke-RestMethod http://localhost:5292/api/v1/system/diagnostics |
    ConvertTo-Json -Depth 20
```

This development-only endpoint and the atomically replaced `<session>.diagnostics.json` (every five seconds and after scanner shutdown) expose:

- Complete WebSocket messages including control messages, market-data-bearing messages, canonical trades/quotes produced, parser failures, connection attempts/reconnects, lifecycle transitions, and acknowledged trade/quote subscription lists. A connection attempt may fail before opening a socket; `Live`/`Reconnected` means a canonical event was produced, not that its prices passed analytical quote validity.
- Accepted channel writes, dequeued/duplicate events, newly pending inputs presented to analysis, events acknowledged by successful persistence calls, persistence failures/batches, asynchronously waiting channel writes, cancelled writes, sampled depth/high-water mark, and longest observed wait. Acknowledged event count is **not a SQL INSERT count**; use the exact-session SQL query independently. Analyzed inputs can include events subsequently outside the retained/as-of calculation.
- Timing count/min/max and **bounded-histogram median/p95 intervals**, not fabricated exact percentiles: provider event -> complete-message receipt; receipt -> canonical/metadata creation; creation -> analysis start; analysis start -> persistence completion; persistence batch duration; asynchronous channel-write wait. Units are milliseconds. Provider lag is signed UTC-clock difference; within-process stages use monotonic `TimeProvider` timestamps. Empty histograms have null statistics.
- Connection-scoped metadata groups for quote bid/ask exchange, condition array (including whether supplied), tape, event counts, zero-ask counts, and first/last zero-ask samples with canonical identity and all three UTC timestamps. Trade conditions/tape are captured too. This is diagnostic sidecar data, **not additional canonical/analytical fields or SQL columns**.

Bounds are fixed: 256 metadata groups across connections/symbols/types/metadata, 256 lifecycle records, 16 conditions per event, 64 characters per text field. Sanitized/truncated/malformed optional metadata is flagged `incomplete`; omitted group-event attribution and dropped transitions have explicit counters. Aggregates remain accurate when samples are capped, but metadata coverage is then incomplete. Structured timing/backpressure/persistence anomaly logs are limited to one per category per minute; no raw messages or credentials are logged. These diagnostic reporting cutoffs do not change trading thresholds or source recovery behavior.

`MessageReceivedTimeUtc` is sampled after the final fragment completes, before UTF-8/JSON decoding. It measures **application completion of the receive call**, not NIC/kernel arrival or provider send time; data buffered while the app was not reading can still look "pre-arrival." `ParsedTimeUtc` is after canonical construction and diagnostic metadata extraction, before yield. Existing `ReceivedTimeUtc` remains the per-item pre-parser stamp and still participates in existing quote identities. Ordering, duplicate handling, formulas, zero-ask rules, and SQL persistence are unchanged.

Keep the final diagnostic JSON, clock JSON, sanitized console log, exact-session SQL results before/after shutdown, and browser observations together. Compare accepted minus duplicates with persistence acknowledgements and SQL identities; report unpersisted/in-flight work explicitly. Treat a drain as successful only when `shutdownDrainStatus` is `Completed`, `remainingEvents` is zero, and accepted work reconciles as duplicates plus persistence acknowledgements. Channel depth zero or process exit alone does not establish a successful drain.

### Verify or troubleshoot selection

Open `http://localhost:5228/` and inspect provider/feed/coverage, or request:

```powershell
Invoke-RestMethod http://localhost:5228/api/v1/system/status
```

If a launch reports a Massive WebSocket plan restriction when Alpaca was intended, that launch selected Massive; Alpaca credentials do not override the selector. Check application arguments, IDE/launch-profile environment settings, `MarketData__Provider`, and the local User Secrets selector in precedence order. Inspect only the relevant non-secret settings; do not dump User Secrets or connection strings into logs/chat. Use the one-run command above to explicitly select Alpaca, then remove or correct the unintended override for future normal launches. The app does not automatically switch providers or feeds on an entitlement failure.

To restore offline mode locally:

```powershell
dotnet user-secrets set "MarketData:Provider" "Fixture" --project src\MarketOpportunity.Web
```

Restart without a higher-priority live-provider override. Credentials may remain in User Secrets; they are unused in fixture mode. Never put API keys or secrets in checked-in settings, command-line examples, or browser code.

## Optional Alpaca Market Data

Alpaca is available for development/live-stream pipeline testing, including its always-available synthetic `test` feed and `FAKEPACA` symbol. This is market-data-only: the adapter does not use Alpaca's Broker API or place orders. Configure credentials through .NET User Secrets, never `appsettings.json` or browser code. See [docs/providers/alpaca-websocket.md](./docs/providers/alpaca-websocket.md) for setup, feed coverage, and the separately invoked opt-in connectivity test.

The default provider remains `Fixture`. To select Alpaca's test stream, configure a non-empty explicit session ID and User Secrets values for `MarketData:Alpaca:ApiKey` and `MarketData:Alpaca:SecretKey`; set `MarketData:Provider` to `Alpaca`. `MarketData:Alpaca:Feed` defaults to `test`, and `Symbols` defaults to `FAKEPACA`. `iex`, `sip`, and `delayed_sip` feeds are also represented in capability metadata; confirm subscription entitlements before selecting live-market feeds. IEX is limited coverage, SIP is consolidated, and delayed SIP is 15 minutes delayed.

To explicitly run the separate Alpaca test-stream connectivity check, configure the credentials and test-feed settings as described in [docs/providers/alpaca-websocket.md](./docs/providers/alpaca-websocket.md), then run:

```powershell
dotnet test tests\MarketOpportunity.AlpacaConnectivityTests\MarketOpportunity.AlpacaConnectivityTests.csproj
```

This project is intentionally outside the solution and default offline unit-test run. It skips when credentials are absent; when configured, it makes a network connection to Alpaca's synthetic test feed. It does not use brokerage endpoints or place orders.

## Optional live market data (Massive)

The recorded fixture remains the default. Massive remains a supported live market-data provider, and Alpaca is the current development/live-stream testing alternative; neither is an execution integration. Live mode consumes trade and quote WebSocket events and analyzes/persists changed symbol/session state at most once every five seconds. The status API and dashboard show the latest market-event/receive times and receive age; the feed changes from `Live` to `Stale` when no event arrives within `Scanner:MaximumLiveFeedStalenessSeconds` (30 seconds by default). Receive freshness is distinct from event-time analysis. Alpaca retries failed/disconnected streams after the bounded `MarketData:Recovery:ReconnectDelayMilliseconds` delay (default 5000 ms, allowed range 10-300000 ms); it does not backfill gaps, and publication remains suspended after a disconnect because data integrity is unresolved. Massive does not yet reconnect. Both live adapters start cold without historical warm-up.

For local development, store the API key in .NET User Secrets rather than a checked-in file or shell command history. User Secrets keep the value outside the repository but are not encrypted; use a protected local account and never treat them as a production secret store. The provider, watchlist, session label, API key, and SQL connection string are read through `IConfiguration`, so deployment can later supply the same settings from Azure Key Vault. The configured session ID is applied verbatim; there is no exchange-calendar lookup or date-based inference. Change it explicitly when starting a different session.

```powershell
dotnet user-secrets set "MarketData:Provider" "Massive" --project src\MarketOpportunity.Web
dotnet user-secrets set "MarketData:Massive:Symbols" "AAPL,MSFT,SPY,NOK,AAPL,GOOGL,AMZN,TSLA" --project src\MarketOpportunity.Web
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

Before running any SQL command, check whether `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` is configured without printing its value. Validate it with `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`: `InitialCatalog` must be non-empty and contain `test` (case-insensitive), and the database must be dedicated to testing. A matching name alone does not establish that it is disposable. If a configured value is invalid or targets a non-test database, stop without replacing it. If the variable is absent, only reuse an already-running, healthy local Compose SQL Server and an already-existing dedicated test database; otherwise stop. Never start a server, create a database during the guard, retarget development data, or paste credentials into chat/logs.

An absent variable does not require another SQL Server instance: with operator approval, reuse only the already-running healthy Compose `sqlserver` service and the dedicated `MarketOpportunityEngine_IntegrationTests` database if it already exists. Verify the catalog using a read-only query before launching tests. The tests may create/migrate the schema within that dedicated database; migration tests use uniquely named temporary databases. Build any guarded connection in process memory from local SQL credentials without displaying or checking them in; preserve the existing container and fixture rows.

After that safety check, use this order. Repeat the hosted test against the **same database**, without deleting/resetting fixture records, to check replay-safe assertions:

```powershell
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj
dotnet build MarketOpportunityEngine.sln
```

### Current verification: configured minimum dollar-volume veto (2026-10-27)

The current policy adds `ShortHorizonDollarVolumeUsd` and `ShortHorizonDollarVolumeStatus` in `features-v14`, and the positive-decimal `MinimumShortHorizonDollarVolumeUsd` setting (default **100,000 USD**) in `scanner-config-v15`; `momentum-continuation-v1` is unchanged. The value is the decimal sum of `TradeEvent.Price * TradeEvent.Size` in USD for retained, canonically ordered, eligible trades in `[AsOfUtc - MomentumWindowSeconds, AsOfUtc]`. It uses same-symbol, explicit-session event-time state; quotes, receive time, wall-clock time, other symbols/sessions and future events do not contribute. Decimal overflow is not caught or clamped.

An empty window yields null/`Insufficient`; any eligible non-positive price or size yields null/`InvalidTradeData`, without dropping invalid observations from a partial sum. Positive inputs yield an available numeric sum, including one trade. `HasCompleteSessionData=false` marks the sum as retained-only and prevents comparison with the full-window minimum, while the existing incomplete-session veto remains. On every detector evaluation, complete/consistent available non-negative values add `InsufficientDollarVolume` only when strictly below the configured minimum; equality passes. Insufficient/null adds `DollarVolumeUnavailable`; invalid trade data, unknown statuses, negative available values and inconsistent value/status combinations add `InvalidDollarVolumeData`, even for partial state. Existing unrelated vetoes are preserved.

The $100,000 cutoff is explicitly provisional risk tolerance, **not market-calibrated**. The synthetic fixture values below are independent event-time regression expectations only; they are not calibration, liquidity-quality or profitability evidence.

| Fixture symbol/session | Eligible ordered trades summarized in test | Decimal notional in 60-second window |
|---|---|---:|
| MOE / fixture-2026-10-22 | 149.90×1200, 150.08×900, 150.23×1400, 150.40×300, 150.48×1800 | $841,258 |
| MOE / fixture-2026-10-23 | 150.50×1250, 150.30×900, 150.08×1450, 149.90×1850 | $818,326 |
| QNT / fixture-2026-10-22 | 80.40×1100, 80.20×950, 80.08×1300, 79.90×1700 | $404,564 |
| QNT / fixture-2026-10-23 | 79.80×1150, 80.00×1000, 80.20×1400, 80.40×350, 80.48×1750 | $453,030 |

Both new fields, including null and status, participate in deterministic feature identity and persisted JSON. The new threshold is serialized configuration; changing it under the same version is rejected by the existing immutable-configuration persistence check. No database migration or schema change was needed.

**Passing validation, in requested order:**

| Order | Exact command | Result |
|---|---|---|
| Focused offline | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter "FullyQualifiedName~ScannerReplayTests|FullyQualifiedName~MomentumContinuationTests"` | **156 passed, 0 failed, 0 skipped** |
| Full offline units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **208 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |
| SQL persistence/migrations | Guarded SQL integration commands | **Not run / unavailable**: `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was unset; no SQL connection, service operation, or database test was attempted. |

**Failed attempt, not passing evidence:** The first focused invocation stopped during compilation (0 tests executed) because one positional snapshot fixture lacked the new arguments and a heterogeneous test-event switch needed an explicit base type. Those test-only compile issues were corrected; the focused rerun and subsequent full unit suite passed. Hosted fixture, browser, live-provider, User Secrets and Azure validation were not run.

**Superseded pre-A.1 recommendation (not current authorization):** Approve a historical-volume source and its session, coverage, retention, and completeness contract before implementing relative volume. The runtime has no historical read path; persisted events are write-only from the scanner. The existing retained-session cumulative volume and short-horizon dollar volume are not relative-volume baselines, and synthetic fixture sessions are not calibration data. No relative-volume feature or version change is implemented. Current work is restricted to live scanner validation.

### Historical verification: validated realized-volatility risk veto (2026-10-26)

The preceding `scanner-config-v14` gate used the unchanged `features-v13` trade-point realized-volatility value and a positive decimal maximum defaulting to **1.00 percentage point**. Complete consistent available values at or above the maximum add `ExcessiveRealizedVolatility`; partial values skip comparison and retain the incomplete-session veto. Its cutoff was explicitly provisional, not market-calibrated. At that checkpoint, the focused suite passed **145/145**, offline units **197/197**, and the solution build had **0 warnings and 0 errors**. SQL was not run because the guarded environment variable was absent. The historical failed-attempt record and command list follow in the prior analysis notes.

### Historical verification: descriptive retained-session trade-point realized volatility (2026-10-26)

The versioned descriptive volatility output was implemented without changing `momentum-continuation-v1` or detector/opportunity output at that time. Focused replay passed **129/129**; the full offline unit suite passed **193/193** before and after SQL; hosted fixture HTTP/SQL passed **1/1 on each of two consecutive runs** against the same database without reset; persistence/migration tests passed **3/3**; and the solution build succeeded with **0 warnings and 0 errors**.

Fixture expectations were independently derived from event-time-ordered scalar trade prices, not fixture arrival order:

| Symbol/session | Ordered eligible trade prices | HTTP percent / status | Persisted status |
|---|---|---|---:|
| MOE / fixture-2026-10-22 | 149.90, 150.08, 150.23, 150.40, 150.48 | 0.199999477 / Available | 0 |
| MOE / fixture-2026-10-23 | 150.50, 150.30, 150.08, 149.90 | 0.231391437 / Available | 0 |
| QNT / fixture-2026-10-22 | 80.40, 80.20, 80.08, 79.90 | 0.367550138 / Available | 0 |
| QNT / fixture-2026-10-23 | 79.80, 80.00, 80.20, 80.40, 80.48 | 0.443762951 / Available | 0 |

The hosted test asserts each exact value and HTTP string status in all four snapshots and raw persisted SQL JSON (numeric `Available` enum value `0`), alongside existing provenance, completeness, ID, canonical-event, negative-lookup and opportunity checks. Persistence coverage confirms changed observations produce a new immutable feature/opportunity record and do not rewrite the original snapshot JSON or decision.

**Historical passing commands/results for the descriptive feature slice (all test runs: 0 failures/0 skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **129 passed, 0 failed, 0 skipped** |
| Offline full, before SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **193 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command, same database without reset | **1 passed, 0 failed, 0 skipped** |
| Persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Offline full, after SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **193 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

`MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. The already-running healthy Compose SQL Server and its pre-existing `MarketOpportunityEngine_IntegrationTests` database were reused; a read-only catalog query confirmed the database existed. Each integration test's .NET 10 `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` guard validated a non-empty test catalog before SQL operations. The fallback connection was held only in process memory and cleared in `finally`; the PowerShell process launching `dotnet` did not load SqlClient. No service was started/stopped/recreated, fixture rows or database were reset/deleted, or configured database dropped. Migration tests used only uniquely named temporary databases.

**Failed attempts, not passing evidence:** The initial focused run stopped at compilation because a directly affected positional `FeatureSnapshot` test fixture lacked the new arguments; the next run had 124 passed / 5 failed, including rounded expected values and prior identity tests that needed to normalize the newly varied feature. Those fixtures were corrected and focused replay passed 129/129. An initial hosted assertion used fixture arrival order for the two late trades; the corrected independently derived event-time expectations pass in both no-reset hosted runs. A PowerShell-side SqlClient builder attempt failed with `PlatformNotSupportedException` before connecting; an initial fallback string-escaping attempt then failed login before database work. The .NET 10 integration-test guards and corrected process-only connection succeeded. Browser, live-provider, User Secrets, and Azure checks were not run.

**Historical next-slice recommendation at that checkpoint:** Define a validated volatility-risk threshold and explicit hard-veto behavior, including the treatment of `Insufficient`, `InvalidPriceData`, and partial retained-session results. This was later implemented as the separate `scanner-config-v14` gate documented above. Full Milestone A remains incomplete.

### Historical verification: retained-session trend-direction slice (2026-10-26)

The earlier trend-direction version passed focused replay **123/123**, offline units **187/187**, hosted fixture **1/1 twice**, persistence/migrations **3/3**, and solution build. Its failed first focused attempt and exact historical coverage are retained in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md); current versions and results are documented above. Historical recommendations for relative volume are superseded by the current Milestone A.1 validation checkpoint and do not authorize feature expansion.

### Historical verification: retained-session trade-point ADX slice (2026-10-26)

The focused replay suite passed **119/119**; the full offline unit suite passed **183/183** before SQL verification and again afterward. Against the same dedicated SQL Server test database, the hosted fixture regression passed **1/1 on each of two consecutive runs** without reset; persistence/migration tests passed **3/3**; the solution build succeeded with **0 warnings and 0 errors**. Every final passing test run had 0 failures and 0 skips.

The connection variable was absent. Only the already-running healthy Compose SQL Server and its existing dedicated `MarketOpportunityEngine_IntegrationTests` database were used. A read-only catalog query verified the database. The process-only connection was built from the container credential in memory; the hosted test's `SqlConnectionStringBuilder` guard accepted it before database work, and the parent process launching `dotnet` did not load SqlClient. Overrides and credential references were cleared. Fixture rows were retained; no database was reset/dropped and no service was started/stopped. The migration tests used only uniquely named temporary databases. Browser, live-provider, User Secrets, and Azure verification were not run.

The hosted fixture contains only **4–7** eligible trades per symbol/session, below the default `2 × 14 = 28` observation warm-up. Its independently derived HTTP and raw persisted SQL values are therefore null with `Insufficient` status:

| Symbol/session | Eligible trades | HTTP `sessionAdx` / status | Persisted status |
|---|---:|---|---:|
| MOE / fixture-2026-10-22 | 7 | null / Insufficient | 1 |
| MOE / fixture-2026-10-23 | 6 | null / Insufficient | 1 |
| QNT / fixture-2026-10-22 | 6 | null / Insufficient | 1 |
| QNT / fixture-2026-10-23 | 7 | null / Insufficient | 1 |

Persistence tests independently assert original snapshot ADX **100** and changed-input ADX **57.14285714285714285714285714**, with `Available` status. Changed inputs create a new immutable snapshot/opportunity; the original serialized snapshot, decision fields, and evidence remain unchanged.

**Historical ADX-slice passing commands and results:**

| Order | Command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **119 passed, 0 failed, 0 skipped** |
| Offline full, before SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **183 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command, same database without reset | **1 passed, 0 failed, 0 skipped** |
| SQL persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Offline full, after SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **183 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

**Failed attempts, not passing evidence:** An initial focused run found the new positional snapshot field missing from a test fixture; after that correction, two replay assertions exposed a wrong expected ADX decimal and an identity test whose trade sizes unintentionally changed VWAP. These were corrected before the final 119/119 focused run. One SQL persistence/migration run had **2 passed / 1 failed** from an expected decimal rounding mismatch; the correction was independently derived and the final rerun passed **3/3**. A separate PowerShell SqlClient child guard returned `Microsoft.Data.SqlClient is not supported on this platform` before launching tests, and one orchestration attempt ended after preflight without launching tests. These are guard/runner or assertion failures, not evidence that SQL Server was unavailable. The successful hosted and persistence/migration tests each validated the guarded catalog in their .NET test process before database operations. No prior persisted data was reset.

**Historical next-slice recommendation at that checkpoint:** Define a descriptive trade-point realized-volatility measure using retained scalar trade prices, without synthesizing OHLC bars or changing detector, scoring, or veto behavior. This recommendation was completed by the current `features-v13` slice above. Full Milestone A remains incomplete.

### Historical verification: retained-session price structure slice (2026-10-26)

The focused replay suite passed **107/107**, and the offline unit suite passed **171/171** before SQL verification. Against the same dedicated SQL Server test database, the hosted fixture regression passed **1/1 on each of two consecutive runs** without reset; persistence/migration tests passed **3/3**; the post-SQL unit suite passed **171/171**. The solution build succeeded with **0 warnings and 0 errors**. Every passing test run had 0 failures and 0 skips.

The connection variable was absent. The existing healthy Compose SQL Server and only its already-existing dedicated `MarketOpportunityEngine_IntegrationTests` database were used. A read-only container-local `sqlcmd` query verified the catalog; an isolated encoded PowerShell 7 child loaded the built Windows runtime SqlClient assembly with `Assembly.LoadFrom` and validated/built the connection through `SqlConnectionStringBuilder`'s indexer. The SQL-client runtime was not loaded into the PowerShell child that launched `dotnet`; process-only overrides were cleared. Fixture records were retained; no database was reset/dropped and no service was started/stopped. Browser, live-provider, User Secrets, and Azure verification were not run.

The fixture regression independently derives that all four sessions have fewer than two confirmed pivot highs or lows. It asserts null `sessionPriceStructure` values in every HTTP snapshot and raw persisted SQL snapshot JSON:

| Symbol/session | Ordered eligible prices | Confirmed structure |
|---|---|---|
| MOE / fixture-2026-10-22 | 149.90, 150.08, 150.23, 150.40, 150.48 | null |
| MOE / fixture-2026-10-23 | 150.50, 150.30, 150.08, 149.90 | null |
| QNT / fixture-2026-10-22 | 80.40, 80.20, 80.08, 79.90 | null |
| QNT / fixture-2026-10-23 | 79.80, 80.00, 80.20, 80.40, 80.48 | null |

The persistence regression asserts null structure in both the original snapshot and the later changed-input snapshot. The later input still creates a distinct snapshot and opportunity decision, while the original snapshot JSON, opportunity fields, and evidence remain unchanged.

**Current passing commands and results:**

| Order | Command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **107 passed, 0 failed, 0 skipped** |
| Offline full | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **171 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command, same database without reset | **1 passed, 0 failed, 0 skipped** |
| SQL persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Post-SQL full offline | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **171 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

**Failed attempts, not passing evidence:** Initial focused runs found an invalid decimal `InlineData` attribute, a positional `FeatureSnapshot` fixture missing the new field, incorrect swing test paths/identity coverage, and a missing test using; these were corrected before the final 107/107 pass. Early isolated SQL guard attempts failed before test execution due to PowerShell type resolution, assembly selection, script construction, or a native-DLL failure while opening `SqlConnection` from PowerShell. The successful primary guard used the builder-only isolated SqlClient check plus a read-only container-local catalog query, and its documented SQL sequence passed. A later redundant attempt to rerun the persistence/migration filter reached the SQL test runner but failed all three tests with an `sa` login failure from that attempt's temporary connection override; a corrected read-only catalog query still confirmed the dedicated database was accessible. This runner/credential mismatch is a separate failed attempt, not evidence that SQL Server/database was unavailable, and it does not replace the passing primary-sequence results. The integration-test project was built once to materialize the guard assembly (0 warnings/0 errors); the final solution build is reported above.

### Historical verification: descriptive EMA slice (2026-10-25)

The preceding EMA slice passed focused replay **98/98**, full offline units **162/162**, hosted fixture **1/1 twice**, persistence/migrations **3/3**, and a solution build with **0 warnings and 0 errors**. Historical EMA assertions remain in tests; these are prior results, not additional current-run totals.

The historical hosted HTTP payload and raw persisted SQL JSON asserted these fast EMA values; the remaining nullable outputs were null because the fixture sessions had fewer than 20 trades and fewer than two initialized fast samples in the 60-second slope interval:

| Symbol/session | Ordered eligible prices | Fast EMA (5) | Medium EMA (20) | Spread % | Fast slope / price-min | Medium slope / price-min |
|---|---|---:|---:|---:|---:|---:|
| MOE / fixture-2026-10-22 | 149.90, 150.08, 150.23, 150.40, 150.48 | 150.218 | null | null | null | null |
| MOE / fixture-2026-10-23 | 150.50, 150.30, 150.08, 149.90 | null | null | null | null | null |
| QNT / fixture-2026-10-22 | 80.40, 80.20, 80.08, 79.90 | null | null | null | null | null |
| QNT / fixture-2026-10-23 | 79.80, 80.00, 80.20, 80.40, 80.48 | 80.176 | null | null | null | null |

The historical persistence regression confirmed that later input created a new feature snapshot and opportunity decision without rewriting the original records; the new snapshot JSON asserted fast EMA **102.1** and fast slope **12 price/minute**, while the original four-trade snapshot remained null for fast/medium EMA.

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

Earlier attempts in this slice are **not passing SQL evidence**: the Windows PowerShell child guard could not resolve SQL-client dependencies; the terminal runner subsequently returned `program not found` before launching validation. Recovery used a temporary VS Code task, loading the SQL-client runtime with `Assembly.LoadFrom` only inside a PowerShell 7 child for the builder guard. The parent then ran the unchanged documented commands. The helpers/task were removed afterward. Prior second-ROC (**30 replay / 94 units**), VWAP-slope (**8 replay / 72 units**), native-SNI/wrapper failures, and browser runs remain historical, not results of this slice. Exact commands and evidence boundaries are separated in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#20-test-status).

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

Feature snapshots expose descriptive `SessionVwapSlopePerMinute`, `SessionVwapSlopeDirection`, `SecondaryHorizonReturnPercent`, `ShortHorizonDollarVolumeUsd` plus `ShortHorizonDollarVolumeStatus`, `SessionCumulativeVolume`, `SessionHighPrice`, `SessionLowPrice`, `SessionRangePositionPercent`, nullable `SessionPriceStructure`, nullable `SessionAdx`, `SessionAdxStatus`, non-null `SessionTrendDirection`, and nullable `SessionRealizedVolatilityPercent` plus `RealizedVolatilityStatus`. Read retained-session values with `HasCompleteSessionData` as described above. Dollar volume is consumed only by the separate hard veto; the volatility veto and its distinct descriptive feature remain documented above.

## Current limits

- Two synthetic fixture symbols and two explicit fixture sessions; this is not a market recommendation.
- One Momentum Continuation detector; the remaining three Milestone A detectors are not implemented.
- Session VWAP and momentum use bounded in-memory events per symbol/session. If bounded state evicts events, the analysis is explicitly marked incomplete and vetoed.
- Complete, available realized volatility at or above the provisional configured 1.00 percentage-point threshold is a hard veto; insufficient and invalid volatility have explicit veto reasons.
- No exchange calendar, Massive live-feed reconnect, benchmark/sector context, SignalR application hub, outcome labeling, or production migration pipeline.
- Alpaca has bounded reconnect and explicit unresolved-gap publication suspension; historical gap recovery/backfill remains unimplemented. Other feed health states suspend publication through the application lifecycle gate.
- Setup quality is a deterministic 0–100 rules score, not a probability of profit.
- Azure deployment is not configured yet; Azure SQL Database remains the required deployment database.

## Decisions

See [docs/adr/](./docs/adr/) for render mode, persistence, in-process transport, event retention, and provider-selection decisions.
