# Market Opportunity Engine

A .NET 10 modular-monolith decision-support scanner for US equities/ETFs. The application defaults to recorded fixtures and supports optional read-only Alpaca and Massive market-data adapters. It is not a broker and does not place orders.

## Current milestone

**Milestone A.2.3b - Tracked Opportunity + Pre-Trade Readiness: IMPLEMENTED; PR #2 review corrections require guarded SQL revalidation.** The original offline + guarded SQL closeout is recorded; the review corrections are separate validation work. A.1 live scanner validation, A.2.1 market structure, A.2.2 immutable candidate TradePlans, and A.2.3a prior-session context are complete. See [PROJECT_STATUS.md](./PROJECT_STATUS.md) for the concise checkpoint and [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md) for historical evidence.

## Architecture overview

```text
Recorded fixture (default) or optional read-only adapter
  -> canonical market events
  -> bounded in-process channel for live ingestion
  -> event-time state and versioned features
  -> Momentum Continuation setup
  -> immutable opportunity and candidate TradePlan
  -> current publication-health projection
  -> tracked candidate and immutable pre-trade readiness assessments
  -> ASP.NET Core API and Blazor Interactive Server dashboard
```

The solution targets .NET 10, ASP.NET Core 10, Blazor Interactive Server, EF Core 10, and SQL Server locally / Azure SQL Database as the deployment target. Domain and analytics remain independent of provider DTOs and persistence. Blazor's circuit transport updates the UI; there is no separate application SignalR hub. See [the master specification](./Market_Opportunity_Engine_Final_Master_Spec.md) and [docs/adr/](./docs/adr/).

## A.2.3a prior-session context

The separate `prior-session-context-v1` contract reports prior-session trade-point pivots, structural references, completeness, and feed/source provenance at `/api/v1/symbols/{symbol}/prior-context`. The dashboard labels partial-session extrema as observed values and reports session high/low/close only when full, gap-free coverage is established. Close means the last eligible trade by event time at or before session end, not an asserted official exchange close.

Only the recorded fixture currently supplies historical data and an explicit fixture calendar; that calendar maps session identities to U.S. equity regular-session boundaries in `America/New_York` semantics. It does not infer dates or holidays. Synthetic fixture coverage is partial. Live Alpaca/Massive history and an authoritative live calendar remain unavailable/unverified, so the scanner keeps running and shows prior context as unavailable. Context is deterministically rebuilt from approved fixture input, not stored in SQL; persisted live events are not currently exposed as an approved historical replay source. See [ADR-007](./docs/adr/ADR-007-prior-session-context.md).

## A.2.3b tracked opportunity and pre-trade readiness

The scanner pins one immutable `PlanDefined` TradePlan per symbol/session as a tracked candidate (`tracked-opportunity-selection-v1`) and evaluates `pre-trade-readiness-v2` against that pinned geometry: **Unavailable**, **Suspended**, **Watching**, **Setup Forming**, **Entry Area**, **Entry Ready**, or **No Longer Actionable**, each with structured reasons and evidence. Newer recalculated plans never move a tracked candidate's entry geometry; after a terminal condition (expiry, invalidation crossed, target reached before entry, extended beyond entry, session change, or plan unavailability) a subsequent evaluation may pin the newest eligible candidate. Feed/data-health problems suspend active candidates without terminating or mutating the tracked plan. Terminal assessments and all terminal lifecycle fields remain immutable.

Readiness states describe market conditions around a candidate opportunity; they are never execution instructions and the application does not know or need the user's holdings. Entry Ready means "candidate entry conditions are aligned," not "buy/sell now." v2 uses current-session inputs only and always reports `CurrentSessionOnly`, even if complete prior context is available separately. `PriorSessionEnhanced` remains readable for historical records but requires actual material use of historical levels, not availability alone. Quote freshness is checked at the current evaluation time without changing the analytical snapshot. Live startup and health ticks persist idle expiry/session-transition lifecycle changes without waiting for another eligible analysis; expiry is timestamped at the pinned validity boundary. Session transitions use strictly later event-time observations, not guessed exchange-close times. The approach boundary `Scanner:PreTrade:ApproachDistanceRiskMultiple` is normalized by the plan's RiskPerShare; its development default (1.0) is PROVISIONAL, not market calibrated, and changing it requires a new scanner-configuration version. See [ADR-008](./docs/adr/ADR-008-tracked-opportunity-readiness.md).

## Quick start

