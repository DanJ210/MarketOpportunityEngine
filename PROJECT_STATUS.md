# Market Opportunity Engine - Current Project Status

**Audit date:** 2026-10-26 (descriptive retained-session trend-direction slice; prior trade-point ADX, EMA, price-structure, range-position, extrema, volume, ROC, VWAP-slope, hosted API negative-lookup, SQL/API/browser, and implementation-audit evidence retained separately below)
**Evidence base:** repository source, tests, configuration, migrations, ADRs, README, master specification, Alpaca official WebSocket/stock-data documentation, and the validation results recorded below.

## Latest Analysis Snapshot - retained-session trend direction (2026-10-26)

**Versioned descriptive retained-session trend-direction slice: COMPLETE. Full Milestone A: NOT COMPLETE.**

| Area | New verified result | Evidence boundary |
|---|---|---|
| Focused replay | **123 passed, 0 failed, 0 skipped** | Direction agreement/conflict/neutral/insufficient/invalid states, independent ADX strength, partial retention, JSON, identity and detector neutrality; previous replay assertions remain. |
| Offline units | **187 passed, 0 failed, 0 skipped** | Passed before SQL and again after SQL verification. |
| Hosted fixture HTTP + SQL | **1 passed on each of two consecutive runs** | Same dedicated database without reset; exact context asserted in all four HTTP snapshots and raw persisted SQL JSON alongside provenance, completeness, identity, canonical-event and negative-lookup checks. |
| SQL persistence/migrations | **3 passed, 0 failed, 0 skipped** | Changed input creates a new immutable snapshot/decision and retains original JSON/decision/evidence. |
| Solution build | **Succeeded** | Full solution, including integration tests. |
| Versions | **features-v12 / scanner-config-v12** | Non-null `FeatureSnapshot.SessionTrendDirection` (`TrendDirectionContext`) participates explicitly in deterministic feature identity; no new configuration; setup remains `momentum-continuation-v1`. Prior persisted records are not rewritten. |
| Browser/live-provider/Azure | **Not run** | Analytics-only change; no UI/provider changes and no new browser, provider, or Azure evidence. |

`FeatureSnapshot.SessionTrendDirection` is a non-null `TrendDirectionContext` enum (`sessionTrendDirection` in JSON) with `Bullish`, `Bearish`, `Neutral`, `Conflicting`, `Insufficient`, and `Invalid` values. Directional inputs are the signed fast/medium EMA spread, both signed EMA slopes, session VWAP slope/direction, and confirmed session price structure. Positive values or Rising/HigherHighHigherLow vote bullish; negative values or Falling/LowerHighLowerLow vote bearish; zero/Flat is neutral; `Mixed` structure is a conflict. ADX remains a separate, validated 0–100 nondirectional strength/status; magnitude never votes. `InvalidPriceData` ADX status marks the context Invalid because it denotes invalid eligible prices.

Deterministic precedence is: invalid/inconsistent provided inputs → `Invalid`; opposing bullish and bearish votes or `Mixed` structure → `Conflicting`; otherwise any one-sided vote → `Bullish`/`Bearish`; available directional inputs that are all zero/flat → `Neutral`; no available directional input → `Insufficient`. Null EMA/slope, VWAP `Insufficient`, null structure, and ADX `Insufficient` add no vote. A non-null spread requires both positive EMAs; slopes require their respective EMA. VWAP's signed slope must match Rising/Falling/Flat, and ADX status/value must satisfy its existing contract. This is fixed sign/enum logic, so no new configuration is required. The version advances from `features-v11` / `scanner-config-v11` to `features-v12` / `scanner-config-v12`; `momentum-continuation-v1` remains unchanged.

The feature calculator continues to use only the existing same-symbol, explicit-session events with `EventTimeUtc <= AsOfUtc`; event order, duplicate handling, retained state, and `AsOfUtc` are unchanged. `HasCompleteSessionData=false` does not change the enum or make partial features complete: any resulting direction describes retained partial-session inputs and must be read with that flag. HTTP serializes the enum as a string; persisted snapshot JSON uses the default numeric enum encoding. The output is explicitly included in identity for every enum state. No detector qualification, scoring/classification, evidence/contradictions, opportunity/publication, veto, UI, provider, or existing feature calculation changed.

**Current passing commands and totals:** focused replay `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` (**123/123**); full offline units `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` (**187/187 before SQL and again after SQL**); hosted fixture `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` (**1/1 twice on the same database without reset**); persistence/migrations `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` (**3/3**); and `dotnet build MarketOpportunityEngine.sln` (**succeeded**). All passing test runs had 0 failures and 0 skips.

**SQL gate/result:** `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. The already-running Compose SQL Server was healthy; its existing dedicated `MarketOpportunityEngine_IntegrationTests` database was verified with a read-only catalog query. An isolated child validated the fallback using `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`. The process-only connection was constructed in memory and cleared in `finally`; the parent process launching `dotnet` did not load SqlClient. No service was started/stopped/recreated, no fixture rows/database were reset or deleted, and the database was not dropped. Migration tests used only uniquely named temporary databases. The guarded SQL sequence passed.

Browser, live-provider, User Secrets, and Azure validation were not run. The first focused attempt had **120 passed / 3 failed**: one JSON test did not use the HTTP string-enum converter, a configuration identity test reused `scanner-config-v12` as the purported changed version, and a structure identity test did not normalize the new context. These test issues were corrected before the final 123/123 run; they are failed-attempt evidence, not passing results. The final focused and ordered verification sequence passed.

**Next smallest slice (recommendation only):** Define a descriptive trade-point realized-volatility measure using retained scalar trade prices, without synthesizing OHLC bars or changing detector, scoring, or veto behavior.

## Historical Analysis Snapshot - retained-session trade-point ADX (2026-10-26)
This is an explicit **trade-point ADX variant**, not bar-based ADX. The canonical events expose trades and quotes but no OHLC bars, so the calculator does not synthesize bars. Each eligible trade price is one observation. For consecutive prices `P[t-1]`, `P[t]`: `TR = abs(P[t] - P[t-1])`; `up = P[t] - P[t-1]`; `down = P[t-1] - P[t]`; `+DM = up` only when `up > down && up > 0`; `-DM = down` only when `down > up && down > 0`; otherwise the respective DM is zero. Equal consecutive prices have zero TR/DM.

`Scanner:TrendStrengthPeriod` defaults to **14 price movements** and validates as a positive integer. For period `N`, the first `N` movements seed arithmetic means for TR/+DM/-DM. Subsequent values use Wilder smoothing `smoothed = prior + (current - prior) / N`. `+DI = 100 * smoothed(+DM) / smoothed(TR)` and `-DI = 100 * smoothed(-DM) / smoothed(TR)`; `DX = 100 * abs(+DI - -DI) / (+DI + -DI)`. Zero true range or zero DI sum defines DX as zero. Initial ADX is the arithmetic mean of the first `N` DX values; subsequent ADX uses the same Wilder recurrence. Calculations use decimal values deterministically; ADX is nondirectional strength on 0–100, not probability.

Eligible input is all retained `TradeEvent`s for the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`, in canonical event-time/provider/ordinal-sequence/canonical-identity order. There is no duration interval; older eligible trades contribute. Quotes, sizes (including zero/negative), receive time, and wall-clock time are excluded. Equal timestamps are distinct, canonically ordered observations. Warm-up needs **2 × N eligible trades**: `N` movements for initial DI/DX, then `N` DX values for initial ADX. No eligible trade or fewer than `2 × N` returns `SessionAdx = null`, `AdxStatus.Insufficient`. Any eligible price `<= 0` returns null and `InvalidPriceData`, without dropping the invalid trade. A non-null value with `HasCompleteSessionData = false` is retained-partial data; completeness does not change the status/null value. A future event is excluded unless arrival evicts eligible retained state, which can change a historical-as-of result; completeness must be interpreted alongside the value.

The output is `decimal? SessionAdx` / JSON `sessionAdx` and `AdxStatus SessionAdxStatus` / JSON `sessionAdxStatus`. HTTP uses the existing string-enum convention; SQL snapshot JSON retains numeric enum encoding (`Available=0`, `Insufficient=1`, `InvalidPriceData=2`). Both fields, including null and state, participate in deterministic feature identity. `TrendStrengthPeriod` is included in serialized configuration and versioned validation. Defaults advance from `features-v10` / `scanner-config-v10` to `features-v11` / `scanner-config-v11`; setup remains `momentum-continuation-v1`. Earlier persisted configurations/snapshots/decisions are not rewritten. ADX is descriptive only and does not affect qualification, scoring, classifications, publication, vetoes, UI, providers, or prior feature calculations.

The hosted fixture has 4–7 eligible trades per symbol/session, below the default 28-observation warm-up; exact expected `sessionAdx` is null and status is `Insufficient` in each HTTP snapshot and raw persisted SQL snapshot JSON. Persistence replay asserts ADX 100 for the original monotonic four-trade input and 57.14285714285714285714285714 for changed input under test period 2; the changed input creates distinct immutable records while original snapshot JSON, opportunity fields, and evidence remain unchanged.

**Passing commands and results (all passing runs had 0 failures/0 skips):** Focused replay `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` (**119/119**); full offline units `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` (**183/183** before SQL and again after); hosted fixture `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` (**1/1 twice against the same database**); SQL persistence/migrations `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` (**3/3**); and `dotnet build MarketOpportunityEngine.sln` (**0 warnings/0 errors**). Integration-test project compile-only build also passed with 0 warnings/0 errors.

The connection variable was absent. The existing Compose SQL Server was already healthy and its existing dedicated `MarketOpportunityEngine_IntegrationTests` catalog existed; a read-only query verified it. The process-only connection was constructed from the running container credential in memory and guarded by `SqlConnectionStringBuilder` before SQL work. The parent process launching `dotnet` did not load SqlClient. Overrides and password references were cleared; fixture rows were retained; no database was reset/dropped and no service was started/stopped. Migration tests used only uniquely named temporary databases. Browser, live-provider, User Secrets, and Azure checks were not run.

**Failed attempts, not passing evidence:** The first focused build found the positional test snapshot fixture missing the new fields. Subsequent replay assertions corrected an expected decimal rounding and an identity test's unintended VWAP change before the final 119/119 run. An initial persistence/migration run was **2 passed / 1 failed** because its exact ADX decimal expectation differed by one final decimal digit; the corrected run passed **3/3**. Separate PowerShell child builder attempts returned `Microsoft.Data.SqlClient is not supported on this platform`, and one orchestration attempt ended after preflight without launching tests. These were child-runner/expectation failures; they are not evidence SQL Server was unavailable. The final SQL test runs passed their in-process connection-string guard and the guarded catalog was available. No browser, live-provider, User Secrets, or Azure verification was run.

**Next smallest slice (recommendation only):** Add descriptive trend-direction context derived from existing EMA relationship/slopes, VWAP slope, price structure, and ADX, with explicit neutral/conflicting semantics and replay tests; keep it separate from `momentum-continuation-v1` qualification and scoring.

## Historical snapshot: retained-session price structure (2026-10-26)

The preceding structure slice passed focused replay **107/107**, offline units **171/171**, hosted fixture **1/1 twice**, persistence/migrations **3/3**, and solution build **0 warnings/0 errors**. Its exact fixture `sessionPriceStructure` was null for all four sessions because there were fewer than two confirmed pivots of each type. Structure output was descriptive, used the fixed two-left/two-right pivot rule, and participated explicitly in `features-v10` identity. Historical results are retained here; current versions are `features-v11` / `scanner-config-v11`.

## Historical high/low analysis snapshot - 2026-10-25

## Historical volume-slice snapshot - 2026-10-25

The following volume-only snapshot and its totals are historical, superseded by the extrema evidence above.

**Versioned descriptive session cumulative-volume slice: COMPLETE. Full Milestone A: NOT COMPLETE.**

| Area | Latest verified result | Evidence boundary |
|---|---|---|
| Hosted fixture → SQL + HTTP | **1 passed, 0 failed, 0 skipped on each of two consecutive runs** | Same dedicated SQL database; isolated Testing host; exact four analyses/snapshots, four absent-resource `404`s, and 26 canonical events compared. |
| Hosted API negative lookups | **4 exact `404 Not Found` assertions passed on both hosted runs** | Valid absent opportunity GUID; absent symbol; explicit absent session for both MOE and QNT. Exact valid session-specific lookups also passed, demonstrating no cross-session fallback. |
| SQL persistence / migrations | **3 passed, 0 failed, 0 skipped** | Dedicated local test database; includes clean-schema and legacy-schema migration tests. |
| Focused replay tests | **46 passed, 0 failed, 0 skipped** | 16 added volume cases; existing ROC/slope/replay cases preserved. |
| Offline unit tests | **110 passed, 0 failed, 0 skipped** | Passed immediately after focused tests and again after SQL; no live provider or database required. |
| Solution build | **Succeeded; no warnings or errors reported** | Includes integration-project compilation. |
| Running dashboard | **Historical integrated-browser walkthrough passed** | Not rerun for this analytics-only slice; no UI change or new browser evidence. |
| Hosted API versus persisted SQL | **4 opportunity IDs, 4 snapshot IDs/payloads, and all 26 canonical events matched automatically** | Exact ID/version-scoped SQL comparisons include analytical fields/evidence/provenance and trade/quote fields. API still reads process memory, not SQL history. |
| Effective configuration / SQL immutability | **Existing HTTP and SQL assertions preserved and passed** | HTTP exposes both windows; SQL rejects a same-version window change and retains original configuration/snapshot payloads. |
| Session cumulative volume | **Exact HTTP and persisted JSON assertions passed** | MOE-22: 5600; MOE-23: 5450; QNT-22: 5050; QNT-23: 5650. All report complete retained state. |
| Live providers / Azure | **Not run in this slice** | No provider User Secrets or live connection. Earlier Massive attempt belongs to historical evidence in section 20. |

### Observed fixture results

| Symbol | Explicit session | Analytical classification | Publication state / reason | Persisted events |
|---|---|---|---|---:|
| MOE | fixture-2026-10-22 | ProbableLong | Expired / OpportunityExpired | 7 |
| QNT | fixture-2026-10-22 | ProbableShort | Expired / OpportunityExpired | 6 |
| MOE | fixture-2026-10-23 | ProbableShort | Active / PublicationAllowed | 6 |
| QNT | fixture-2026-10-23 | ProbableLong | Active / PublicationAllowed | 7 |

All four results carried `RecordedFixture` / `Synthetic` / `Synthetic` provider/feed/coverage, realtime **false**, and trade/quote support **true**. Publication was evaluated at fixture replay time, not today's market time. Scores/classifications are not calibrated probabilities or profitability claims.

### Changes, operational state, and remaining work

