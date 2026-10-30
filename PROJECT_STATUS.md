# Current Project Status

**Checkpoint:** Milestone A.2.1 - Provider-Neutral Universe + Market Structure Foundation
**Updated:** 2026-10-30

## Current status

The A.2.1 foundation implements the scanner-owned symbol universe and descriptive confirmed swing contracts. The latest self-describing 30-minute IEX run is recorded in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#self-describing-30-minute-iex-closeout-2026-10-29). It verifies live IEX ingestion, exact-session SQL reconciliation, provenance, vetoes, pipeline accounting, completed drain, `DurationElapsed` shutdown, exit code 0, and no Error/Critical app-log entries.

**Milestone A.1 - Live Scanner Validation: COMPLETE**

The final [same-page browser check](./VALIDATION_HISTORY.md#final-same-page-browser-observation-2026-10-30), session `alpaca-iex-20261030T174047331Z`, closes the remaining observability criterion. Two timestamped DOM snapshots, 65.066 seconds apart, show increasing event counts, advancing event/receive times, changing prices/features, and changing classifications, vetoes, and publication states without refresh. Feed health remained Live, with no visible reconnect/failure UI or captured browser exceptions during the interval. No application or provider was restarted.

## Current proven capabilities

- Real Alpaca IEX events have traversed the application and been persisted.
- Feature calculations and Momentum Continuation evaluations have occurred on live IEX events.
- Blazor live behavior has previously been observed.
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
- `features-v15` and `scanner-config-v16` add the latest confirmed swing high/low, event-time confirmation and provenance, and explicit availability status. The existing two-sided fractal is unchanged; observations are descriptive, session-scoped, bounded-state-aware, and not trading instructions. `momentum-continuation-v1` and its classifications, scoring, vetoes, and publication rules are unchanged.
- Earlier persisted snapshots and decisions remain immutable; the new fields use the existing snapshot JSON and require no schema migration.

## A.2.1 validation

- Focused structure, universe, provider-option, and replay tests passed; the complete offline unit suite passed **260/260**.
- The in-process hosted API/dashboard test passed **1/1** with synthetic canonical events and a capturing store; it is not SQL persistence coverage.
- Offline launcher interception tests passed **17/17**, with no app, SQL, Docker, or provider connection started by those tests.
- `dotnet build MarketOpportunityEngine.sln --no-restore` succeeded with zero warnings and errors.
- SQL Server persistence and hosted-fixture integration coverage was **not run**: no guarded `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was configured, and the existing SQL service was not retargeted or modified. No live-provider validation was repeated.