Requirements: .NET 10 SDK and Docker Desktop.

From the repository root in PowerShell:

```powershell
docker compose up -d sqlserver
dotnet run --project src\MarketOpportunity.Web --launch-profile http
```

The `http` launch profile starts the development app at `http://localhost:5228`. Open that URL; the fixture scanner starts automatically. Check the API:

```powershell
Invoke-RestMethod http://localhost:5228/health
Invoke-RestMethod http://localhost:5228/api/v1/system/status
Invoke-RestMethod http://localhost:5228/api/v1/opportunities
```

The local SQL Server uses a persistent Compose volume. `docker compose down` preserves data; **do not use `docker compose down --volumes` / `down -v` unless you intend to delete that volume**. A volume is not a backup.

## Configuration

Keep the shared [`appsettings.json`](./src/MarketOpportunity.Web/appsettings.json) provider at `Fixture`. Provider selection is `MarketData:Provider` (`Fixture`, `Alpaca`, or `Massive`); credentials do not select a provider. Configuration precedence is command line, environment variables, Development User Secrets, environment-specific JSON, then shared JSON. Restart the app after changing settings.

Readiness thresholds live under the versioned scanner configuration: `Scanner:PreTrade:ApproachDistanceRiskMultiple` (must be positive) sets how close price must be to the pinned entry area, in multiples of the plan's risk per share, before Setup Forming applies. The shipped default 1.0 is a provisional development value; changing it requires a new `Scanner:Version`, which participates in readiness assessment identity.

Select an optional provider locally through Development User Secrets. For example:

```powershell
dotnet user-secrets set "MarketData:Provider" "Alpaca" --project src\MarketOpportunity.Web
dotnet user-secrets set "Scanner:Universe:Symbols" "AAPL,MSFT" --project src\MarketOpportunity.Web
```

Store provider credentials and the explicit provider session ID in User Secrets as appropriate; never check them into source, pass them in command arguments, or expose them to browser code. User Secrets are not encrypted and are for local development only. Deployed secrets belong in Azure Key Vault. See the [provider configuration and criteria](./docs/adr/ADR-005-market-data-provider-criteria.md).

## Scanner universe

Live adapters use the single comma-separated `Scanner:Universe:Symbols` setting. Entries are trimmed, uppercased, rejected if empty, and deduplicated case-insensitively while preserving first occurrence. Live mode requires a non-empty list. The fixture does not use this setting.

There is no fallback to legacy `MarketData:Alpaca:Symbols` or `MarketData:Massive:Symbols` keys. Choose and explicitly migrate the intended list before a live run; do not merge legacy lists automatically. The configured provider session ID is explicit; the application does not infer a market session from a calendar date.

## Market-data providers

- **Fixture:** offline default; uses recorded synthetic trades/quotes and does not require the live universe.
- **Alpaca:** optional market-data adapter; current provider for development/live-stream evaluation. Configure `MarketData:Alpaca:ApiKey`, `MarketData:Alpaca:SecretKey`, `MarketData:Alpaca:Feed`, and `MarketData:Alpaca:SessionId` in User Secrets. Feed entitlements and coverage are provider-defined.
- **Massive:** supported live trades/quotes adapter. Configure `MarketData:Massive:ApiKey` and `MarketData:Massive:SessionId` in User Secrets. The adapter does not currently reconnect.

Both adapters are market-data-only. Neither selects broker permissions, accesses a brokerage account, or submits orders. Alpaca reconnects after disconnect, but historical gap backfill is not implemented; unresolved gaps suspend publication. Both live adapters start without historical warm-up. Consult [Alpaca protocol and limitations](./docs/providers/alpaca-websocket.md) and [Massive protocol and limitations](./docs/providers/massive-websocket.md). Review [ADR-005](./docs/adr/ADR-005-market-data-provider-criteria.md) before changing provider behavior.

## Local SQL Server

The app requires SQL Server persistence. Development startup creates a clean database schema with EF Core `EnsureCreated`; if the database already exists, it applies the available reviewed migrations. Existing event/session migration behavior labels legacy rows `legacy-unknown`, rather than inferring a session.