- **Changes:** Added nullable `long` `SessionCumulativeVolume`: checked sum of sizes over all retained same-symbol/explicit-session trades at/before `AsOfUtc`, not either ROC window. No eligible trades or any eligible size `<= 0` yields null; one positive-size trade is sufficient. Quotes/receive/system time do not contribute. Positive sizes remain measurable independently of invalid prices; existing price validity, quality, and veto semantics are untouched. `ShortHorizonVolume`, both validated ROC horizons, and exact VWAP-slope tests/algorithm remain unchanged.
- **Completeness:** Read volume with `HasCompleteSessionData`. True means no event eviction; false marks a non-null retained sum as partial, never complete session volume. This sticky flag includes quote eviction and does not establish upstream feed/session-start coverage. After eviction, no eligible trades or invalid retained sizes still yields null. Future events are excluded but may evict earlier state; this is not historical replay/backfill.
- **Feature contract versions:** bumped from v5 to `features-v6` and `scanner-config-v6`; serialized configuration and new volume participate in deterministic identity. Setup remains `momentum-continuation-v1`. Prior persisted configurations/snapshots/decisions remain immutable. No detector, scoring, classification, publication, veto, UI, provider, dependency, or migration change was made.
- **Preserved test isolation:** The fixture host remains non-Development, uses early host configuration, explicitly registers Fixture/configuration/SQL, and does not load provider User Secrets. The non-empty test database-name guard remains intact.
- **Preserved fix:** [SqlServerPersistenceTests](tests/MarketOpportunity.IntegrationTests/SqlServerPersistenceTests.cs) still migrates when `EnsureCreatedAsync` returns false. No test-database guard was weakened.
- **Final-run database safety:** The variable was absent; reused the existing healthy Compose SQL Server as authorized by this request, targeting `MarketOpportunityEngine_IntegrationTests` plus uniquely named migration-test databases. The non-empty catalog containing `test` was validated with `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`. Credentials were held in process memory, not printed/saved. Fixture records were retained; no configured database was dropped.
- **Last verified cleanup:** Test hosts disposed; no separate web process was started for this slice. The pre-existing SQL container remained healthy/running. Connection settings were process-only. The prior port-5228 cleanup/browser walkthrough is historical, not rechecked here.
- **Remaining limits:** API/history reads remain memory-backed; publication history is not persisted; live reconnect/gap recovery, three additional detectors, and broader Milestone A feature/risk coverage are absent. Local fixture verification does not establish Azure SQL or live-provider readiness.
- **Historical next-slice recommendation:** Expose descriptive retained-session high/low prices with event-time cutoff, invalid-input handling, completeness, and replay tests. That recommendation was implemented in the next checkpoint recorded below.
- **Evidence timing:** New ordered automated runs are recorded first in section 20. Earlier failed attempts and historical browser/runtime runs remain separate.

Section 20 records exact commands and separates the successful final run from the earlier blocked preflight and prior checkpoint. Sections 24–27 summarize remaining blockers and the handoff.

## 1. Executive Status

- **Current milestone:** Milestone A — Usable Scanner.
- **Current implementation checkpoint:** Publication/lifecycle safety and Alpaca Market Data development stream are implemented in addition to the existing fixture/Massive sources.
- **Overall status:** **PARTIALLY IMPLEMENTED.** Publication safety and provider provenance are implemented. Local SQL persistence/migration, hosted fixture HTTP, and browser dashboard verification passed on 2026-10-25. Authenticated Alpaca connectivity remains unverified.
- **Last completed vertical slice:** Descriptive retained-session range position over the established extrema, with offline replay and repeated hosted fixture → SQL → current-state API regression. Existing features and detector output are preserved. This does not establish database-backed historical API reads; browser evidence is historical.
- **Currently active work:** Range-position analytics and hosted regressions complete; the pre-existing SQL container was preserved. No live provider or UI change.
- **Primary blocker:** None for this verification slice. Full Milestone A still lacks three detectors, several required features/gates, reconnect/gap recovery, and database-backed reads.
- **Recommended immediate next step:** Add fast/medium EMA relationship and event-time slope as versioned descriptive trend features, following the master specification; do not change detector outcomes until separately authorized. Keep provider recovery separate; do not connect live providers implicitly.

Milestone A has a tested offline analysis slice, an application-level publication safety boundary, and a verified local fixture SQL/API/interactive UI workflow. Provider adapters include fixture, Alpaca, and Massive. Full usable-scanner exit criteria still require reliable live recovery, four detectors, required risk/liquidity coverage, and structural invalidation monitoring.

## 2. Technology Verification

| Technology | Status | Evidence | Notes |
|---|---|---|---|
| .NET 10 / `net10.0` | **IMPLEMENTED** | All eight source/test projects target `net10.0`; solution restore/build succeeded. | Optional connectivity test project is outside the solution/default unit suite. No runtime patch pinning is visible. |
| ASP.NET Core 10 | **IMPLEMENTED** | Web SDK project targets `net10.0`; hosting/configuration is in `src/MarketOpportunity.Web/Program.cs`. | Local app startup depends on configured SQL Server. |
| C# | **IMPLEMENTED** | Source projects use C#; domain, analytics, pipeline, adapters, and endpoint code are C#. | Nullable reference types are enabled on the projects inspected. |
| Blazor | **IMPLEMENTED** | Blazor Web App components under `src/MarketOpportunity.Web/Components`; `App.razor` uses `InteractiveServer`. | No separate frontend technology. |
| Entity Framework Core 10 | **IMPLEMENTED** | Infrastructure references `Microsoft.EntityFrameworkCore.SqlServer` 10.0.0 and owns `MarketOpportunityDbContext`. | Local persistence and both migration tests passed on 2026-10-25. |
| Microsoft SQL Server | **IMPLEMENTED** | `docker-compose.yml` defines SQL Server 2022 Developer; development configuration targets `localhost,1433`. | SQL integration tests require an explicitly configured, dedicated test database. |
| Azure SQL Database compatibility | **PARTIALLY IMPLEMENTED** | Uses EF Core SQL Server provider and ordinary SQL Server mappings. | No Azure SQL connection, deployment, or compatibility test configuration exists. Compatibility is plausible, not verified. |
| SignalR | **PARTIALLY IMPLEMENTED** | Blazor Interactive Server uses its circuit transport; ADR-001 explicitly declines a separate application hub. | No app-level `AddSignalR`, hub, or domain event contract is present. |
| OpenTelemetry | **PARTIALLY IMPLEMENTED** | `Program.cs` adds ASP.NET Core tracing and ASP.NET Core/runtime metrics; OTLP exporter is conditional on `OTEL_EXPORTER_OTLP_ENDPOINT`. | No scanner-specific spans, counters, histograms, or end-to-end trace propagation. |
| Azure App Service | **NOT IMPLEMENTED** | No App Service deployment configuration or IaC found. | The Web project is an ASP.NET Core application but has no Azure hosting setup. |
| Azure Key Vault | **NOT IMPLEMENTED** | No Key Vault package/provider or resource configuration found. | Local README advises User Secrets; this is not deployed secret integration. |
| Azure Monitor / Application Insights | **NOT IMPLEMENTED** | No Application Insights package/exporter or Azure Monitor configuration found. | OTLP exporter is generic and does not alone constitute Azure Monitor integration. |
| Bicep | **NOT IMPLEMENTED** | No `.bicep` files found. | Azure infrastructure is not provisioned from code. |
| Azure DevOps Pipeline | **NOT IMPLEMENTED** | No Azure pipeline YAML or pipeline configuration found. | `.github/copilot-instructions.md` is not CI/CD. |

## 3. Solution / Repository Structure

Meaningful repository tree (generated `bin`, `obj`, and vendored Bootstrap assets omitted):

```text
MarketOpportunityEngine.sln
README.md
Market_Opportunity_Engine_Final_Master_Spec.md
docker-compose.yml
.github/
  copilot-instructions.md
docs/
  adr/
    ADR-001-blazor-render-mode.md
    ADR-002-ef-sqlserver-conventions.md
    ADR-003-in-process-event-transport.md
    ADR-004-market-event-retention.md
    ADR-005-market-data-provider-criteria.md
  providers/
    alpaca-websocket.md
    massive-websocket.md
src/
  MarketOpportunity.Domain/
    MarketEvents.cs
  MarketOpportunity.Analytics/
    MomentumContinuationDetector.cs
    SymbolEventState.cs
  MarketOpportunity.Application/
    ScannerPipeline.cs
  MarketOpportunity.Infrastructure/
    AlpacaMarketDataSource.cs
    EfScannerStore.cs
    FixtureMarketDataSource.cs
    MarketDataSourceFactory.cs
    MassiveMarketDataSource.cs
    MarketOpportunityDbContext.cs
    Migrations/
      20261023190000_AddExplicitSessionIdentity.cs
      20261024232400_AddOpportunityMarketDataProvenance.cs
  MarketOpportunity.Web/
    Program.cs
    ScannerRunnerService.cs
    appsettings.json
    appsettings.Development.json
    Properties/launchSettings.json
    Fixtures/momentum-continuation.json
    Components/
      App.razor
      Routes.razor
      Layout/
      Pages/
        Home.razor
        Counter.razor
        Weather.razor
        Error.razor
        NotFound.razor
tests/
  MarketOpportunity.UnitTests/
    AlpacaMarketDataTests.cs
    MassiveMarketDataOptionsTests.cs
    MassiveMarketDataParserTests.cs
    MomentumContinuationTests.cs
    ScannerPipelineTests.cs
    ScannerReplayTests.cs
  MarketOpportunity.IntegrationTests/
    FixtureWebApplicationTests.cs
    ExplicitSessionMigrationTests.cs
    SqlServerPersistenceTests.cs
  MarketOpportunity.AlpacaConnectivityTests/
    AlpacaTestStreamConnectivityTests.cs
```

### Projects and dependencies

| Project | Purpose | Project dependencies |
|---|---|---|
| `MarketOpportunity.Domain` | Canonical market-event, feature, opportunity, classification, and scanner-configuration contracts. | None. |
| `MarketOpportunity.Analytics` | Event-time symbol/session state, feature calculations, deterministic IDs, and Momentum Continuation detector. | Domain. |
| `MarketOpportunity.Application` | Market-data/store interfaces, health state, provider-independent publication gate/read model, and fixture/live orchestration pipeline. | Domain, Analytics. |
| `MarketOpportunity.Infrastructure` | Fixture, Alpaca, and Massive adapters; source factory; SQL entities/context/migrations and EF store. | Domain, Application. |
| `MarketOpportunity.Web` | ASP.NET Core host, API routes, hosted scanner, and Blazor interface. | Domain, Application, Analytics, Infrastructure. |
| `MarketOpportunity.UnitTests` | Fast tests for parser, options, analytics, replay, and pipeline behavior. | Domain, Analytics, Application, Infrastructure. |
| `MarketOpportunity.IntegrationTests` | SQL Server persistence/migration tests and exact hosted fixture Web API + persistence regression. | Analytics, Infrastructure, Web. |
| `MarketOpportunity.AlpacaConnectivityTests` | Explicitly invoked network test for Alpaca's synthetic `test` stream; not part of the solution/default unit suite. | Infrastructure; shares Web project's User Secrets ID. |

**Architectural assessment:** No direct dependency violation was found in the inspected project references: Domain has no project dependencies; Analytics has no EF/HTTP/provider dependency; Infrastructure owns EF and provider adapters. The application is a modular monolith. There are no separate API, Contracts, MarketData, Replay, Models, or Observability projects from the broader illustrative master-spec tree; API and hosting are currently in Web.

## 4. Architecture Status

| Pipeline stage | Status | Implementation / key files | Tests | Known limitations |
|---|---|---|---|---|
| Market data | **IMPLEMENTED** for fixture; **PARTIALLY IMPLEMENTED** for optional live sources | `FixtureMarketDataSource`; `AlpacaMarketDataSource`; `MassiveMarketDataSource`; centralized factory | Pipeline tests and Alpaca/Massive parser/options/capability/factory tests | Authenticated connectivity, entitlements, reconnect, and recovery are not integration-tested. |
| Normalization | **PARTIALLY IMPLEMENTED** | Fixture JSON, Massive messages, and Alpaca `t`/`q` messages map to canonical events in Infrastructure. | Alpaca/Massive parser tests cover timestamps, identities, malformed and unsupported messages. | No distinct reusable normalizer/data-quality stage; no correction or gap contract; no aggregates. |
| Event-time state | **IMPLEMENTED** for bounded symbol/session state | `SymbolEventState` stores and sorts events; duplicate identities are ignored while retained. | Replay tests cover ordering, duplicates, sessions, and future-event exclusion. | No watermarks, gap tracking, corrections, or exchange-session calendar. |
| Features | **PARTIALLY IMPLEMENTED** | `FeatureCalculator` in `SymbolEventState.cs`. | Momentum and replay tests cover VWAP, current price, return, volume, and as-of cutoff. | Narrow feature set; bounded eviction means cumulative session values become incomplete and vetoed. |
| Setup detector | **PARTIALLY IMPLEMENTED** | `MomentumContinuationDetector`. | `MomentumContinuationTests`, `ScannerReplayTests`. | Only one detector, using one short horizon and VWAP alignment; no complete trend/context engine. |
| Risk/data-quality gates | **PARTIALLY IMPLEMENTED** | Quote/data vetoes remain in the detector; `OpportunityPublicationGate` separately evaluates source health and candidate expiry in Application. | Tests cover stale/offline transition, immutable evidence, expiry, stale quote, invalid quote/price, spread, insufficient data, and evicted state. | Gap recovery, liquidity, reward/risk, volatility, and structural invalidation monitoring remain absent. |
| Opportunity/publication lifecycle | **PARTIALLY IMPLEMENTED** | Immutable detector `Opportunity` plus separate `PublicationDecision` (`Active`, `Suspended`, `Expired`, `NotQualified`) and reason; `ScannerState` recalculates on feed-status changes. | Deterministic gate, transition, API read-model serialization, and fixture tests. | Current publication-state transitions are process-memory read-model state, not append-only SQL lifecycle rows; no later-price structural invalidation monitor. |
| Persistence | **SQL VERIFIED 2026-10-26** | `EfScannerStore`, `MarketOpportunityDbContext`, provider provenance columns, SQL Server provider, explicit-session and provenance migrations. | Hosted fixture HTTP/SQL regression passed twice on the same dedicated database; persistence/migration selector passed 3/3. | API does not query stored history; all tests require a guarded dedicated SQL test database. |
| ASP.NET Core | **IMPLEMENTED AND FIXTURE-VERIFIED** | `Program.cs` maps health and five API routes; opportunity list/detail responses expose publication status/reason; `ScannerRunnerService` starts scanner. | Hosted HTTP + SQL fixture test passed; local browser/API results matched SQL IDs. | API still serves the in-memory singleton state, not persisted history. |
| Blazor | **IMPLEMENTED IN CODE** | Interactive Server `App.razor`; dashboard in `Home.razor` shows source/feed/coverage and per-candidate lifecycle/reason. | No component/UI tests found; build succeeded. | Runtime browser workflow was not exercised; no SignalR application hub. |

