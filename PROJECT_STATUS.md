# Current Project Status

**Checkpoint:** Milestone A.2.2 - Trade Plan Foundation
**Updated:** 2026-10-30

## Current status

The A.2.1 foundation implements the scanner-owned symbol universe and descriptive confirmed swing contracts. A.2.2 adds deterministic structural candidate trade plans, immutable SQL persistence, API exposure, and descriptive dashboard presentation without changing Momentum Continuation decisions. The latest self-describing 30-minute IEX run is recorded in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#self-describing-30-minute-iex-closeout-2026-10-29). It verifies live IEX ingestion, exact-session SQL reconciliation, provenance, vetoes, pipeline accounting, completed drain, `DurationElapsed` shutdown, exit code 0, and no Error/Critical app-log entries.

**Milestone A.1 - Live Scanner Validation: COMPLETE**

**Milestone A.2.2 - Trade Plan Foundation: COMPLETE**

The final [same-page browser check](./VALIDATION_HISTORY.md#final-same-page-browser-observation-2026-10-30), session `alpaca-iex-20261030T174047331Z`, closes the remaining observability criterion. Two timestamped DOM snapshots, 65.066 seconds apart, show increasing event counts, advancing event/receive times, changing prices/features, and changing classifications, vetoes, and publication states without refresh. Feed health remained Live, with no visible reconnect/failure UI or captured browser exceptions during the interval. No application or provider was restarted.

## Current proven capabilities

- Real Alpaca IEX events have traversed the application and been persisted.
- Feature calculations and Momentum Continuation evaluations have occurred on live IEX events.
- Blazor live behavior has previously been observed.
- Candidate trade plans are separate, versioned analytical records; they do not represent execution readiness or modify setup classification.
- Reconnect/publication safety is implemented.
- Opt-in live-validation instrumentation is implemented.
- Graceful shutdown drains accepted in-process work within a bounded timeout; deterministic and integration tests verify the drain and its accounting.
- No analytical or trading behavior changed during instrumentation or shutdown fixes.

## Final A.1 browser evidence

No remaining A.1 blocker. The browser check directly observed the existing integrated-browser page at `http://localhost:5291/`, with unchanged page time origin and zero navigation events. See the [timestamped comparison](./VALIDATION_HISTORY.md#final-same-page-browser-observation-2026-10-30); post-duration disconnect behavior is recorded separately from live-session behavior.

Extended browser monitoring remained Live without reconnect/failure UI or browser exceptions until the configured 1,800-second run requested `DurationElapsed` shutdown at `2026-10-30T18:11:17.1925747Z`. Disconnect/rejoin UI and negotiation errors began only afterward. The run stopped normally with exit code 0, completed drain, and zero remaining events; no early shutdown was initiated.

The launcher and app record self-describing session evidence: a sanitized `<SessionId>.app.log`, recorded shutdown trigger and duration, per-stage latency threshold counts, and `<SessionId>.launcher-result.json` with exit code and exit time. The 30-minute run verified these artifacts. Follow the README [final integrated-browser validation workflow](./README.md#final-integrated-browser-validation-workflow). Ingestion, channel capacity, analysis interval, persistence, formulas, thresholds, scoring, classifications, and quote handling are unchanged.

## A.2.1 foundation

- `Scanner:Universe:Symbols` is the canonical comma-separated live universe. Both adapters share the same trim, uppercase, empty-entry rejection, and case-insensitive first-occurrence deduplication. Fixture replay does not consult the universe.
- Provider-specific symbol keys are no longer read, and there is no automatic fallback: legacy lists can differ. Operators must explicitly migrate their intended universe in User Secrets as described in the [README](./README.md#scanner-owned-universe-and-user-secrets-migration). No local settings were changed by the implementation.
- `features-v16` and `scanner-config-v16` expose latest/previous confirmed swings, higher/lower/equal swing relationships, combined structure state, nearest support/resistance candidates with provenance, and distances to the nearest confirmed highs/lows. They reuse the existing two-sided trade-point fractal and remain descriptive, event-time-safe, session-scoped, and bounded-state-aware.
- The feature-set version advanced for new serialized content; no configuration threshold changed. `momentum-continuation-v1`, its classifications/scoring/vetoes/publication, and prior persisted snapshots/decisions remain unchanged. Structure stays in existing snapshot JSON; no schema migration is required.

## A.2.1 validation

- Focused structure, universe, provider-option, and replay tests passed; the complete offline unit suite passed **262/262**.
- Momentum Continuation regression tests passed **22/22**; detector source and setup behavior are unchanged.
- In-process hosted fixture and synthetic-structure API/dashboard tests passed **2/2** with a capturing store; neither opens SQL. SQL-backed hosted-fixture coverage is separate.
- Offline launcher interception tests passed **17/17**, with no app, SQL, Docker, or provider connection started by those tests.
- `dotnet build MarketOpportunityEngine.sln --no-restore` succeeded with zero warnings and errors.
- SQL-backed hosted-fixture and persistence coverage was **not run**: no guarded `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was configured, and the existing SQL service was not retargeted or modified. No schema migration was needed, and no live-provider validation was repeated.

## A.2.2 foundation

- `trade-plan-v1` builds a separate candidate plan from the directional opportunity and confirmed swing levels. Entry zones span nearest support/resistance and the reference price; entry reference is their exact decimal midpoint. Invalidation uses a distinct confirmed swing beyond the entry structure, and Target 1 is the nearest opposing confirmed level. Missing structure remains insufficient/forming; directionally inconsistent geometry is rejected.
- Risk/share, reward/share, and reward/risk are decimal calculations. No minimum reward/risk veto, artificial zone width, target projection, or execution state was introduced. Market-data publication health remains separate from immutable plan geometry.
- Plan IDs incorporate the snapshot/opportunity identities, direction/status, plan-definition and analytical versions, geometry, evidence, and data provenance. SQL persistence uses a separate `trade_plan` row keyed by immutable `PlanId`; replay of identical content is idempotent and conflicting content fails. [ADR-006](./docs/adr/ADR-006-immutable-trade-plan-persistence.md) records the persistence decision.
- `/api/v1/opportunities` and the detail endpoint include the plan. Blazor cards render a descriptive **TRADE PLAN** section and continue to show publication state separately. `features-v16`, `scanner-config-v16`, and `momentum-continuation-v1` are unchanged.

## A.2.2 validation

- Focused trade-plan, A.2.1 market-structure, and Momentum Continuation regression tests passed **52/52**.
- Complete offline unit suite passed **279/279**.
- Hosted fixture/API/dashboard tests passed **2/2** using an in-process capturing store; no SQL or provider connection was opened.
- SQL persistence/migration coverage passed **6/6** against the existing dedicated local `MarketOpportunityEngine_IntegrationTests` database. Clean-schema creation and existing-schema migration verify `trade_plan` columns and ordered indexes; long/short plans round-trip all fields including null Target2, exact replay is idempotent, and conflicting content under the same plan ID fails without rewriting history. Existing event, feature, opportunity, evidence, and configuration persistence also passes.
- SQL-backed hosted fixture/API coverage passed **1/1 twice** against the same guarded database without reset. All four HTTP plans match persisted plans; all six table row counts remain unchanged on repeat. No development/live-validation database or live provider was used. See the [SQL closeout evidence](./VALIDATION_HISTORY.md#milestone-a22---sql-validation-closeout-2026-10-30).
- The final required sequence passed: persistence/migrations **6/6**, SQL-backed hosted fixture **1/1**, repeat **1/1**, full offline units **279/279**, then solution build. Only tests and documentation changed; production code is unchanged.
- `dotnet build MarketOpportunityEngine.sln --no-restore` succeeded with **0 warnings and 0 errors**.