Schema management is deliberately mixed: clean development schemas may use `EnsureCreated`; existing schemas use reviewed hand-written, idempotent migrations. The repository has no EF model snapshot or initial migration. Do not generate a model migration blindly; it may describe the entire model. Review the migration-generation strategy before changing this convention. The current TradePlan schema is introduced by an explicit migration, and the tracked-candidate/readiness tables (`tracked_opportunity`, `pre_trade_readiness_assessment`) by `20261101120000_AddTrackedOpportunityReadiness`. See [ADR-002](./docs/adr/ADR-002-ef-sqlserver-conventions.md), [ADR-006](./docs/adr/ADR-006-immutable-trade-plan-persistence.md), and [ADR-008](./docs/adr/ADR-008-tracked-opportunity-readiness.md).

SQL integration tests use `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` and must target a dedicated test database, never the development database or live-validation database. Before any SQL test command, verify the effective connection's catalog with a read-only SQL Server query and confirm it is dedicated to testing (the A.2.2 closeout used `MarketOpportunityEngine_IntegrationTests`). If no safe dedicated test connection already exists, stop; do not start a server, create/retarget a database, or drop/reset data to make the tests run. Follow the guarded procedure and exact database checks in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#milestone-a22---sql-validation-closeout-2026-10-30).

## Offline fixture mode

The fixture at `src\MarketOpportunity.Web\Fixtures\momentum-continuation.json` contains synthetic canonical trades and quotes for `MOE` and `QNT` across two explicit fixture sessions. It replays deterministically through the same analysis pipeline and persists events, feature snapshots, opportunities/evidence, configuration snapshots, and TradePlans.

To restore fixture selection locally:

```powershell
dotnet user-secrets set "MarketData:Provider" "Fixture" --project src\MarketOpportunity.Web
```

Check for higher-priority environment or command-line provider overrides before diagnosing a non-fixture launch. Credentials can remain in User Secrets; fixture mode does not use them.

## Optional live Alpaca validation

Live validation is opt-in and not needed for normal development or A.2.3. Configure Alpaca credentials and the scanner universe in Development User Secrets, and prepare the dedicated `MarketOpportunityEngine_LiveValidationTests` database using the documented safety checks. The validation launcher defaults to a 10-minute IEX run; its synthetic test feed is not exchange evidence.

```powershell
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 -Feed iex -PreflightOnly
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 -Feed iex
```