**Exact evidence boundary:** Unit tests cover analysis/publication safety and provider parsers/options without network access. On 2026-10-26 the guarded hosted fixture HTTP/SQL regression passed twice on the same database and the persistence/migration selector passed; the prior local browser walkthrough from 2026-10-25 remains historical evidence only. Authenticated provider streams and historical reads remain unverified/absent. The API reads `ScannerState`, not persisted records.

## 5. Market Data Status

`IMarketDataSource` has three implementations. Provider choice is centralized by `MarketDataSourceFactory`; each source provides capability metadata.

| Source | Type | Data supplied | Trades | Quotes | Aggregates | Provider-specific | Authentication | Working / tested | Limitations |
|---|---|---|---|---|---|---|---|---|---|
| `FixtureMarketDataSource` | Recorded JSON fixture | Synthetic `MOE` and `QNT` events, interleaved over two explicit sessions; event and receive timestamps, provider, sequence identity, trade and quote fields. | Yes | Yes | No | No; normalized fixture values | No | Pipeline unit tests and hosted API+SQL test passed; local browser walkthrough passed on 2026-10-25. No committed browser test suite. | Small, synthetic, finite replay; not actual exchange data. |
| `AlpacaMarketDataSource` | Live WebSocket (also Alpaca synthetic test stream) | Stock trade/quote messages mapped to canonical events. Feed configurable as `test`, `iex`, `sip`, or `delayed_sip`. | Yes | Yes | No | Yes, parser and socket adapter confined to Infrastructure | Yes, API key ID + secret in User Secrets/configuration | Parser, configuration validation, feed capability mapping, source factory, and provenance unit-tested; test-stream connectivity test is opt-in and was not run. | No reconnect, historical warm-up, correction handling, gap/backfill, or aggregates; test feed is synthetic; IEX is limited and delayed SIP is delayed. |
| `MassiveMarketDataSource` | Optional live WebSocket | Massive real-time stock trades (`T.<symbol>`) and quotes (`Q.<symbol>`); provider event time and local receive time. | Yes | Yes | No | Yes, DTO parsing confined to Infrastructure | Yes, Massive API key; feed entitlement required | Parser/options are unit-tested. Actual authenticated WebSocket operation was not tested. | No historical warm-up, reconnect, heartbeat recovery, gap detection/backfill, or aggregates; depends on configured plan and credentials. |

The active default in `appsettings.json` is `MarketData:Provider = Fixture`. No evidence was collected that a live provider is currently connected. There is no historical source. Alpaca test stream configuration defaults to feed `test`/symbol `FAKEPACA`; credentials and explicit session ID are required before selection. Massive remains supported. No order/broker integration exists.

## 6. Canonical Market Event Model

The domain contracts are in `MarketEvents.cs`:

| Concept | Status | Evidence / notes |
|---|---|---|
| `MarketEvent` | **IMPLEMENTED** | Abstract record with `Symbol`, `SessionId`, `EventTimeUtc`, `ReceivedTimeUtc`, `Provider`, nullable `SequenceId`. |
| `TradeEvent` | **IMPLEMENTED** | Adds `decimal Price`, `long Size`. |
| `QuoteEvent` | **IMPLEMENTED** | Adds decimal bid/ask prices and nullable long bid/ask sizes. |
| Provider/feed capability metadata | **IMPLEMENTED** | `MarketDataCapabilities` records provider, feed, coverage (`Synthetic`, `Limited`, `Consolidated`, `ProviderDefined`), real-time flag, and supported trade/quote channels. |
| Aggregate/bar event | **NOT IMPLEMENTED** | Unsupported by the canonical event switch and provider adapter. |
| Symbol | **PARTIALLY IMPLEMENTED** | String field, not a validated instrument/symbol value object. |
| Event time / receive time | **IMPLEMENTED** | Separate `DateTimeOffset` fields; both live adapters map provider time to event time and injected `TimeProvider` to receive time. |
| Provider | **IMPLEMENTED** | String provider identity. |
| Event/sequence identity | **PARTIALLY IMPLEMENTED** | Nullable provider sequence plus deterministic `MarketEventIdentity.Create`; identity is derived rather than a field on the record. |
| Explicit session identity | **IMPLEMENTED** | Required by adapters and pipeline validation; session is never inferred from date. |

- **Ordering:** `SymbolEventState` binary-inserts by event time, then provider, sequence ID, and derived identity. Equal event times are deterministic.
- **Duplicates:** A sequence-backed content identity is hashed and deduplicated in state; content-derived identity is used when no sequence is available. The identity set is bounded with event eviction, so an evicted event's identity is no longer remembered in memory. SQL uses a unique event-identity index for persisted idempotency.
- **Late events:** Accepted and inserted into event-time order. No explicit late-event classification or late-event metrics are recorded.
- **Invalid events:** Adapters reject missing structural fields and unsupported/malformed event types. Invalid numeric quote/trade values can enter the canonical model and are marked invalid by feature calculation and detector vetoes; they are not uniformly rejected at ingestion.
- **Gaps/corrections:** **NOT IMPLEMENTED.** No gap record, correction contract, watermark, or backfill workflow.
- **Analysis provenance:** `FeatureSnapshot` and `Opportunity` carry provider/feed/coverage/realtime/channel metadata. Opportunity columns persist these fields through the provenance migration; hosted/persistence tests and local SQL queries verified fixture provenance on 2026-10-25.

## 7. Event-Time State Engine

- **Bounded?** Each `SymbolEventState` is capped by `Scanner:MaximumStateEvents` (default 10,000). Oldest event-time entries are removed and `HasEvictedEvents` marks completeness false. The state dictionaries are keyed by `(Symbol, SessionId)`. The fixture pipeline also retains a separate list of all input events for persistence; that collection is not bounded by the state cap.
- **Indexing:** In-memory sorted `List<MarketEvent>` with binary insertion and a `HashSet<string>` of identities; symbol/session isolation is enforced.
- **Windows:** `MomentumWindowSeconds` defaults to 60 seconds; `SecondaryMomentumWindowSeconds` defaults to 30 seconds and must be positive and shorter. The second ROC uses its own inclusive window; original return/volume and VWAP slope retain the first window. Session VWAP uses retained events in the explicit session.
- **`TimeProvider`:** Registered in Web and passed to live pipeline timing and both live adapters' receive timestamps. Feature/as-of analysis uses event time. Publication lifecycle accepts explicit evaluation time; fixture results are evaluated at event-time `AsOfUtc` to remain deterministic.
- **Event time vs receive time:** Distinct in domain and persistence. Quote age is based on quote event time relative to `AsOfUtc`; live-feed health uses receive time.
- **Future-event leakage:** `GetEventsAsOf` excludes events with event time after the requested as-of; replay and feature tests explicitly cover future-event exclusion.
- **Stale data:** Quote age compared with `MaximumQuoteAgeSeconds` (default 15); live receive age compared with `MaximumLiveFeedStalenessSeconds` (default 30) for feed status.
- **Feed lifecycle:** Live status progresses from Connecting to Live, then Stale by configured receive-age threshold; normal stream completion becomes Offline and exceptions become Error. Lifecycle gate suspends candidates for unhealthy statuses; there is still no reconnect/backfill or gap recovery.

## 8. Feature Engine Status

All implemented calculations are in `FeatureCalculator` (`SymbolEventState.cs`). There is no separate feature registry; all actual calculated values are strongly typed fields of `FeatureSnapshot`.

| Feature | Status | Inputs / output / horizon | Tests | Known limitations |
|---|---|---|---|---|
| Current/reference price | **IMPLEMENTED** | Last trade at/before as-of; decimal reference price. | Momentum and replay tests. | Does not fall back to quote midpoint. |
| Bid/ask spread | **IMPLEMENTED** | Latest quote at/before as-of; spread in basis points. | Invalid/excessive spread detector tests. | No size-weighted/microprice feature. |
| VWAP | **IMPLEMENTED** | Cumulative session trade price × size / total size within retained state. | Momentum/replay tests include VWAP values. | Not a bar-based VWAP; evicted earlier trades make session result incomplete. |
| Price relative to VWAP | **IMPLEMENTED** | Last trade vs VWAP, percent. | Momentum alignment tests. | No reclaim, rejection, or volatility normalization. |
| Session VWAP slope | **IMPLEMENTED, DESCRIPTIVE** | First-to-last cumulative VWAP sample change per elapsed event-time minute in primary momentum window; Rising/Falling/Flat/Insufficient. | Existing exact +1.5/-1.5/0/null slope tests preserved. | Requires two distinct timestamps; retained-session completeness applies. |
| EMA | **IMPLEMENTED, DESCRIPTIVE** | Fast/medium per-trade EMA (5/20 default), spread, and event-time slopes. | Exact SMA seeding, smoothing, periods, slopes, invalid prices, isolation and identity replay. | Non-positive eligible prices invalidate all EMA outputs; retained partial state is indicated separately. |
| Trend strength / trade-point ADX | **IMPLEMENTED, DESCRIPTIVE** | Wilder-smoothed trade-point DMI/ADX with configurable 14-movement default period; scalar trade prices only, no synthesized OHLC bars. | Exact rising/falling/flat/alternating paths, initialization/smoothing, warm-up, ordering, invalid inputs, duplicates, bounded eviction, completeness, identity, JSON and detector neutrality. | Nondirectional; values may reflect partial retained history; not used in setup qualification/scoring. |
| Trend direction context | **IMPLEMENTED, DESCRIPTIVE** | Fixed enum context from EMA spread/slopes, VWAP slope/direction and session price structure; ADX remains separate nondirectional strength. | Agreement, disagreement, zero/flat, missing/invalid, partial retention, ADX independence, identity, JSON, fixture HTTP/SQL and detector neutrality. | Reads only existing retained snapshot inputs; partial results must be read with `HasCompleteSessionData`; no qualification/scoring effect. |
| Momentum / rate of change | **IMPLEMENTED, LIMITED** | Original 60-second and descriptive secondary 30-second default ROC windows; first-to-last percentage change. Secondary ROC is null for fewer than two observations or any non-positive in-window price. | Exact +25%/-25%/0%, insufficient/invalid prices, canonical equal-time ordering, isolation, replay, configuration identity, future-event exclusion. | Two short horizons; no acceleration or new detector inputs. |
| RSI | **NOT IMPLEMENTED** | — | None. | — |
| MACD | **NOT IMPLEMENTED** | — | None. | — |
| Volume | **IMPLEMENTED, LIMITED** | Unchanged primary-window size sum; descriptive nullable `SessionCumulativeVolume` over all retained eligible session trades. | Exact sums, invalid/empty input, overflow, isolation, canonical replay, cutoff, eviction, identity, HTTP/SQL JSON. | Retained partial sum after eviction per `HasCompleteSessionData`; no dollar volume or price/volume divergence. |
| Relative volume | **NOT IMPLEMENTED** | — | None. | No historical time-of-day baseline. |
| Volume acceleration | **NOT IMPLEMENTED** | — | None. | — |
| ATR / volatility | **NOT IMPLEMENTED** | — | None. | — |
| Relative strength vs market | **NOT IMPLEMENTED** | — | None. | No benchmark feed/context. |
| Sector-relative strength | **NOT IMPLEMENTED** | — | None. | No sector classification or benchmark. |
| Session high/low and range position | **IMPLEMENTED (descriptive)** | `FeatureCalculator`, replay and hosted HTTP/SQL tests. | Nullable extrema and `(ReferencePrice - low) / (high - low) * 100m`; null on empty, invalid price/reference, or zero range. | Partial after any eviction; no breakout qualification or detector input. |
| Opening range | **NOT IMPLEMENTED** | — | None. | No exchange calendar/opening-range configuration. |
| Support/resistance structure | **NOT IMPLEMENTED** | — | None. | — |
| Breakout levels | **NOT IMPLEMENTED** | — | None. | — |
| Quote freshness | **IMPLEMENTED** | `AsOfUtc - latest quote EventTimeUtc`; quote validity and age; thresholds drive data quality and veto. | Stale quote test. | No source heartbeat or feed-gap semantics in the snapshot. |

Other snapshot fields include quote validity, trade-price validity, required-data availability, session completeness, and `DataQualityStatus`.

## 9. Milestone A Setup Detectors

| Detector | Status | Direction support | Conditions/evidence/scoring | Veto integration | Tests / limitations |
|---|---|---|---|---|---|
| Momentum Continuation | **IMPLEMENTED, LIMITED** | Long and short; neutral when directional return threshold is not met. | Requires a configured directional return for direction, then scores directional momentum, VWAP alignment, and any positive window volume. Evidence and contradictions are recorded. | Uses stale quote, invalid quote/price, excessive spread, insufficient/incomplete state, and no defensible invalidation checks. | Long/short, watch, veto, and replay tests exist. Price structure and ADX are descriptive only; no composite trend-strength, relative-strength, risk/reward, or liquidity scoring. |
| Confirmed Breakout / Breakdown | **NOT IMPLEMENTED** | — | — | — | No breakout levels, confirmation, or detector tests. |
| VWAP Reclaim / Rejection | **NOT IMPLEMENTED** | — | — | — | VWAP-side alignment is not a reclaim/rejection detector. |
| Failed Breakout / Reversal | **NOT IMPLEMENTED** | — | — | — | No prior breakout state, reversal tracking, or detector tests. |

## 10. Classification and Scoring

- The classification enum contains **Probable Long**, **Probable Short**, **Watch**, and **Avoid / No Trade**.
- In the one detector, any active veto yields `AvoidNoTrade`. Otherwise directional long/short with score at or above `ProbableScoreThreshold` (default 75) yields Probable Long/Short; score at or above `WatchScoreThreshold` (default 35) yields Watch; otherwise Avoid/No Trade.
- Scores add configured points for directional momentum (50), VWAP alignment (35), and positive window activity (15), then clamp to 0–100. The three configured weights must sum to 100. Contradictory evidence is described but **does not subtract points**.
- Thresholds, weights and both ROC windows are in `ScannerConfiguration` and `appsettings.json`; the effective configuration endpoint returns them. Version defaults are `scanner-config-v5`, `features-v5`, and unchanged `momentum-continuation-v1`.
- The UI explicitly labels it “Rules-based setup quality” and disclaims probability/recommendation. **The system does not present score as probability of profit.**
- Other constants include the 5-second live analysis interval and 5,000-item channel capacity in `ScannerPipeline`; the 1 MiB WebSocket message cap is in `MassiveMarketDataSource`; score clamp endpoints are 0 and 100. These operational/value-range constants are not currently configuration values. The scoring logic is intentionally simple but treats any positive window volume as participation support rather than relative/thresholded confirmation.

