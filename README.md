# Market Opportunity Engine

A .NET 10 modular-monolith decision-support scanner for US equities/ETFs. The application defaults to recorded fixtures and supports optional read-only Alpaca and Massive market-data adapters. It is not a broker and does not place orders.

## Current milestone

**Milestone A.2.4b - Structural Level Reaction Classification: COMPLETE. A.2.5 Live Prior-Session Historical Context: COMPLETE (Batches 1-4 COMPLETE).** The production-wired reaction tracker remains unchanged. A.2.5 terminally warms approved live Alpaca symbol/session scopes before their first eligible analysis, reconstructs the immediately preceding calendar-resolved regular session from explicit historical SIP, and honestly falls back to `CurrentSessionOnly` when context is partial or unavailable. The next checkpoint requires design/approval. See [PROJECT_STATUS.md](./PROJECT_STATUS.md), [ADR-011](./docs/adr/ADR-011-live-prior-session-historical-source.md), and the [master specification](./Market_Opportunity_Engine_Final_Master_Spec.md#a25-live-prior-session-historical-context).

## Architecture overview

```text
Recorded fixture (default) or optional read-only adapter
  -> canonical market events
  -> bounded in-process channel for live ingestion
  -> event-time state and versioned features
     -> confirmed MarketStructure + eligible PriorSessionContext
        -> separate descriptive MarketSituation assessment
           -> event-driven MarketReaction episodes + descriptive research context
     -> Momentum Continuation setup
        -> immutable opportunity and candidate TradePlan
           -> current publication-health projection
              -> tracked candidate and immutable pre-trade readiness assessments
  -> ASP.NET Core API and Blazor Interactive Server dashboard
```

The solution targets .NET 10, ASP.NET Core 10, Blazor Interactive Server, EF Core 10, and SQL Server locally / Azure SQL Database as the deployment target. Domain and analytics remain independent of provider DTOs and persistence. Blazor's circuit transport updates the UI; there is no separate application SignalR hub. See [the master specification](./Market_Opportunity_Engine_Final_Master_Spec.md) and [docs/adr/](./docs/adr/).

## A.2.4a market situation

`market-situation-v1` describes the nearest eligible support and resistance interactions independently: approaching, testing, or crossed. It can report support and resistance simultaneously; the primary situation is the active interaction closest to its zone edge, with support winning an exact tie. The API includes the assessment in each opportunity read model, and the dashboard presents both interactions and expandable level provenance. Assessments are persisted as immutable SQL history by `20261105120000_AddMarketSituationHistory`; deterministic replay is idempotent and a conflicting write fails. See [ADR-009](./docs/adr/ADR-009-market-situation-level-interaction.md).

Current-session levels come only from confirmed swings when current structure, price data, and retained session history are complete. Prior-session swings and high/low are eligible only from complete, usable, gap-free context with full coverage and matching provenance. Incomplete prior context contributes no levels. The assessment never predicts a hold, bounce, reversal, breakdown, rejection, breakout, or failed breakout, and does not change setup, plan geometry, publication, tracking, or readiness.

The versioned configuration keys are `Scanner:MarketSituation:TestZonePercent` and `Scanner:MarketSituation:ApproachDistancePercent`. The shipped `scanner-config-v18` defaults are 0.10% test-zone half-width and 0.25% distance from the zone edge; both are provisional development values, not market-calibrated. A level's test zone is centered on its price, and approach/crossed relevance uses the percentage distance from the active zone edge.

The current `scanner-config-v19` also adds `Scanner:MarketReaction:ConfirmationDistancePercent` (0.10%) and `Scanner:MarketReaction:MaxEpisodeDurationSeconds` (300 seconds). These are provisional development defaults, not market-calibrated. Confirmation requires a later eligible trade to clear the original pinned zone edge by the configured percentage; unresolved episodes expire at the event-time duration boundary. Changing these policy values requires a new scanner configuration version.

`market-reaction-v1` classifies six reactions: `SupportHeld`, `SupportBreakdown`, `SupportReclaimed`, `ResistanceRejected`, `ResistanceBreakout`, and `ResistanceFailedBreakout`. Unresolved episodes expire by event time; unresolved episodes restored after restart become terminal `ContinuityUnknown` before new observations can advance them. `level_interaction_episode` stores controlled episode lifecycle, `market_reaction_assessment` stores immutable reaction history, and `market_reaction_research_context` stores an immutable descriptive sidecar. Health/publication ticks do not progress reactions. Support and resistance episodes may coexist; per-level interaction latches prevent duplicate starts while an interaction remains eligible. `GET /api/v1/reactions` and opportunity responses expose current projections, and the dashboard has a distinct MARKET REACTION section. The confirmation distance (0.10%) and unresolved duration (300 seconds) in `scanner-config-v19` are provisional development thresholds, not market-calibrated. Research context is not a confirmation gate; Momentum and PreTradeReadiness behavior are unchanged. See [ADR-010](./docs/adr/ADR-010-market-reaction-confirmation.md).

## A.2.3a prior-session context

The separate `prior-session-context-v1` contract reports prior-session trade-point pivots, structural references, completeness, and feed/source provenance at `/api/v1/symbols/{symbol}/prior-context`. The dashboard labels partial-session extrema as observed values and reports session high/low/close only when full, gap-free coverage is established. Close means the last eligible trade by event time at or before session end, not an asserted official exchange close.

The recorded fixture remains the shared offline default and retains its intentionally partial fixture history. A.2.5 also production-wires the optional live Alpaca path: before a live symbol/session's first eligible analysis, the calendar adapter resolves the previous valid regular session and the historical REST adapter retrieves explicit SIP trades. Successful bounded pagination over the exact inclusive session request establishes declared full-session retrieval coverage; partial, failed, malformed, unauthenticated, non-entitled, cancelled, or timed-out retrieval never becomes usable complete context. The approved reconstructor freezes `prior-session-context-v1` for that exact current symbol/session, and the scanner falls back to honest `CurrentSessionOnly` analysis rather than delaying unrelated symbols indefinitely.

Live warm-ups are single-flight per current provider/feed/symbol/session and relevant version scope, bounded by infrastructure concurrency and timeout settings, and cancelled when a later explicit session supersedes the old scope. Raw historical trades are not persisted or retained after reconstruction. A completed warm-up may enrich MarketSituation, and its prior-level provenance can pin an ordinary MarketReaction episode, but setup, TradePlan, and readiness behavior do not change. Historical and live feeds remain distinct, so Alpaca SIP history can enrich a configured Alpaca IEX stream without relabeling that live stream as SIP. See [ADR-007](./docs/adr/ADR-007-prior-session-context.md) and [ADR-011](./docs/adr/ADR-011-live-prior-session-historical-source.md).

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

The `http` launch profile starts the development app at `http://localhost:5228`. Compose publishes SQL Server only on the IPv4 loopback address at `127.0.0.1,14333`, avoiding the default SQL Server host port `1433`; SQL Server still listens on `1433` inside the container. Open the app URL; the fixture scanner starts automatically. Check the API:

```powershell
Invoke-RestMethod http://localhost:5228/health
Invoke-RestMethod http://localhost:5228/api/v1/system/status
Invoke-RestMethod http://localhost:5228/api/v1/opportunities
```

The local SQL Server uses a persistent Compose volume. `docker compose down` preserves data; **do not use `docker compose down --volumes` / `down -v` unless you intend to delete that volume**. A volume is not a backup.

## Configuration

Keep the shared [`appsettings.json`](./src/MarketOpportunity.Web/appsettings.json) provider at `Fixture`. Provider selection is `MarketData:Provider` (`Fixture`, `Alpaca`, or `Massive`); credentials do not select a provider. Configuration precedence is command line, environment variables, Development User Secrets, environment-specific JSON, then shared JSON. Restart the app after changing settings.

Readiness thresholds live under the versioned scanner configuration: `Scanner:PreTrade:ApproachDistanceRiskMultiple` (must be positive) sets how close price must be to the pinned entry area, in multiples of the plan's risk per share, before Setup Forming applies. The shipped default 1.0 is a provisional development value; changing it requires a new `Scanner:Version`, which participates in readiness assessment identity. MarketSituation thresholds and their provisional defaults are described in [A.2.4a market situation](#a24a-market-situation).

Select an optional provider locally through Development User Secrets. For example:

```powershell
dotnet user-secrets set "MarketData:Provider" "Alpaca" --project src\MarketOpportunity.Web
dotnet user-secrets set "Scanner:Universe:Symbols" "AAPL,MSFT" --project src\MarketOpportunity.Web
```

Store provider credentials and the explicit provider session ID in User Secrets as appropriate; never check them into source, pass them in command arguments, or expose them to browser code. User Secrets are not encrypted and are for local development only. Deployed secrets belong in Azure Key Vault. See the [provider configuration and criteria](./docs/adr/ADR-005-market-data-provider-criteria.md).

Alpaca historical infrastructure reuses `MarketData:Alpaca:ApiKey` and `SecretKey`; it does not introduce a second credential. Optional `MarketData:Alpaca:Historical` safety settings are `MaximumPages` (100), `MaximumRecords` (1,000,000), `PageSize` (10,000), and `CalendarLookbackDays` (14). `Operations:PriorSessionWarmup:TimeoutSeconds` (120) and `MaximumConcurrentWarmups` (4) bound live startup work. These are infrastructure safeguards, not analytical timeouts. The existing account's historical SIP entitlement has been demonstrated for an AAPL regular session; that does not claim every Alpaca account or plan has identical access.

## Scanner universe

Live adapters use the single comma-separated `Scanner:Universe:Symbols` setting. Entries are trimmed, uppercased, rejected if empty, and deduplicated case-insensitively while preserving first occurrence. Live mode requires a non-empty list. The fixture does not use this setting.

There is no fallback to legacy `MarketData:Alpaca:Symbols` or `MarketData:Massive:Symbols` keys. Choose and explicitly migrate the intended list before a live run; do not merge legacy lists automatically. The configured provider session ID is explicit; the application does not infer a market session from a calendar date.

## Market-data providers

- **Fixture:** offline default; uses recorded synthetic trades/quotes and does not require the live universe.
- **Alpaca:** optional market-data adapter; current provider for development/live-stream evaluation. Configure `MarketData:Alpaca:ApiKey`, `MarketData:Alpaca:SecretKey`, `MarketData:Alpaca:Feed`, and `MarketData:Alpaca:SessionId` in User Secrets. Feed entitlements and coverage are provider-defined.
- **Massive:** supported live trades/quotes adapter. Configure `MarketData:Massive:ApiKey` and `MarketData:Massive:SessionId` in User Secrets. The adapter does not currently reconnect.

Both adapters are market-data-only. Neither selects broker permissions, accesses a brokerage account, or submits orders. Alpaca reconnects after disconnect; unresolved live-stream gaps still suspend publication. For an approved live Alpaca scanner run, Batch 3 now performs bounded prior-session warm-up from explicit historical SIP before that symbol/session's first eligible analysis. It never falls back from SIP to IEX and keeps historical SIP provenance separate from the configured live feed. Massive has no A.2.5 historical warm-up capability. A historical `ReceivedTimeUtc` is the HTTP retrieval/normalization time, not an invented original market-receipt time, and is excluded from historical identity. Consult [Alpaca protocol and limitations](./docs/providers/alpaca-websocket.md) and [Massive protocol and limitations](./docs/providers/massive-websocket.md). Review [ADR-005](./docs/adr/ADR-005-market-data-provider-criteria.md) before changing provider behavior.

## Local SQL Server

The app requires SQL Server persistence. Development startup creates a clean database schema with EF Core `EnsureCreated`; if the database already exists, it applies the available reviewed migrations. Existing event/session migration behavior labels legacy rows `legacy-unknown`, rather than inferring a session.

Schema management is deliberately mixed: clean development schemas may use `EnsureCreated`; existing schemas use reviewed hand-written, idempotent migrations. The repository has no EF model snapshot or initial migration. Do not generate a model migration blindly; it may describe the entire model. Review the migration-generation strategy before changing this convention. The current TradePlan schema is introduced by an explicit migration, tracked-candidate/readiness tables (`tracked_opportunity`, `pre_trade_readiness_assessment`) by `20261101120000_AddTrackedOpportunityReadiness`, MarketSituation history by `20261105120000_AddMarketSituationHistory`, and A.2.4b episode/reaction history and research context by `20261106120000_AddLevelInteractionReactionPersistence` and `20261106150000_AddMarketReactionResearchContext`. See [ADR-002](./docs/adr/ADR-002-ef-sqlserver-conventions.md), [ADR-006](./docs/adr/ADR-006-immutable-trade-plan-persistence.md), [ADR-008](./docs/adr/ADR-008-tracked-opportunity-readiness.md), [ADR-009](./docs/adr/ADR-009-market-situation-level-interaction.md), and [ADR-010](./docs/adr/ADR-010-market-reaction-confirmation.md).

SQL integration tests use `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` (environment variable first, then the integration-test project's User Secrets) and accept only `127.0.0.1,14333` with the exact `MarketOpportunityEngine_IntegrationTests` catalog. They must never target the development or live-validation database. Independently verify the effective connection's catalog with a read-only SQL Server query before any SQL test command; the code guard does not replace this preflight. If the dedicated catalog must be initialized on the repository's local Compose SQL Server, the explicit `sqlserver-test-init` service in the `validation` profile creates it only if absent and verifies it without dropping or resetting catalogs; it preserves the live-validation catalog setup and persistent SQL volume. Do not point that service or the tests at another SQL Server, and stop if the exact approved target cannot be confirmed. Follow the guarded procedure and database checks in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#milestone-a22---sql-validation-closeout-2026-10-30).

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
pwsh -NoProfile -File .\scripts\Start-AlpacaValidation.ps1 -Feed iex -ValidationSymbol AAPL
```

The launcher uses process-scoped validation configuration and does not migrate User Secrets. `-ValidationSymbol` bounds a validation run to one symbol without changing the configured universe; omit it only when the approved full-universe validation scope is intentional. Never point the launcher at the development database. For setup, preflight requirements, exact-session persistence checks, browser observations, and safe shutdown, follow the [A.1 validation record and runbook](./VALIDATION_HISTORY.md#consolidated-a1-validation-record-2026-10-29) and [Alpaca reference](./docs/providers/alpaca-websocket.md). Do not repeat provider validation simply because documentation changed.

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
- `GET /api/v1/reactions?symbol={symbol}&sessionId={sessionId}` (both filters are optional; returns current scanner reaction projections)
- `GET /api/v1/symbols/{symbol}/snapshot?sessionId={sessionId}` (`sessionId` is optional)
- `GET /api/v1/symbols/{symbol}/prior-context?sessionId={sessionId}` (`sessionId` is optional)
- `GET /api/v1/configuration/effective`

Opportunity list and detail include the immutable analytical opportunity, current publication decision/reason, candidate TradePlan, a `readiness` read model for the tracked candidate, and current `marketReactions` with episode, assessment, and descriptive research context. `readiness` remains null when no tracked candidate exists. The reactions endpoint filters the current scanner projection by optional symbol/session and does not query historical SQL assessments. The snapshot endpoint returns the versioned feature snapshot, including market-structure fields. `GET /api/v1/system/diagnostics` is available only in Development when opt-in validation diagnostics are enabled; it is not a normal status endpoint.

## Dashboard

The dashboard at `/` shows source/feed health and coverage, a compact **SYMBOL OVERVIEW** linking each current symbol/session to its detailed analysis, setup classification and quality, freshness, evidence, contradictions, vetoes, and publication state/reason. Each opportunity has a **MARKET REACTION** section showing reaction status/type, pinned level, event times, and descriptive research context; the UI explicitly states that context is not a confirmation gate or trading instruction. It also retains the primary **PRE-TRADE READINESS** section with the plain-language states Unavailable, Suspended, Watching, Setup Forming, Entry Area, Entry Ready ("Candidate entry conditions are aligned."), and No Longer Actionable, a compact explanation, and the context line. Each opportunity also keeps the **TRADE PLAN** section displaying candidate geometry and plan status. Publication health can suspend presentation without rewriting immutable setup, plan, readiness, or reaction history.

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
- The recorded fixture produces no confirmed structure, so it yields no tracked candidates; readiness reports no tracked candidate in fixture mode.
- The `Scanner:PreTrade:ApproachDistanceRiskMultiple` default (1.0) is provisional and not market calibrated; it makes no profitability claim.
- The scanner has no `PositionContext` and cannot infer whether a user entered a trade. `MANAGE`, `TAKE PROFIT`, and `EXIT` states are not implemented.
- Breakout, VWAP reclaim/rejection, and failed-breakout setup classifiers are not implemented. A.2.4a reports descriptive approaching/testing/crossed support and resistance; A.2.4b separately classifies the six approved mechanical reactions without changing setup or readiness. Reaction research context is descriptive and does not label outcomes or imply profitability.
- A.2.5 historical warm-up enriches live Alpaca analysis only; there is no general historical event-read API, historical SQL cache, live-gap backfill, outcome labeling, backtesting, ML, or ONNX inference.
- Raw vendor payloads are not persisted; normalized-event retention has occurred for local evaluation. See [ADR-004](./docs/adr/ADR-004-market-event-retention.md) before sustained research/backtesting/model-training use.
- Live Alpaca warm-up uses provider calendar/history; Massive has neither historical warm-up nor reconnect behavior, and no provider has live historical gap recovery.
- No Azure deployment infrastructure or production migration orchestration.
- No broker integration or order execution.

## Documentation map

- [Master specification and roadmap](./Market_Opportunity_Engine_Final_Master_Spec.md): authoritative product, architecture, and future contracts.
- [PROJECT_STATUS.md](./PROJECT_STATUS.md): current checkpoint, completed slices, blockers, and immediate next step.
- [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md): historical test, live-run, SQL, and browser validation evidence.
- [docs/adr/](./docs/adr/): durable architecture decisions (ADR-001 through ADR-010).
- [docs/providers/](./docs/providers/): provider protocol references and current adapter limitations.
- [scripts/](./scripts/): opt-in live validation tooling.