The launcher uses process-scoped validation configuration and does not migrate User Secrets. Never point it at the development database. For setup, preflight requirements, exact-session persistence checks, browser observations, and safe shutdown, follow the [A.1 validation record and runbook](./VALIDATION_HISTORY.md#consolidated-a1-validation-record-2026-10-29) and [Alpaca reference](./docs/providers/alpaca-websocket.md). Do not repeat provider validation simply because documentation changed.

## Tests

Offline tests and build:

```powershell
dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj
dotnet build MarketOpportunityEngine.sln
```

SQL Server integration tests require a verified dedicated test database. After following the safety guard above:

```powershell
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"
dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests
```

The separate Alpaca connectivity test project makes a network connection when credentials are configured; it is not part of the solution's offline unit suite. Do not run it unless explicitly validating that provider. Latest checkpoint results are summarized in [PROJECT_STATUS.md](./PROJECT_STATUS.md); full historical results are in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md).

## API

- `GET /health`
- `GET /api/v1/system/status`
- `GET /api/v1/opportunities`
- `GET /api/v1/opportunities/{id}`
- `GET /api/v1/symbols/{symbol}/snapshot?sessionId={sessionId}` (`sessionId` is optional)
- `GET /api/v1/symbols/{symbol}/prior-context?sessionId={sessionId}` (`sessionId` is optional)
- `GET /api/v1/configuration/effective`

Opportunity list and detail include the immutable analytical opportunity, current publication decision/reason, candidate TradePlan, and a `readiness` read model for the tracked candidate (tracked identity, pinned `PlanId`, readiness state/reason, context mode, current price, entry zone, distance to entry and its risk multiple, invalidation, Target 1, reward/risk, `AsOfUtc`, assessment version, and prior-context completeness when attached); `readiness` is null when no tracked candidate exists. The snapshot endpoint returns the versioned feature snapshot, including market-structure fields. `GET /api/v1/system/diagnostics` is available only in Development when opt-in validation diagnostics are enabled; it is not a normal status endpoint.

## Dashboard

The dashboard at `/` shows source/feed health and coverage, symbol/session analyses, setup classification and quality, freshness, evidence, contradictions, vetoes, and publication state/reason. Each opportunity has a primary **PRE-TRADE READINESS** section with the plain-language states Unavailable, Suspended, Watching, Setup Forming, Entry Area, Entry Ready ("Candidate entry conditions are aligned."), and No Longer Actionable, a compact explanation (current price, entry area, distance, invalidation, Target 1, reward/risk), and the context line (Prior Session + Today, or Today Only when prior-session history is unavailable or incomplete). Each opportunity also keeps the **TRADE PLAN** section displaying candidate geometry and plan status. Publication health can suspend presentation without rewriting immutable setup, plan, or readiness records.

## Current analytical capabilities

- One implemented setup detector: Momentum Continuation (`momentum-continuation-v1`).
- Event-time-safe, bounded, symbol/session-isolated features and explicit data-quality behavior.
- `features-v16` market structure: `SessionPriceStructure`, `LatestConfirmedSwingHigh/Low`, `PreviousConfirmedSwingHigh/Low`, `MarketStructureStatus`, `StructureState`, swing relationships, `NearestSupportCandidate/NearestResistanceCandidate`, and distances to confirmed highs/lows.
- Versioned configuration, opportunity evidence, contradictions, vetoes, and immutable historical analysis.
- A 0–100 score describes rules-based setup quality; it is not a probability of profit.

Market structure is descriptive. Swings require later eligible trades to confirm; retained-state incompleteness can make structure insufficient. These fields do not change Momentum Continuation classification, scoring, or veto behavior.

## Current TradePlan capability

The immutable `trade-plan-v1` is derived separately from a directional opportunity and confirmed structure. It contains direction/status, entry zone/reference, numeric invalidation, structural Target 1 (Target 2 may be null), decimal risk/share, reward/share, reward/risk, evidence, provenance, and version identities. `PlanDefined` means calculable geometry—not an instruction, execution readiness, or a position state.

Plans persist in the SQL Server `trade_plan` table keyed by deterministic `PlanId`. Identical replay is idempotent; different content under the same ID fails without rewriting history. The opportunity API and dashboard expose the plan. Current publication health is separate and is not stored in historical plan geometry. Reward/risk is descriptive; no minimum reward/risk veto is implemented or authorized by this checkpoint.

## Tracked-opportunity and readiness capability

A tracked candidate pins one immutable `PlanDefined` plan per symbol/session; the association and its controlled lifecycle persist in `tracked_opportunity`, and every analysis-time readiness assessment persists immutably in `pre_trade_readiness_assessment`. Identical replay is idempotent; different content under the same ID fails without rewriting history. Active tracked candidates are restored at startup and reevaluated against current trustworthy market state; prior readiness states are never restored blindly. See [ADR-008](./docs/adr/ADR-008-tracked-opportunity-readiness.md).

## Current limitations

- A.2.3b readiness states describe market conditions only; Entry Ready is not an execution instruction, and no order/position capability exists.
- The original A.2.3b guarded SQL closeout passed; PR #2 review corrections require guarded SQL revalidation.
- The recorded fixture produces no confirmed structure, so it yields no tracked candidates; readiness reports no tracked candidate in fixture mode.
- The `Scanner:PreTrade:ApproachDistanceRiskMultiple` default (1.0) is provisional and not market calibrated; it makes no profitability claim.
- The scanner has no `PositionContext` and cannot infer whether a user entered a trade. `MANAGE`, `TAKE PROFIT`, and `EXIT` states are not implemented.
- Breakout, VWAP reclaim/rejection, and failed-breakout detectors are not implemented; market-situation classifications (Potential Profit Area, breakout/breakdown) are not implemented.
- No historical event-read path, outcome labeling, backtesting, ML, or ONNX inference.
- Raw vendor payloads are not persisted; normalized-event retention has occurred for local evaluation. See [ADR-004](./docs/adr/ADR-004-market-event-retention.md) before sustained research/backtesting/model-training use.
- No exchange-calendar integration or full historical gap recovery; Massive has no reconnect behavior.
- No Azure deployment infrastructure or production migration orchestration.
- No broker integration or order execution.

## Documentation map

- [Master specification and roadmap](./Market_Opportunity_Engine_Final_Master_Spec.md): authoritative product, architecture, and future contracts.
- [PROJECT_STATUS.md](./PROJECT_STATUS.md): current checkpoint, completed slices, blockers, and immediate next step.
- [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md): historical test, live-run, SQL, and browser validation evidence.
- [docs/adr/](./docs/adr/): durable architecture decisions (ADR-001 through ADR-008).
- [docs/providers/](./docs/providers/): provider protocol references and current adapter limitations.
- [scripts/](./scripts/): opt-in live validation tooling.