## 11. Risk and Data-Quality Gates

| Veto / requirement | Implemented? | Configuration / behavior | Test coverage |
|---|---|---|---|
| Stale/connecting/offline/error feed | **IMPLEMENTED as publication gate** | Application publication decision is `Suspended` unless live source status is `Live`; health changes reevaluate current results. This is separate from detector scoring/vetoes. | Deterministic tests cover stale transition, clean disconnect to Offline, and preservation of analytical evidence. |
| Stale quote | **IMPLEMENTED** | Quote age over configured 15 seconds or negative age adds `StaleQuote`. | Covered. |
| Invalid quote | **IMPLEMENTED** | Non-positive bid/ask or ask below bid adds `InvalidQuoteOrPrice`. | Covered. |
| Invalid price/trade data | **IMPLEMENTED** | Any retained trade with non-positive price/size sets invalid price data and detector vetoes. | Covered. |
| Excessive spread | **IMPLEMENTED** | Missing spread or above configured 25 bps adds `ExcessiveSpread`. | Covered. |
| Insufficient liquidity | **NOT IMPLEMENTED** | Quote sizes are stored but no minimum size, dollar volume, or liquidity gate exists. | None. |
| Insufficient feature data | **IMPLEMENTED, LIMITED** | Required trade/window/VWAP/quote data or complete state missing adds `InsufficientRequiredFeatureData`. | Covered. |
| Missing benchmark/context | **NOT IMPLEMENTED** | No benchmark/context input exists. | None. |
| Excessive volatility | **NOT IMPLEMENTED** | No volatility feature/threshold. | None. |
| Poor reward/risk | **NOT IMPLEMENTED** | No target or reward/risk calculation. | None. |
| Opportunity expiration | **PARTIALLY IMPLEMENTED** | Detector sets `ValidUntilUtc`; publication gate returns `Expired` when evaluated after it. No persistent lifecycle history or background expiration worker. | Gate expiry and deterministic fixture-time behavior tested. |
| Structural invalidation | **PARTIALLY IMPLEMENTED** | Textual invalidation uses session VWAP for directional setup; neutral/no VWAP yields veto. No later price monitor changes opportunity status. | Text and no-invalidation behavior covered. |
| Data gap / recovering | **NOT IMPLEMENTED** | No gap state, backfill or recovering veto. | None. |

## 12. Opportunity Model

`Opportunity` contains immutable detector evidence. Separately, the application read model contains a current `PublicationDecision`:

| Field | Present? |
|---|---|
| Symbol / explicit session | Yes |
| `AsOfUtc`, `ValidUntilUtc` | Yes |
| Setup type | Yes, currently “Momentum Continuation” |
| Direction/bias and classification | Yes |
| Quality score (0–100) | Yes |
| Supporting evidence / contradictions / active vetoes | Yes |
| Reference price | Nullable decimal |
| Invalidation | Text condition |
| Targets | **No** |
| Feature-set version | Yes |
| Setup-definition version | Yes |
| Configuration version | Yes |
| Provider/feed/coverage/realtime/channel provenance | Yes |

Publication state is `Active`, `Suspended`, `Expired`, or `NotQualified`, with a structured status reason; it does not rewrite the analytical candidate, evidence, contradictions, or detector vetoes. IDs are deterministic hashes of input/configuration-derived identity. Identical replay preserves identity; changed inputs/configuration can create distinct records. `EfScannerStore` inserts only when the ID is absent and persists evidence separately. Configuration version values are immutable in the store: reusing a version with different JSON throws. Current publication decisions are process-memory projections, not append-only persisted lifecycle revisions; no later-price structural invalidation monitor exists.

## 13. Database Status

- **Engine/configuration:** SQL Server 2022 Developer container via `docker-compose.yml`; development connection string is `localhost,1433`. EF configured in `Program.cs` with SQL Server provider and retry-on-failure.
- **DbContext:** One `MarketOpportunityDbContext` in Infrastructure.
- **Entities/tables:** `market_event`, `feature_snapshot`, `opportunity`, `opportunity_evidence`, and `configuration_snapshot`.
- **Persistence:** `EfScannerStore` writes canonical events, JSON feature snapshots, opportunity attributes, evidence/contradictions/veto/invalidation, and immutable configuration JSON. It deduplicates by event identity and opportunity/snapshot IDs.
- **Migrations:** `20261023190000_AddExplicitSessionIdentity` adds non-null `SessionId` with `legacy-unknown` for existing rows and symbol/session/time indexes. `20261024232400_AddOpportunityMarketDataProvenance` adds provider/feed/coverage/realtime/channel columns using idempotent SQL for the development startup path. Both migration tests passed; persistence-test setup now also migrates existing test schemas.
- **Indexes:** Unique event identity; market event `(Symbol, SessionId, EventTimeUtc)`; feature snapshot `(Symbol, SessionId, AsOfUtc)`; opportunity `(Symbol, SessionId, AsOfUtc)`; evidence by opportunity.
- **Local config:** Docker SQL Server and Development configuration are present. README/provider docs describe User Secrets for Alpaca and Massive; credentials are absent from checked-in settings. A local-only SQL development password default is present in Docker Compose; it should not be reused outside disposable local development.
- **Azure SQL:** Provider/mappings are SQL Server compatible, but Azure SQL configuration, schema deployment, and verification are absent.
- **Intentionally absent logical tables:** No separate instrument, market session, trade/quote tables, aggregate bar, data gap, regime snapshot, agent assessment, opportunity outcome, paper observation, model registry/evaluation, system incident, or configuration audit tables. The single `market_event` table stores both trades and quotes. Outcomes/ML are future milestones.
- **Can fixture/event/opportunity data be persisted and retrieved?** SQL persistence/migration tests passed, and direct SQL queries matched the running fixture API's four opportunity IDs, four snapshot IDs, and 26 events. The API and dashboard **do not retrieve persisted history**; they serve current in-memory `ScannerState`. Persistence does not imply historical read capability.

## 14. Blazor Status

- **Hosting/render mode:** Blazor Web App, Interactive Server, `Routes` in `App.razor`; documented by ADR-001.
- **Pages/components:** Dashboard `Home.razor`, template `Counter`, `Weather`, `Error`, `NotFound`, routes, main layout/nav, reconnect modal.
- **Dashboard state:** Shows provider/feed and coverage capabilities, feed state, event time, symbol/session results, price, VWAP, spread, quote freshness/data quality, score, evidence, contradictions, analytical vetoes, invalidation, expiry, analysis versions, and publication status/reason.
- **Application integration:** Injects singleton `ScannerState` and `ScannerConfiguration`; subscribes to `ScannerState.Changed` and rerenders. Does not query EF directly.
- **SignalR/realtime:** Interactive Server circuits provide the Blazor connection. `ScannerState.Changed` updates connected UI circuits in-process. No separate application hub/events are implemented.
- **Errors/loading/data health:** Empty/loading state and feed/freshness labels are present. `ScannerRunnerService` records Error status and structured exception log if processing fails. Unhealthy live state suspends existing candidates and displays the reason; no gap-recovery state, benchmark health, or deep health dashboard.
- **Observed browser behavior (2026-10-25):** The local Fixture dashboard showed Recorded status, Synthetic feed/coverage, and all four symbol/session cards. The 2026-10-22 MOE long/QNT short results were Expired; the 2026-10-23 MOE short/QNT long results were Active with reasons. Provenance, evidence, vetoes, and versions were visible. Browser reload established a `/_blazor` WebSocket. This is a local fixture walkthrough, not live-provider/error-recovery coverage.

## 15. API Status

All routes are GET endpoints in `src/MarketOpportunity.Web/Program.cs`. The exact hosted fixture HTTP/SQL regression passed twice against the same database on 2026-10-25. A separate, earlier running-app/browser check verified health, status, list, all four details, and all four session-specific snapshots; that walkthrough was not repeated for the test-only slice.

| Method / path | Purpose | Implementation | Working / tested |
|---|---|---|---|
| `GET /health` | ASP.NET Core health response. | `AddHealthChecks` + `MapHealthChecks`; no database/provider-specific checks registered. | Local runtime returned Healthy. |
| `GET /api/v1/system/status` | Current feed/source/event status and capabilities. | Reads `ScannerState`. | Hosted test passed; local runtime reported Recorded / 26 events. |
| `GET /api/v1/opportunities` | Current opportunities sorted by as-of, with publication decision. | Reads in-memory results; not SQL. Response shape wraps analytical `Opportunity` with current publication status/reason. | Hosted test passed; local API IDs/provenance matched SQL. |
| `GET /api/v1/opportunities/{id}` | Current in-memory opportunity and publication decision. | Reads `ScannerState`, returns 404 if absent. | Hosted test passed; all four local detail responses returned 200 and matching IDs. |
| `GET /api/v1/symbols/{symbol}/snapshot` | Latest in-memory feature snapshot; optional `sessionId` query narrows session. | Reads `ScannerState`. | Hosted test passed; all four local session-specific responses returned 200 and SQL-matching IDs. |
| `GET /api/v1/configuration/effective` | Effective scanner configuration. | Returns singleton `ScannerConfiguration`. | Hosted HTTP test asserts the entire payload, including secondary window. |

`GET /api/v1/market/regime`, `/api/v1/symbols/{symbol}/features`, and any standalone feature/history API are not present. No app-level SignalR hubs or named hub events are implemented.

## 16. Configuration System

`ScannerConfiguration` is bound from the `Scanner` section in `appsettings.json`, validated at startup, and returned by the effective-configuration endpoint.

| Parameter | Default | Location / notes |
|---|---:|---|
| Configuration version | `scanner-config-v12` | No new parameter; fixed trend-direction rules; current effective configuration remains stored as immutable JSON in `configuration_snapshot`. |
| Feature set version | `features-v12` | Adds non-null descriptive `SessionTrendDirection`; retains nullable trade-point ADX and all prior feature outputs. |
| Structure pivot strength (fixed) | 2 observations on each side | Versioned feature rule, not a configurable property; two later eligible trades confirm a candidate. No elapsed-time threshold or tolerance. |
| Trend-strength period | 14 trade movements | `TrendStrengthPeriod`; positive integer; first ADX after 2 × N eligible price observations. |
| Setup definition version | `momentum-continuation-v1` | Scanner config and opportunity. |
| Maximum events per symbol/session | 10,000 | Bounds event state, not all pipeline collections. |
| Momentum window | 60 seconds | Short-horizon return/volume and cumulative session VWAP/EMA-slope window. |
| Secondary momentum window | 30 seconds | `SecondaryMomentumWindowSeconds`; positive and strictly less than primary window. Descriptive `SecondaryHorizonReturnPercent` only. |
| Fast EMA period | 5 trades | `EmaFastPeriod`; positive observation count, strictly below medium period; SMA seed then alpha `2/(N+1)` per trade. |
| Medium EMA period | 20 trades | `EmaMediumPeriod`; positive observation count greater than fast period; SMA seed then alpha `2/(N+1)` per trade. |
| Maximum quote age | 15 seconds | Quote staleness veto. |
| Maximum live feed staleness | 30 seconds | Changes feed health; unhealthy live health suspends publication in the Application gate. |
| Maximum spread | 25 basis points | Spread veto. |
| Minimum directional return | 0.05% | Long/short direction threshold. |
| Minimum required trades | 3 | Data sufficiency. |
| Watch / Probable thresholds | 35 / 75 | Classification. |
| Opportunity duration | 120 seconds | Sets `ValidUntilUtc`; publication gate reports Expired at evaluation time, without a persisted lifecycle history/worker. |
| Momentum / VWAP / activity weights | 50 / 35 / 15 | Must sum to 100. |

Configuration snapshots are immutable per version in SQL. Feature/opportunity deterministic IDs include the versioned input. There is no external configuration administration UI or effective version history API beyond current settings. Other hard-coded analytical/operational values include the 5-second live analysis interval, channel capacity 5,000, and 1 MiB provider message cap; no dynamic session/calendar configuration exists.

## 17. Observability

| Capability | Status | Evidence / implementation |
|---|---|---|
| Structured logging | **PARTIALLY IMPLEMENTED** | Standard `ILogger` structured log in `ScannerRunnerService` for unexpected processing exception and completion counts. Default ASP.NET logging configuration exists. |
| OpenTelemetry tracing | **PARTIALLY IMPLEMENTED** | ASP.NET Core instrumentation only. |
| OpenTelemetry metrics | **PARTIALLY IMPLEMENTED** | ASP.NET Core and runtime instrumentation only. |
| OTLP export | **PARTIALLY IMPLEMENTED** | Conditional exporter if `OTEL_EXPORTER_OTLP_ENDPOINT` is set. |
| Scanner-specific metrics/spans | **NOT IMPLEMENTED** | No event, feature, scoring, veto, DB, channel, or UI spans/counters/histograms found. |
| Health checks | **PARTIALLY IMPLEMENTED** | `/health` uses default health checks; no SQL or feed health check registration. |
| Application Insights / Azure Monitor | **NOT IMPLEMENTED** | No Azure-specific integration/configuration found. |

No project-specific metrics or spans exist for the minimum observability list in the master specification.

## 18. Azure Readiness

| Capability | Status | Evidence / limitation |
|---|---|---|
| Bicep | **NOT IMPLEMENTED** | No Bicep artifacts. |
| Azure App Service | **NOT IMPLEMENTED** | No app, deployment profile, or hosting settings. |
| Azure SQL Database | **NOT IMPLEMENTED for deployment** | SQL Server provider is used, but only local SQL Server connection is configured. |
| Key Vault | **NOT IMPLEMENTED** | No provider/resource integration. |
| Application Insights | **NOT IMPLEMENTED** | No Azure exporter/SDK or resource configuration. |
| Azure Monitor | **NOT IMPLEMENTED** | No Azure Monitor setup. |
| Entra ID | **NOT IMPLEMENTED** | No authentication/authorization integration. |
| Azure DevOps Pipeline | **NOT IMPLEMENTED** | No Azure pipeline file. |

**Local-development readiness:** Partially prepared: .NET 10 solution, SQL Server Docker Compose service, development launch profiles, fixture default, and User Secrets instructions. A running SQL Server is required for web startup and SQL integration tests.  
**Azure-deployment readiness:** Not ready; no infrastructure, hosted secret store, identity, deployment pipeline, Azure database configuration, or Azure observability implementation.

## 19. Architecture Decision Records

| ADR | Decision | Status / consequences |
|---|---|---|
| [ADR-001](docs/adr/ADR-001-blazor-render-mode.md) | Blazor Web App with Interactive Server; no separate app SignalR hub for current checkpoint. | Accepted. Server keeps analysis/credentials off browser; circuit transport is used. |
| [ADR-002](docs/adr/ADR-002-ef-sqlserver-conventions.md) | EF Core 10 and SQL Server; Infrastructure owns persistence; singular snake_case tables; development `EnsureCreated`, reviewed migrations for existing schema. | Accepted. Production migration orchestration remains follow-up. |
| [ADR-003](docs/adr/ADR-003-in-process-event-transport.md) | Fixture runs direct/in-process; use bounded `Channel<MarketEvent>` only for continuous producer/consumer. | Accepted. Live pipeline uses bounded channel with `Wait`; no distributed broker. |
| [ADR-004](docs/adr/ADR-004-market-event-retention.md) | Persist normalized trade/quote events; no raw vendor payload retention until reviewed. | Accepted. No raw-source reconstruction; retention/licensing duration still needs decision before live use beyond evaluation. |
| [ADR-005](docs/adr/ADR-005-market-data-provider-criteria.md) | Massive first live trade/quote provider; Alpaca approved alternative, not broker authorization. | Accepted. Fixture remains default; provider entitlements and terms must be confirmed. |

The requested render mode, EF/SQL Server, `Channel<T>`, retention, and provider-selection decisions are all documented.

## 20. Test Status

### 2026-10-26 retained-session trend-direction evidence (current)

Current results and the SQL gate outcome are summarized in the latest analysis snapshot above. See that snapshot for exact passing totals and the separately recorded initial focused-test failures.

The output is non-null `TrendDirectionContext SessionTrendDirection`, serialized as `sessionTrendDirection`. Enum values are `Bullish`, `Bearish`, `Neutral`, `Conflicting`, `Insufficient`, and `Invalid`. Inputs are the signed fast/medium EMA spread and both signed EMA slopes; session VWAP slope and direction; session price structure; and existing ADX value/status. Positive EMA values vote bullish, negative bearish, and zero is neutral. VWAP Rising/Falling votes bullish/bearish, Flat is neutral, Insufficient contributes no vote. HigherHighHigherLow/LowerHighLowerLow vote bullish/bearish, Flat is neutral, Mixed forces conflict. ADX is validated as nondirectional strength; its numeric magnitude never votes.

Precedence is invalid/inconsistent supplied input → Invalid; any opposing bullish and bearish votes or Mixed structure → Conflicting; otherwise any one-sided vote → Bullish/Bearish; available directional inputs with only zero/flat signals → Neutral; no available directional inputs → Insufficient. Null EMA/slope, VWAP Insufficient, null structure and ADX Insufficient do not vote. Non-positive available EMA values, incomplete/inconsistent EMA inputs, VWAP enum/slope mismatch, unknown directional enums, invalid ADX status/value, and ADX InvalidPriceData return Invalid. If there is only partial signal availability, use available votes; neutral signals do not cancel directional votes.

This uses fixed sign and existing enum states, so there is no new configuration property or configurable threshold. Defaults advance `features-v11` / `scanner-config-v11` to `features-v12` / `scanner-config-v12`; `momentum-continuation-v1` remains unchanged. The enum is non-null in every snapshot and participates explicitly in deterministic identity, including all six states. HTTP uses the existing string-enum convention; persisted snapshot JSON uses its existing numeric enum encoding. `HasCompleteSessionData` does not change direction resolution; when false, the context describes retained partial features, not complete-session direction. Same-symbol/session and event-time cutoff are inherited from the existing `FeatureSnapshot` and `AsOfUtc`; no receive/wall-clock, ordering, duplicate, retention, or completeness semantics change.

Offline focused tests cover bullish/bearish agreement; EMA, VWAP and price-structure disagreements; flat and conflicting signals; ADX magnitude independence; no/mixed availability; invalid/mismatched states; complete versus retained-partial context; enum round trips; identity; and byte-identical serialized detector output as descriptive signals vary. The hosted fixture's exact independently-derived expected context is MOE/2026-10-22 Bullish, MOE/2026-10-23 Bearish, QNT/2026-10-22 Bearish and QNT/2026-10-23 Bullish. Hosted assertions read that enum from HTTP string JSON and raw persisted numeric JSON alongside existing provenance/completeness/identity checks. Persistence asserts original and changed input produce new immutable records without rewriting original snapshot/decision/evidence.

### Historical 2026-10-26 retained-session trade-point ADX evidence (superseded)

Added `SessionAdx` and `SessionAdxStatus` as descriptive outputs derived from bounded retained trades. The observation contract is one scalar trade price per canonical observation; no OHLC bars are synthesized. For adjacent prices, `TR=abs(P[t]-P[t-1])`, `up=P[t]-P[t-1]`, `down=P[t-1]-P[t]`; `+DM=up` only if `up>down && up>0`, `-DM=down` only if `down>up && down>0`, otherwise zero. Equal adjacent prices therefore have zero TR/DM.

`TrendStrengthPeriod` defaults to 14 trade movements and must be positive. Seed arithmetic means of TR/+DM/-DM from the first N movements; update with Wilder's `prior+(current-prior)/N`. Calculate `+DI=100*smoothed(+DM)/smoothed(TR)`, `-DI=100*smoothed(-DM)/smoothed(TR)`, and `DX=100*abs(plusDI-minusDI)/(plusDI+minusDI)`. Zero TR or a zero DI sum yields DX=0. Initial ADX is the mean of the first N DX values, then uses the same Wilder recurrence. All math uses decimal. The first value requires 2×N eligible price observations. `SessionAdx` is nullable decimal; no eligible history or fewer than 2×N trades yields null/`Insufficient`; any eligible non-positive price yields null/`InvalidPriceData`. Available values are nondirectional 0–100 strength.

Only retained same-symbol, explicit-session `TradeEvent`s with `EventTimeUtc <= AsOfUtc` contribute, in canonical ordering; no elapsed-time interval is applied. Quotes, sizes, receive time and wall-clock time are excluded. Equal-time trades count separately in canonical order. `HasCompleteSessionData=false` does not null a warmed-up value but marks it as retained-partial. Future arrivals can evict eligible retained history and change a historical-as-of output. Null/value and status explicitly participate in deterministic feature identity; the positive period is in serialized versioned configuration. Versions are `features-v11` / `scanner-config-v11`; setup remains unchanged at `momentum-continuation-v1`. No detector, scoring, veto, publication, UI, provider, or prior feature behavior changed.

Replay coverage adds exact rising/falling/flat/non-directional and smoothing paths; period-2 and default-period initialization; size/quote and invalid-price cases; inclusive AsOf and old retained trades; equal timestamps/canonical ordering; receive-time changes; duplicate/conflicting first occurrence; symbol/session isolation; eviction/completeness; JSON null/value and identity; positive-period validation; and byte-identical serialized opportunity output as ADX value/status varies. Hosted fixture observations are independently 4–7 trades per group, below 28-observation default warm-up; HTTP has `sessionAdx:null`/`Insufficient`, and raw SQL JSON has null/value status 1 for all four groups. Persistence tests assert original ADX 100 and changed-input ADX 57.14285714285714285714285714, while asserting changed inputs create distinct immutable snapshots/opportunities and do not rewrite the original snapshot, decision, or evidence.

**Passing validation (all runs 0 failures/0 skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **119 passed, 0 failed, 0 skipped** |
| Offline full, before SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **183 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command on same database without reset | **1 passed, 0 failed, 0 skipped** |
| Persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Offline full, after SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **183 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **0 warnings, 0 errors** |

SQL guard: `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. The existing Compose SQL Server was already healthy and the existing dedicated `MarketOpportunityEngine_IntegrationTests` database was verified with a read-only query. A process-only connection was built from the container credential in memory and guarded by `SqlConnectionStringBuilder`; no SqlClient assembly was loaded in the parent process launching `dotnet`. Both hosted runs used the same database without reset; fixture rows were retained, no configured database was dropped, and migration tests used only unique temporary databases. Temporary overrides and credential references were cleared. Browser/live-provider/User Secrets/Azure checks were not run.

**Failed attempts, not passing evidence:** Initial focused compilation required adding the new positional fields to a test fixture. Two later focused assertions had an incorrect decimal expectation and an identity test with unintended VWAP change; both were fixed before 119/119 passed. The first persistence/migration execution had **2 passed / 1 failed** due to the final decimal rounding digit; corrected expected output then passed 3/3. A separate PowerShell child SqlClient guard failed with `Microsoft.Data.SqlClient is not supported on this platform` before tests, and one runner orchestration attempt stopped after preflight without launching tests. These failures are runner/expectation issues, not evidence of SQL unavailability; the final .NET tests passed their in-process connection-string guard.

### Historical 2026-10-26 retained-session price-structure evidence

Nine replay cases were added for the fixed two-left/two-right pivot rule, rising/falling/mixed/flat structure, confirmation and warm-up, equal-price selection and exact decimal comparisons, equal-time canonical ordering, size/quote/invalid-price eligibility, `AsOfUtc`, duplicates/receive-time independence, symbol/session isolation, eviction/completeness, JSON null/value round trips, deterministic identity, and detector neutrality. The existing replay assertions remain intact.

**Passing validation (0 failures/0 skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **107 passed, 0 failed, 0 skipped** |
| Offline full | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **171 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command on the same database without reset | **1 passed, 0 failed, 0 skipped** |
| SQL persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Post-SQL full units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **171 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

The SQL connection override was absent. The existing healthy Compose SQL Server and already-existing dedicated `MarketOpportunityEngine_IntegrationTests` database were used. A read-only container-local query verified the database, while an isolated encoded PowerShell 7 child loaded the built Windows runtime SqlClient assembly via `Assembly.LoadFrom` and validated/built the test connection with `SqlConnectionStringBuilder` indexers. The SqlClient runtime was not loaded into the PowerShell child launching `dotnet`. Temporary credentials/overrides stayed in process memory and were cleared; fixture records were retained; no database was reset/dropped and no service was started/stopped. Migration tests used unique temporary databases only.

**Failed attempts, not passing evidence:** Early focused attempts exposed invalid decimal `InlineData`, a positional `FeatureSnapshot` constructor missing the new field, incorrect swing paths, incomplete identity assertions, and a missing JSON converter using; they were corrected before the final 107/107 run. Pre-test SQL guards failed at different setup stages (PowerShell type/assembly resolution, script parsing, and `DllNotFoundException` opening SqlConnection from PowerShell). None launched the primary SQL test sequence. The successful primary gate used builder-only validation plus the read-only container-local database query. A later redundant persistence/migration rerun reached the test runner but failed **0/3** because its temporary override caused `sa` login failures; the read-only database query still passed. This is a runner/credential setup failure, not SQL-unavailability evidence, and is separate from the passing primary sequence. No browser, live-provider, User Secrets, or Azure verification was run.

### Historical 2026-10-25 descriptive retained-trade EMA evidence

Added nullable `FastEma`, `MediumEma`, `FastMediumEmaSpreadPercent`, `FastEmaSlopePerMinute`, and `MediumEmaSlopePerMinute` to `FeatureSnapshot`; calculations use the existing bounded `SymbolEventState` and its canonical ordering. Only retained same-symbol/explicit-session trades at or before `AsOfUtc` contribute. EMA updates once per observation, independent of size and elapsed time. Periods default to fast 5 and medium 20 trades, are positive integers, and require fast < medium. The first value is the SMA seed of N positive trade prices; subsequent values use `price * alpha + priorEMA * (1-alpha)`, where `alpha=2/(N+1)`. Fewer than N observations yields null. Any eligible non-positive price makes every EMA output null; quotes do not contribute.

Spread is `(fast-medium)/medium*100m`, null until both EMA values exist. Each slope is the signed EMA change per elapsed event-time minute across the first and last initialized timestamp samples in the inclusive `[AsOfUtc-MomentumWindowSeconds, AsOfUtc]` interval (60 seconds by default); all trades at one timestamp update in canonical order, and the last canonical EMA at that timestamp is the sample. Earlier trades can warm up an EMA outside the slope interval. Fewer than two distinct timestamp samples yields null; equal endpoint values yield zero. Read outputs with `HasCompleteSessionData`; false means non-null values may describe only retained partial state. Future observations can affect an as-of value if they evict eligible retained state.

All five outputs use nullable decimal JSON fields `fastEma`, `mediumEma`, `fastMediumEmaSpreadPercent`, `fastEmaSlopePerMinute`, and `mediumEmaSlopePerMinute`, including explicit JSON nulls; each value is explicitly included in deterministic feature identity. Configuration/version advance from `features-v8` / `scanner-config-v8` to **features-v9 / scanner-config-v9**; setup remains **momentum-continuation-v1**. No detector, opportunity, scoring, qualification, classification, publication, veto, UI, provider, schema, or `App.razor` behavior changed.

**Passing validation (0 failed/0 skipped):**

| Order | Exact command | Result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **98 passed, 0 failed, 0 skipped** |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| SQL 2 | Same hosted fixture command, same database without reset | **1 passed, 0 failed, 0 skipped** |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **162 passed, 0 failed, 0 skipped** |
| Build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

Hosted HTTP and raw persisted SQL JSON assert exact independently-derived fixture values: MOE / fixture-2026-10-22 fast EMA **150.218** from prices `149.90, 150.08, 150.23, 150.40, 150.48`; QNT / fixture-2026-10-23 fast EMA **80.176** from `79.80, 80.00, 80.20, 80.40, 80.48`. Other fixture fast values are null; all medium, spread, and slope values are null because fewer than 20 trades and fewer than two initialized fast samples exist in the slope interval. The SQL persistence test confirms new input creates a new snapshot/decision with fast EMA **102.1** and slope **12 price/minute**, without rewriting the original records.

The connection variable was absent. The already-running healthy Compose SQL Server and only its dedicated `MarketOpportunityEngine_IntegrationTests` database were reused; a read-only `sqlcmd` query verified the database, and an isolated encoded PowerShell 7 child validated the catalog with the built Windows runtime `SqlConnectionStringBuilder` via `Assembly.LoadFrom` and indexer setters. The parent launching `dotnet` did not load SqlClient. Process-only overrides were cleared; fixture records and the configured test database were not reset/dropped; migration tests used unique temporary databases. Browser/live-provider/User Secrets/Azure checks were not run.

**Failed attempts, not passing evidence:** Initial focused runs exposed a nullable conditional compile error, a positional test fixture missing fields, and incorrect EMA test expectations/session helper inputs; all were corrected before the passing focused run. SQL guard-runner attempts involving encoded command construction and a failed login stopped before SQL tests; the successful subsequent guard used the existing healthy database, and the required complete SQL sequence passed. These unsuccessful guard attempts are not classified as SQL test failures or evidence the server was unavailable.

### 2026-10-25 descriptive retained-session range-position evidence (historical)

Added nullable decimal `SessionRangePositionPercent` to the existing `FeatureSnapshot` and `FeatureCalculator`. Formula: `(ReferencePrice - SessionLowPrice) / (SessionHighPrice - SessionLowPrice) * 100m`, using decimal arithmetic and only retained same-symbol/explicit-session trades with `EventTimeUtc <= AsOfUtc`; the reference remains the latest eligible retained trade in existing deterministic order. The existing extrema are reused; no additional state or eligibility path was introduced. Values are not clamped or rounded and do not use either ROC window.

Empty state, unavailable/non-positive reference, unavailable extrema, and zero-width range return null. One trade and equal-price trades therefore return null; the feature does not require `MinimumRequiredTrades`. Any eligible non-positive price nulls extrema and range position even when outside both ROC windows. Sizes neither weight nor filter the range: zero/negative-size trades with positive prices still contribute to extrema/reference/position. Existing `IsPriceDataValid`, data quality, volume, detector, veto, publication, rolling volume, ROC, and exact VWAP-slope behavior remain unchanged. Read position with sticky `HasCompleteSessionData`; false means retained partial-session range, while true does not prove upstream session-start coverage. Quote eviction remains sticky; future arrivals may evict earlier state.

`sessionRangePositionPercent` is serialized for both values and null and explicitly participates in deterministic feature identity. Defaults advance from `features-v7` / `scanner-config-v7` to **features-v8 / scanner-config-v8**; `momentum-continuation-v1` remains unchanged. Prior configurations, snapshots, and decisions remain immutable; SQL persistence verifies a later input creates a distinct snapshot/decision without changing the original snapshot JSON, opportunity fields, or evidence. No schema migration was required.

**New successful validation (all test runs passed, 0 failures/skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **83 passed, 0 failed, 0 skipped** |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **147 passed, 0 failed, 0 skipped** |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| SQL 2 | Same hosted fixture command | **1 passed, 0 failed, 0 skipped**, same database and no reset |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **147 passed, 0 failed, 0 skipped** after the final replay test was added |
| Build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

The hosted tests assert exact position in both HTTP snapshot payloads and raw persisted SQL snapshot JSON. Independently derived fixture expectations: MOE-22 **100**, MOE-23 **0**, QNT-22 **0**, QNT-23 **100**, with retained reference/extrema values and `HasCompleteSessionData=true`. Replay coverage includes low/high/interior, rising/falling, extrema outside both ROC windows, empty/single/equal/below-minimum-trade ranges, invalid prices inside/outside windows, positive prices with invalid sizes including the latest reference, quote exclusion, isolation, canonical first duplicates/conflicting payloads, reordered/equal-time/receive-time replay, inclusive cutoff/future extreme and invalid exclusion, eviction/partial/null/sticky quote completeness/future-arrival eviction, identity mutation and JSON round trips, and detector output invariance at null/0/25/100. All prior exact volume, extrema, ROC, VWAP, payload, negative-lookup, canonical-event, configuration-version, and immutability assertions remain.

The SQL connection variable was absent. The pre-existing Compose SQL Server was confirmed healthy, and only its dedicated `MarketOpportunityEngine_IntegrationTests` database was used. A PowerShell 7 encoded child loaded the built Windows runtime `Microsoft.Data.SqlClient` assembly via `Assembly.LoadFrom` to validate `InitialCatalog` with `SqlConnectionStringBuilder`; the isolated child returned no connection text, and the parent launching `dotnet` loaded no SQL-client runtime assembly. Temporary process overrides/password references were cleared; fixture rows and the configured test DB were retained; no service was started/stopped/reset and the configured DB was not dropped. Migration tests used only uniquely named temporary databases.

**Failed attempts, not passing evidence:** Initial focused replay run was **80 passed / 2 failed** due to the test expecting 25 instead of the exact 50 midpoint for range 100..150, and a version-change test reusing the new `scanner-config-v8` default. The expected value and changed test version were corrected; the successful 83-case focused run followed. Guard setup attempts that loaded the root SQL-client assembly, passed the DLL path as a script, or assigned an unsupported builder property failed before SQL tests ran. The successful isolated PowerShell 7 guard correctly invoked an encoded child, loaded the built Windows runtime assembly using `Assembly.LoadFrom`, and populated `SqlConnectionStringBuilder` through its indexer. These were runner/guard setup failures, not evidence of SQL unavailability. No browser, live-provider, User Secrets, or Azure verification was run.

### 2026-10-25 descriptive retained-session high/low evidence (historical)

Versions advance from `features-v6` / `scanner-config-v6` to **features-v7 / scanner-config-v7**; **momentum-continuation-v1** is unchanged. The exact extrema contract is at the top of this report and in README. Both nullable decimal fields are serialized with existing web naming and explicitly included in the deterministic feature-value identity. No persistence schema/ADR decision changes were needed.

**New successful validation, in execution order (all exit 0):**

| Order | Exact command | New result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **72 passed, 0 failed, 0 skipped** |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **136 passed, 0 failed, 0 skipped** |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| SQL 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**, same database, no reset |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **136 passed, 0 failed, 0 skipped** |
| Final build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

The 26 added replay cases cover extrema across all retained trades outside/inside both windows; empty/single/equal prices below the detector minimum; zero/negative first/intermediate/last prices inside and outside both windows; positive prices with invalid sizes retaining extrema but preserving vetoes; quote exclusion; symbol/explicit-session isolation; canonical first duplicate with conflicting payloads; reordered/equal-time input and receive-time independence for stable IDs; inclusive cutoff with future extreme/invalid prices; prior-extrema eviction, partial/empty/invalid retained results, conservative sticky quote eviction, and future arrivals evicting historical state. Serialization/exact identity assertions now independently mutate each new field (including null representation), preserving the explicit volume identity assertion. Detector comparisons vary each extrema field independently, including null, and compare the full serialized opportunity output.

The exact fixture constants at the top were inspected from trade prices, not generated solely by the calculator under test. Hosted HTTP and raw persisted SQL snapshot JSON both assert them alongside all exact prior volume/completeness checks. Existing four positive snapshot/detail/list payloads, four absent-resource `404`s, 26 canonical-event identities/fields, effective configuration, analytical evidence/provenance, version and immutable configuration/snapshot assertions remain in place. Original SQL extrema **103/100** remain unchanged after later input generates **103.50/100**. The volume assertions **100 then 800**, both ROC horizons/validation and exact VWAP-slope assertions were preserved, not replaced or weakened.

**Safety/cleanup:** The connection variable was absent. Used the request-authorized existing healthy Compose SQL Server and dedicated `MarketOpportunityEngine_IntegrationTests`, plus only migration tests' unique temporary databases. Credentials were captured and the connection built in process memory. An isolated encoded PowerShell 7 child used `Assembly.LoadFrom` and `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` to require non-empty `InitialCatalog` containing `test` case-insensitively. No SQL-client runtime was loaded into the parent launching `dotnet`; no SQL connection was opened from PowerShell. Temporary overrides/credential references were cleared. No helper/task files were needed. No server or configured test database was started/stopped/reset/dropped; fixture rows were retained. Hosted factories disposed; pre-existing container remained running/healthy.

**Failed attempt, not passing evidence:** Initial focused run: **66 passed, 6 failed, 0 skipped** (72 total). Six theory rows used incorrect test-only expected volume **130** instead of **160**; older trade sizes were 10+20+30, plus existing volume 100. Corrected that expected constant only, then ran the complete successful sequence above. No SQL/assembly/runner failures occurred in this slice. Prior runner/native-SNI failures below remain historical.

**Unavailable/not-run:** No remaining SQL verification blocker. No new browser walkthrough, live-provider connection, provider User Secrets or Azure evidence. App UI and `App.razor` unchanged. Full Milestone A remains incomplete.

### Earlier 2026-10-25 descriptive session cumulative-volume evidence (historical)

**Contract:** `SessionCumulativeVolume` is nullable `long`, summing only retained trades for the state's symbol/explicit session with `EventTimeUtc <= AsOfUtc`, including trades outside both ROC horizons. No trades or any non-positive size yields null, not zero; one valid size is sufficient regardless of `MinimumRequiredTrades`. No trade is silently filtered for invalid size. Positive sizes remain summable even with invalid prices, while existing `IsPriceDataValid`/quality/veto behavior remains unchanged. Quotes and receive/system time never supply volume. Existing checked long summation throws `OverflowException`, never wrapping/clamping. `ShortHorizonVolume` is unchanged.

`HasCompleteSessionData` is reused, not redefined: no eviction means the non-null sum covers the observed retained session; any eviction makes a non-null sum partial. The flag is sticky/conservative even for quote eviction, not proof of upstream session-start coverage. No eligible retained trades or invalid sizes still yields null. Future events cannot affect prior values/identity if retained eligible events/completeness are unchanged; future arrivals can evict prior events, so bounded state cannot reproduce arbitrarily old snapshots. There is no backfill.

Versions advance from `features-v5` / `scanner-config-v5` to **`features-v6` / `scanner-config-v6`**. The setup stays **`momentum-continuation-v1`**. Volume participates explicitly in deterministic feature-value identity and JSON serialization. Existing configuration immutability/version checks, retained canonical first-occurrence deduplication/order, both ROC horizons/validation, exact VWAP-slope tests, detector/publication/veto behavior, SQL/migration assertions, and all hosted positive/negative lookups remain intact. No schema, dependency, UI (including `App.razor`), provider, or infrastructure change.

**New successful validation, in order:**

| Order | Exact command | New result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **46 passed, 0 failed, 0 skipped**, exit 0. |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **110 passed, 0 failed, 0 skipped**, exit 0. |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**. |
| SQL 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**, same database without reset. |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped**. |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **110 passed, 0 failed, 0 skipped**. |
| Final build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded; no warnings/errors reported**. |

The final sequence completed successfully with fail-fast exit-code checks. Sixteen added replay cases assert session accumulation outside both windows; unchanged rolling volume; 0/1/2-trade input; zero/negative sizes; positive sizes with invalid prices; exact `long.MaxValue` and overflow; quote exclusion; symbol/session isolation; duplicates, equal timestamps, reordered input and changed receive times; inclusive cutoff and excluded invalid/overflowing future sizes; partial/unavailable retained sums after eviction; bounded historical-replay limits; raw snapshot serialization/exact identity contribution; and unchanged detector output. Prior slope and ROC exact assertions remain.

Hosted HTTP snapshots and raw persisted SQL JSON assert **5600 / 5450 / 5050 / 5650** shares for MOE-22/MOE-23/QNT-22/QNT-23, all with complete retained state. Four snapshot/opportunity identities and payloads, all 26 canonical events, classifications/publication/evidence/provenance, effective configuration, and four exact absent-resource `404`s still pass on both runs. SQL persistence additionally asserts original volume **100** remains unchanged after later input produces **800**; same-version configuration changes remain rejected.

**Safety/cleanup:** The environment variable was absent. Reused the pre-existing healthy Compose SQL Server, targeting only `MarketOpportunityEngine_IntegrationTests` (plus migration tests' uniquely named temporary databases). A PowerShell 7 child loaded the SQL-client runtime using `Assembly.LoadFrom` and validated the non-empty case-insensitive `test` catalog via `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` before any SQL-backed test. No SQL connection was opened from PowerShell and no SQL runtime assembly was loaded into the parent launching `dotnet`. Credentials stayed in process memory, were not printed/saved, and temporary overrides were cleared/restored. No server was started/stopped or configured database dropped/reset; fixture rows were retained. Final status: `sqlserver running healthy`. Hosted test factories disposed. Temporary VS Code task and session scripts were removed.

**Failed attempts in this slice, not passing validation evidence:** First Windows PowerShell child `Add-Type` guard failed dependency resolution (`System.Memory`, `Microsoft.Identity.Client`, `Azure.Core`); it opened no SQL connection and no tests ran. Subsequent terminal-runner calls returned `program not found`, including a trivial shell probe. Recovery ran the same documented sequence through a temporary VS Code task and isolated PowerShell 7 child. These were runner/assembly failures, not evidence SQL Server was unavailable.

**Unavailable/not-run boundaries:** No remaining SQL verification blocker. No new browser walkthrough, live-provider connection/User Secrets, or Azure evidence. Full Milestone A remains incomplete. Historical results/failures below are preserved and are not new runs.

### Earlier 2026-10-25 second short-horizon ROC evidence (historical)

Added `SecondaryHorizonReturnPercent` to `features-v5` with `scanner-config-v5`. `SecondaryMomentumWindowSeconds` defaults to 30 and must satisfy `0 < secondary < MomentumWindowSeconds` (default 60). Eligible trades belong to the same symbol and explicit session and fall in the inclusive event-time window ending at `AsOfUtc`. The first and last trades in existing canonical order yield `(last price - first price) / first price * 100` in decimal arithmetic. Fewer than two observations or any non-positive in-window price yields null; flat prices yield zero. Equal timestamps count as separate observations ordered by provider, ordinal sequence and canonical identity. Receive/system time does not determine the value. The original ROC, VWAP-slope algorithm/exact assertions, and all detector/publication/veto logic are unchanged.

The added window is serialized in configuration identity and exposed through the existing effective-configuration API. The new value is included in deterministic feature identity. SQL persistence continues to reject changed configuration values under the same version; the extended SQL test checks rejection for this specific window and compares the retained original configuration/snapshot. No old decision is rewritten and no schema migration is needed.

**Successful primary validation, in execution order (all exit 0):**

| Order | Exact command | New result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **30 passed, 0 failed, 0 skipped**. |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **94 passed, 0 failed, 0 skipped**. |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**. |
| SQL 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**, same database without reset. |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped**. |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **94 passed, 0 failed, 0 skipped**. |
| Final build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded; 0 warnings, 0 errors**. |

The 22 added replay cases cover exact rising **+25%**, falling **-25%**, flat **0%**, inclusive lower/upper boundaries, quote exclusion, non-positive first/intermediate/last prices, fewer than two in-window observations, same-time canonical ordering, changed receive times, reordered input, symbol/session isolation, configuration validation and deterministic identity, and descriptive-only detector behavior. Existing golden replay, duplicate, future-event exclusion and exact VWAP-slope tests remain intact. Changing only the new window also verifies unchanged primary ROC/slope and detector outcomes.

Hosted tests retain all four valid detail/snapshot/list comparisons, all four exact missing-resource `404`s, expected classifications/publication reasons, all four opportunity/snapshot SQL payloads, and all 26 canonical event identities and fields. The hosted effective-configuration assertion is additional, not a replacement.

**Safety and cleanup:** `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. This request authorized fallback to the existing healthy Compose SQL Server; it was not started/stopped/replaced. The process-only connection targeted `MarketOpportunityEngine_IntegrationTests`, with a non-empty case-insensitive `test` catalog guard using `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` before SQL tests. Credentials were captured in memory from that container, never printed/saved. Final guard ran in a child PowerShell; tests ran under their normal .NET runtime. Fixture rows were retained, no configured database was dropped, and migration tests used only uniquely named temporary databases. Environment overrides and temporary credential references were cleared; the temporary validation helper was removed. No provider User Secrets, Alpaca/Massive connectivity, or separate web process was used.

**Earlier attempts and diagnostics in this slice (not successful SQL/build evidence):**

- A PowerShell SQL-open preflight failed `TdsParser` initialization because native `Microsoft.Data.SqlClient.SNI.dll` could not load. It reached no tests and made no database changes.
- A subsequent in-process validation wrapper returned **-2147450751** without test output for the first hosted command and attempted unit/build commands; hosted repetition and SQL persistence/migrations did not run in that attempt. A separate `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --no-restore --list-tests` then exited 0, but discovery is not a passing test run.
- A diagnostic `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --no-restore` passed **94/94**, exit 0. That diagnostic attempt did not reach SQL/build before its execution limit. It is separate from the complete ordered run above.
- The final sequence isolated the builder guard in a child PowerShell and invoked the documented commands directly; every command passed. The wrapper failure's precise cause was not established; it was not evidence that SQL Server was unavailable.

**Unavailable/not-run boundaries:** No SQL verification blocker remains. No new browser/live-provider/Azure evidence is claimed; earlier browser and VWAP-slope results below are historical. Full Milestone A remains incomplete.

### Earlier 2026-10-25 session VWAP-slope feature evidence (historical)

Added event-time-derived session VWAP slope to the existing feature snapshot. For the existing `MomentumWindowSeconds`, the calculator groups eligible trades by distinct event timestamp, tracks cumulative session VWAP through each timestamp, and computes `(last cumulative VWAP - first cumulative VWAP) / elapsed minutes`. Actual event-time spacing handles irregular intervals. Fewer than two distinct in-window timestamps with positive cumulative volume yields null slope and `Insufficient`; exactly zero is `Flat`, positive is `Rising`, negative is `Falling`. No new threshold or config parameter was introduced. The feature is descriptive only and does not change setup qualification, score, publication, or vetoes.

The snapshot and scanner configuration versions are now `features-v4` and `scanner-config-v4`. Existing persisted decision rows are immutable; this calculation creates new deterministic feature/opportunity identities. No schema migration, dependency, or provider change was needed.

| Order | Exact command | New final result |
|---|---|---|
| Focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | Exit **0**; **8 passed, 0 failed, 0 skipped**. |
| 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**. |
| 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**, same dedicated database, no reset/deletion. |
| 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | Exit **0**; **3 passed, 0 failed, 0 skipped**. |
| 4 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | Exit **0**; **72 passed, 0 failed, 0 skipped**. |
| 5 | `dotnet build MarketOpportunityEngine.sln` | Exit **0**; **0 warnings, 0 errors**. |

Focused replay coverage includes rising and falling slopes with exact expected values of **+1.5** and **-1.5 price units per minute** across irregular event intervals, flat zero, insufficient input, symbol/session isolation, deterministic identity, and future-event exclusion. The full fixture regression retained all prior exact positive API/SQL checks and unknown opportunity/symbol/session `404` assertions. SQL tests used the existing Compose SQL Server and guarded `MarketOpportunityEngine_IntegrationTests` database. Fixture records were retained, migration tests used only their uniquely named temporary databases, the container was not changed, and process-only credentials/configuration were cleared. No provider User Secrets, live provider connections, or browser walk-through were used.

**Earlier attempts in this slice (not passing evidence):**

- The first focused build found one test-only `FeatureSnapshot` construction missing the new slope fields; the publication-gate test fixture was updated with the explicit insufficient/null state.
- The next focused run passed 7 of 8 replay tests; the existing configuration identity test selected `scanner-config-v4`, which had become the new default and therefore was not a changed configuration. Its test-only changed value was updated to `scanner-config-v5`; no production default was changed to accommodate the test.
- The first SQL setup attempt loaded the neutral SQL client instead of the Windows runtime assembly and failed before any database test. Loading the packaged Windows runtime `Microsoft.Data.SqlClient.dll` resolved setup. No database was reset/dropped, and no credential was printed or saved.

No outstanding verification blocker remains for this feature slice. The hosted fixture test still uses the isolated Testing/Fixture setup and no Development User Secrets.

### Earlier 2026-10-25 hosted API negative-lookup regression evidence

Added hosted API `404` assertions in [FixtureWebApplicationTests](tests/MarketOpportunity.IntegrationTests/FixtureWebApplicationTests.cs) and directly related documentation; the existing fixture-to-SQL-to-API assertions and persistence-test schema migration fix remain unchanged. The final sequence used the already-running Compose SQL Server with a process-only, guarded connection to `MarketOpportunityEngine_IntegrationTests`. The test connection variable was initially absent; local SQL credentials were obtained in memory without printing or saving them. No test records were deleted to make replay pass.

| Order | Exact command | New final result |
|---|---|---|
| 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**. |
| 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**, same database and retained fixture records. |
| 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | Exit **0**; **3 passed, 0 failed, 0 skipped**. Migration tests drop only their uniquely named temporary databases, never the configured database. |
| 4 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | Exit **0**; **69 passed, 0 failed, 0 skipped**. |
| 5 | `dotnet build MarketOpportunityEngine.sln` | Exit **0**; **0 warnings, 0 errors**. |

The hosted test now checks the exact four symbol/session pairs and expected classifications; complete list/detail payload equality; all four session-specific snapshots and analytical versions; exact `404 Not Found` for a valid absent opportunity GUID, an absent symbol, and an unknown explicit session for both MOE and QNT; SQL opportunity IDs, analytical fields, supporting/contradictory evidence, veto absence, invalidation, and provenance; SQL snapshot IDs/columns and deserialized payload equality; and all canonical event identities/IDs, provider/session/sequence, event/receive times, and trade/quote fields. Because valid session snapshots are compared exactly before the negative assertions, the regression also confirms an unknown session does not fall back to another session's latest result. The fixture has **26** events and **26** unique canonical identities, grouped **7/6/6/7** for MOE-22/MOE-23/QNT-22/QNT-23. Expected events retain the first occurrence of each canonical identity, matching existing duplicate semantics. Queries select exact expected identities/versions, so unrelated history is neither counted as evidence nor rejected.

Publication is checked at deterministic replay time **2026-10-23T14:30:42Z**. Both October 22 opportunities are **Expired / OpportunityExpired**; both October 23 opportunities are **Active / PublicationAllowed**. This is the current in-memory publication projection, not persisted lifecycle history. The HTTP API still reads `ScannerState`.

**Earlier attempts in this slice (not passing evidence):**

- SQL setup wrappers initially failed while loading the SQL client or constructing the PowerShell wrapper; no tests ran in those attempts. Loading the Windows runtime SQL client directly resolved setup. One diagnostic retry lacked the process environment variable and failed the existing guard.
- The first preflight after adding the negative assertions attempted a direct PowerShell SQL connection and failed because native `Microsoft.Data.SqlClient.SNI.dll` could not be loaded. No test command ran and no database was mutated. The successful ordered run skipped that PowerShell connection smoke check and let the .NET integration-test runtime load its packaged SQL dependency.
- The old factory's late `ConfigureAppConfiguration` override did not reliably override settings already captured by `Program`. Initial hosted attempts unexpectedly inherited local Massive settings, attempted a connection, received a plan/access denial, and failed with a disposed-host exception. These were not successful live-provider validation. Logs showed no migrations applied and no completed fixture analysis; the old host's SQL target was not independently established. No provider credential values were displayed or saved.
- Fixed this within the test factory: use the non-Development `Testing` environment to avoid loading provider User Secrets, supply early host configuration, and explicitly register the fixture source, guarded SQL connection, and checked-in scanner configuration. Schema preparation now runs explicitly against the guarded test connection. No application/provider adapter code changed.
- A subsequent fixture-only run failed the exact opportunity-ID check because constructing default configuration in test code serialized decimal thresholds differently from the checked-in configuration (`25` versus `25.0`). The expected replay now reads the checked-in scanner section, preserving its existing representation and immutable configuration version rather than rewriting records or weakening the ID assertions.

**Cleanup and evidence boundary:** All final ordered commands passed, test hosts were disposed, the pre-existing SQL container remained healthy, and the process connection was cleared. The configured database and fixture rows were retained. No separate app/browser was started for this test-only slice; the walkthrough below remains historical. Azure and intentional Alpaca/Massive connectivity checks were not run. No outstanding verification blocker remains for the regression slice.

### Earlier 2026-10-25 completed Compose-backed fixture/browser verification (historical)

After operator approval, reused the already-running healthy Compose `sqlserver` container with the dedicated `MarketOpportunityEngine_IntegrationTests` database. Constructed a process-only connection from local container credentials without displaying or persisting it; the non-empty catalog/`test` guards remain unchanged. The development database was not targeted.

The first SQL persistence run failed with missing provenance columns (`DataCoverage`, `DataFeed`, `DataProvider`, and channel flags): its setup used only `EnsureCreated`, which does not upgrade existing schemas. Changed `SqlServerPersistenceTests` to call `MigrateAsync` when `EnsureCreatedAsync` returns false, matching application startup. No application behavior or migration guard was weakened.

| Command / check | Final result after fix |
|---|---|
| `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**. Ran first, then again after the setup fix. |
| Filtered persistence/migration command in [README](README.md#fixture-runtime-verification-order) | Exit **0**; **3 passed, 0 failed, 0 skipped** (one persistence test and two migration tests). Initial pre-fix run: 2 passed, 1 failed. |
| `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | Exit **0**; **69 passed, 0 failed, 0 skipped** after the code change. |
| `dotnet build MarketOpportunityEngine.sln` | Exit **0**; **0 warnings, 0 errors** after the code change. |
| `dotnet run --project src\MarketOpportunity.Web --launch-profile http --no-build` | Started with process-only Fixture/provider and guarded SQL overrides. `/health` returned Healthy; status reported Recorded / RecordedFixture / 26 events. |
| Integrated browser at `http://localhost:5228/` | All four cards, publication state/reason, and RecordedFixture / Synthetic / Synthetic provenance observed. Reload established a Blazor `/_blazor` WebSocket. |
| Browser HTTP checks | Four opportunity details and four session-specific snapshots returned **200**, with matching IDs/symbol/session. |
| Guarded, read-only SQLCMD queries in the test database | Exact opportunity ID set (**4**) and snapshot ID set (**4**) matched the running API. Fixture events: MOE 2026-10-22 **7**, MOE 2026-10-23 **6**, QNT 2026-10-22 **6**, QNT 2026-10-23 **7**; total **26**. All four opportunity rows: RecordedFixture / Synthetic / Synthetic, realtime false, trades/quotes true. |
| Cleanup | Temporary attached web app stopped; port 5228 no longer listening. Existing SQL container remained running/healthy and its volume was not removed or recreated; fixture records retained in the test database. Connection overrides were process-only. |
| Live provider / Azure checks | **Not run**; no Alpaca/Massive connection, provider credential use, or Azure deployment. |

Observed publication states were Expired / OpportunityExpired for both 2026-10-22 cards, and Active / PublicationAllowed for both 2026-10-23 cards. Fixture publication evaluation uses replay time (`2026-10-23T14:30:42Z`), not a claim of current market validity. No strategy profitability claim is implied.

The initial missing-variable blocker below is historical and resolved for this run. A future terminal/session still needs a safely constructed dedicated test connection; no permanent credential configuration was created. The API remains memory-backed: matching persisted rows does **not** prove historical reads or live recovery.

### Earlier 2026-10-25 preflight (superseded by the completed run above)

Inspected `FixtureWebApplicationTests`, `SqlServerPersistenceTests`, `ExplicitSessionMigrationTests`, and the integration project dependencies. At that checkpoint the hosted test attempted to force Fixture mode using late configuration, waited for four opportunity results, checked status/list/detail/snapshot HTTP routes, and checked persisted opportunity IDs/provenance plus broad snapshot/event counts. The new regression slice above replaces those counts and hardens provider isolation. The persistence test covers session isolation, immutable records, replay idempotency, and provenance. Both migration tests use isolated temporary databases. All three test classes retain the non-empty database-name/`test` guard.

| Check / command | Exact result |
|---|---|
| Process-environment preflight for `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` | **Absent**. No connection value, credentials, or User Secrets were displayed. No SQL connection was attempted. |
| `dotnet --version` | **10.0.402**. |
| `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **Not run**: missing dedicated SQL test connection. |
| Filtered SQL persistence/migration command in [README](README.md#fixture-runtime-verification-order) | **Not run**: same safety gate. |
| Manual Fixture-mode dashboard / HTTP walkthrough | **Not run**: no safely configured local test database was established. No app or Docker process was started and no database was altered. |
| `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **Succeeded**, exit code **0**; **69 passed, 0 failed, 0 skipped**; reported test duration **2 seconds**. |
| `dotnet build MarketOpportunityEngine.sln` | **Succeeded**, exit code **0**; **0 warnings, 0 errors**; elapsed **00:00:02.60**. Includes the hosted integration-test project, but does not execute it. |
| Alpaca/Massive connectivity | **Not run**. No live-market connection or provider credentials were used. |

At that earlier preflight, only documentation changed and runtime checks remained unverified. The later approved Compose-backed run above resolved that blocker. Migration coverage requires permission to create/drop its uniquely named temporary databases.

### Prior 2026-10-24 checkpoint evidence

These results are historical, not additional commands run during the 2026-10-25 attempt:

| Command | Result |
|---|---|
| `dotnet restore MarketOpportunityEngine.sln` | **Succeeded**, exit code 0. |
| `dotnet build MarketOpportunityEngine.sln --no-restore` | **Succeeded**, exit code 0; **0 warnings, 0 errors**. |
| `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --no-build` | **Succeeded**, **69 passed, 0 failed, 0 skipped**. |
| `dotnet build tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj` | **Succeeded**, exit code 0; **0 warnings, 0 errors**. Hosted fixture test compiled but was not executed. |
| `dotnet test tests\MarketOpportunity.AlpacaConnectivityTests\MarketOpportunity.AlpacaConnectivityTests.csproj` | **Succeeded**, **0 passed, 1 skipped, 0 failed**; skipped because credentials are absent. No network request was attempted. |
| SQL Server integration test execution, including `FixtureWebApplicationTests` | **Not run**. `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent; no database was mutated. |

At that prior checkpoint SQL integration coverage was **unverified**, not passed; the completed 2026-10-25 run above supersedes it. The connectivity project initially lacked the environment-variable configuration package; that dependency was added and restore/test then succeeded with the expected credential-gated skip.

### Existing test categories

- **Unit:** Yes — Alpaca/Massive parser/options/capability/factory, analytics, detector, publication lifecycle, and application pipeline tests.
- **Integration:** Yes, real SQL Server — hosted fixture/API/persistence test, persistence/idempotence test, and two migration tests all passed on 2026-10-25.
- **Architecture:** No dedicated architecture test project.
- **Component:** No component test project.
- **Replay/golden:** Replay-style deterministic tests in `ScannerReplayTests`; fixtures are constructed in test code rather than an external golden snapshot suite.
- **UI:** No committed browser/UI test project. Local integrated-browser walkthrough completed on 2026-10-25.

### Specific coverage evidence

| Behavior | Coverage |
|---|---|
| VWAP | Yes: replay and detector tests assert calculated session VWAP. |
| Spread | Yes: invalid quote and excessive spread veto tests. |
| Event ordering | Yes: late/out-of-order input sorted deterministically. |
| Duplicates | Yes: repeated identity does not change replay result. |
| Future-event leakage | Yes: future event cannot affect earlier snapshot/opportunity. |
| Setup qualification | Yes: probable long/short and watch cases. |
| Setup rejection | Yes: veto and insufficient-data cases. |
| Stale-data handling | Stale-quote veto and stale/offline live-feed publication suspension are tested separately. |
| Excessive-spread veto | Yes. |
| Insufficient-data behavior | Yes. |
| Opportunity expiration | Valid-until timestamp and publication-gate expiry tested; persistent lifecycle history is not implemented. |
| Configuration/version propagation | Snapshot/opportunity identity and version fields covered offline; SQL persistence/version behavior passed integration coverage. |
| Alpaca mapping | Parser, timestamp/identity handling, feed capabilities, options, and source selection are offline unit-tested; socket connectivity is not verified. |
| Publication/API read model | JSON serialization unit-tested; hosted HTTP/SQL test passed and local browser/API results matched persisted IDs/provenance. |

## 21. End-to-End Proof

Offline unit coverage plus the 2026-10-25 local fixture SQL/API/browser verification establishes the following evidence:

```text
Synthetic canonical events
  ✓  test event sources and replay inputs
Normalization/parser
  ✓  Alpaca/Massive parser unit tests; fixture adapter maps JSON to canonical events
Event-time state
  ✓  sorted ordering, duplicates, symbol/session isolation, bounded eviction
Features
  ✓  VWAP, current price, short return, window volume, quote age, as-of cutoff
Momentum detector
  ✓  long/short/watch qualification, evidence, scoring, versioning
Risk/data-quality gates
  ✓  quote-age, invalid data, spread, insufficient data, incomplete state
Opportunity result
  ✓  deterministic identity and expiration timestamp
Publication decision
  ✓  active/suspended/expired/not-qualified state and reason; feed-health reevaluation
Provider capabilities/provenance
  ✓  feed mapping, source selection and offline provenance tests
Test store boundary
  ✓  ScannerPipeline tests use a recording store
SQL persistence
  ✓  hosted fixture, persistence, and both migration tests passed; 26 fixture events in SQL
HTTP API
  ✓  hosted test passed; local four opportunity/snapshot IDs matched SQL
Blazor UI
  ✓  local browser walkthrough: four cards, publication/provenance, Blazor WebSocket
Alpaca/Massive provider sockets
  ?  adapters compile; parser/options tested; no authenticated stream exercised
```

`✓` denotes the scoped fixture evidence recorded in section 20, not live-provider verification, database-backed API reads, a permanent browser regression suite, or full Milestone A completion.

## 22. Master Spec Compliance

### Implemented as Specified

- .NET 10/C#, ASP.NET Core Blazor, EF Core SQL Server, and modular-monolith project boundaries.
- Provider-independent market-event contracts with decimal/integer trading values and event/receive timestamps.
- Explicit symbol/session event state, deterministic event-time ordering, dedupe, bounded retained state, and future-event cutoff.
- Deterministic feature/setup versions, opportunity explanations, evidence, contradictions, veto list, score, and expiry timestamp.
- Independent publication gate suspends live candidates for Connecting, Stale, Offline, or Error source health without changing analytical evidence.
- Provider/feed coverage and channel provenance flows into snapshots/opportunities; Alpaca is optional and Fixture remains the configured default.
- Fixture default, SQL Server local environment, canonical event retention, and accepted ADRs.
- No machine learning, broker access, order placement, Redis, distributed broker, microservices, Kubernetes, Level 2, or LLM-generated signals found.

### Implemented Differently

- The repo includes optional Alpaca and Massive live adapters, while fixture remains the configured default. Both live adapters remain non-resilient.
- SignalR is present only as Blazor Interactive Server circuit transport; there is no separate SignalR application hub.
- Trade and quote persistence share one `market_event` table instead of separate logical tables.
- The application/API read model is in-memory. Persistence is write-oriented at this point; current API results are not database-backed.
- Session ID is part of the current event contract and is required rather than inferred.
- Configuration and feature-set versions exist, but no complete operational configuration-history/audit interface exists.

### Missing but Required for Current Checkpoint

- Remaining three Milestone A detectors.
- Feed reconnect/recovery and gap handling; unhealthy feed publication suspension is implemented but does not provide data-integrity recovery.
- Liquidity, benchmark/context, volatility, reward/risk, targets, and later-observation structural invalidation.
- Additional requested feature engines (trend strength, multi-horizon momentum, relative volume/strength, opening range); the current price-structure output is descriptive only and is not integrated into trend scoring or detectors.
- Historical/persisted API reads. The fixture's parallel SQL persistence/current-state API/Blazor path is verified, not database-backed reads.
- Live dashboard completeness and runtime proof, including error/recovery health.
- Azure SQL deployment configuration and reviewed production schema deployment path are absent despite being required by the technology baseline.
- SQL integration verification passed against a dedicated local test database; production/Azure database verification remains absent.

### Intentionally Deferred

- Machine learning, backtesting/outcomes/research, broker or automated trading, LLM explanations, and future model tables.
- Raw provider payload retention pending license/replay review.
- Separate application SignalR hub pending a need beyond the current Blazor Server circuit.
- Redis, distributed messaging, microservices, Kubernetes, and Level 2 processing are explicitly outside Milestone A and absent.

### Accidental Scope Expansion

No evidence of accidental scope expansion into ML, trading, distributed infrastructure, or other prohibited future platform work. Alpaca and Massive are optional data adapters; Alpaca is explicitly approved in ADR-005 and does not authorize broker access.

## 23. Technical Debt / Concerns

### HIGH

1. **No gap/recovery/reconnect path.** Live adapter termination ends the stream; missed events cannot be backfilled and there is no unresolved-gap/recovering state. Publication suspension limits stale-result display but does not restore data integrity.
2. **Reads remain memory-only.** Fixture SQL/API/browser runtime is verified, but the API/dashboard do not load historical data after restart.
3. **Core scanner capability is incomplete.** One detector and a subset of data-quality checks do not meet Milestone A detector/risk requirements.

### MEDIUM

1. **Publication lifecycle is process-local.** Current `PublicationDecision` reflects health and expiration in memory; append-only lifecycle history is not persisted, and later price action does not monitor structural invalidation.
2. **State-boundedness does not bound every collection.** Fixture pipeline retains each state's event list separately for persistence, and symbol/session maps have no global cardinality cap. Current fixture/provider configuration keeps expected inputs limited but the overall memory bound is not universal.
3. **Current data API is a process-local read model.** Endpoints return only current in-memory results, not stored opportunities or snapshots.
4. **Development SQL credential default is checked into Compose configuration.** Documentation scopes it to local development and recommends overriding it; it must not be reused outside disposable local use.
5. **Feature completeness and participation scoring are narrow.** A positive window volume awards the full activity weight; no relative-volume or minimum-liquidity context is used.
6. **Browser regression coverage is manual.** Hosted HTTP/SQL tests and a local browser walkthrough passed; there is no committed component/browser test suite or live error/recovery browser coverage.

### LOW

1. **Operational tuning constants are embedded in code.** Live batch cadence, bounded channel capacity, and provider message-size limit do not appear in validated scanner configuration.
2. **No scanner-specific telemetry.** Current OTel instrumentation cannot explain event-to-opportunity latency, veto counts, ingestion gaps, persistence latency, or SignalR client health.

## 24. Current Blockers

- **Verification blocker:** None for this slice. The absent variable was resolved using this request's authorized fallback to the existing healthy Compose SQL Server and a process-only connection to the dedicated integration-test database. Earlier preflight/wrapper failures are separated in section 20.
- **Local run dependency:** Web startup requires SQL Server configuration/service, even when using fixture mode.
- **Milestone A implementation blockers:** Only Momentum Continuation exists; required detector and hard-gate coverage is missing; live reconnect/gap recovery is absent; persisted-result APIs are not implemented. The current-state fixture SQL/API regression is verified; browser walkthrough evidence is historical.
- **External live-data dependency:** Alpaca or Massive credentials and feed entitlement are needed for intentional live socket validation. Initial pre-isolation hosted attempts inherited local Massive configuration and were denied; final tests use the isolated Fixture host without loading provider User Secrets. No provider credential values were printed or saved.

Current trend-direction verification is recorded in section 20, including focused replay, full offline, hosted fixture, persistence/migration, and build results. No browser walkthrough, live connectivity, User Secrets, or Azure verification is claimed for this analytics-only slice.

## 25. Recommended Next Vertical Slice

**Goal (recommendation only; not implemented):** Define and add one descriptive retained-session volatility estimate using existing scalar trade observations, without synthesizing OHLC bars or introducing a new event contract.

**Why now:** The master specification identifies ATR or an equivalent versioned volatility estimate and short-horizon realized volatility as volatility/risk inputs. Existing events have scalar trade prices, not OHLC bars, so define an explicitly trade-point measure and its insufficient/invalid/partial semantics before using volatility in any gate.

**Acceptance criteria:** Specify the exact event-time formula, units, invalid/insufficient states and completeness behavior; derive only from the same-symbol/explicit-session retained snapshot at `AsOfUtc`; version output identity; add exact offline replay/JSON/identity/partial-state tests; preserve all current features and detector outcomes; do not use the result to score, classify, veto, or publish.

**Validation:** Focused offline replay tests, full units, guarded hosted fixture twice on one test database, persistence/migrations, and solution build. No live provider or browser walkthrough is needed for an analytics-only slice.

**Explicitly exclude:** Other setup detectors, live-provider connections, broker integration, provider selection changes, Azure deployment, ML, and distributed transport.

## 26. Questions Requiring Architect Decision

No immediate product/architecture decision is required for the verification slice. Broader questions about Azure deployment and production provider terms remain subject to the master specification/ADR process.

## 27. Handoff Summary for Copilot

# Handoff Summary for Copilot

**Current Milestone:** Milestone A — Usable Scanner; no formal checkpoint ID recorded.  
**Working Vertical Slice:** Versioned descriptive retained-session trend direction using existing EMA spread/slopes, VWAP slope/direction, and price structure; ADX remains nondirectional strength. Preserves prior features and detector behavior; repeated hosted fixture → SQL → current-state API regression with negative lookup/session-isolation checks. Prior Blazor walkthrough is historical.
**Implemented:** .NET 10 modular monolith; fixture default; optional Alpaca and Massive trade/quote WebSocket adapters; capability/provenance metadata; event-time state and duplicate identity; VWAP, short-horizon return, window volume, spread, quote freshness; one directional detector; independent health-to-publication gate; SQL Server/EF persistence code and provenance migration; GET status/opportunity/snapshot/config endpoints; Interactive Server dashboard; five accepted ADRs.  
**Not Implemented:** Three other Milestone A detectors; composite trend-direction/quality scoring; relative-volume/strength, ATR/other volatility, opening-range, liquidity, benchmark/sector context, reward/risk; data-gap recovery/reconnect; persisted publication revisions; later-event invalidation monitor; database-backed read API; App Service/Azure SQL deployment, Key Vault, Azure Monitor/App Insights, Entra ID, Bicep, Azure DevOps.
**Current Data Source:** Fixture is the configured default; Alpaca test feed defaults to `FAKEPACA`, while live feeds require operator credentials, symbols, explicit session ID, and entitlement. Massive remains supported. No historical adapter.  
**Current Features:** Current trade reference price, bid/ask/spread, retained-session VWAP, session VWAP slope per minute with direction/insufficient state, price vs VWAP, fast/medium EMA (5/20 trade periods by default), signed fast/medium spread and event-time slopes, trade-point Wilder ADX (`TrendStrengthPeriod` 14 by default; null/state for warm-up or invalid prices), descriptive `SessionTrendDirection` (bullish/bearish/neutral/conflicting/insufficient/invalid), two separately configured short-horizon returns (secondary descriptive/nullable), unchanged primary-window volume, descriptive nullable session cumulative volume, session high/low and range position, fixed two-left/two-right session price structure (nullable; higher-high/higher-low, lower-high/lower-low, mixed, or flat; partial after eviction per `HasCompleteSessionData`), quote age/validity, required-data/completeness quality. Versions: `features-v12`, `scanner-config-v12`, unchanged `momentum-continuation-v1`.
**Current Detector(s):** Momentum Continuation only; Long, Short, Watch, Avoid/No Trade.  
**Current Vetoes:** Stale quote, invalid quote/price, excessive/unavailable spread, insufficient required data/incomplete state, no defensible invalidation. Separately, unhealthy live source suspends publication with a lifecycle reason.  
**Persistence:** EF Core 10 SQL Server store and five tables; session-identity/provenance migrations. Hosted fixture and persistence/migration tests passed. SQL matched four API opportunity IDs, four snapshot IDs, and 26 fixture events. API reads process memory.  
**Blazor:** Local integrated-browser walkthrough verified all four fixture cards, publication/provenance, and a Blazor WebSocket. No committed browser regression suite or live recovery walkthrough.  
**Realtime UI:** In-process `ScannerState.Changed` updates Blazor Server circuits; no application SignalR hub.  
**Tests:** Latest trend-direction slice: focused replay **123/123**; offline units **187/187 before and after SQL**; hosted fixture **1/1 twice on the same database**; persistence/migrations **3/3**; solution build succeeded. Hosted fixture asserts exact HTTP string and raw SQL numeric enum values alongside prior provenance/identity/negative-lookup/configuration-immutability regressions. Historical browser walkthrough not rerun. Fixture hosts use Testing configuration and Fixture only. Failed focused-test attempt is separated from final passing evidence in section 20.
**Azure Readiness:** No deployment infrastructure/integration. Only local SQL Server Docker setup and configuration abstractions exist.  
**Primary Technical Debt:** No reconnect/gap recovery; publication state is not persisted as revisions; no later-event invalidation monitor; historical reads absent. Exact fixture SQL/API positive and negative lookup regression assertions are implemented.  
**Current Blocker:** None for the completed verification slice. Future sessions need a guarded process-only test connection; full Milestone A still requires missing functionality.  
**Recommended Next Step:** Define a descriptive trade-point realized-volatility measure from existing retained scalar prices, without synthesizing OHLC bars or changing detector, scoring, or veto behavior.
**Decisions Needed:** No decision blocks the next-slice recommendation. Do not connect live providers implicitly.
