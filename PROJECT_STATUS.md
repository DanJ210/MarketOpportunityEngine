# Current Project Status

**Checkpoint:** Milestone A.1 - Live Scanner Validation
**Updated:** 2026-10-29

## Current status

The completed instrumented IEX observation is recorded in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#completed-instrumented-iex-observation-2026-10-29). It verifies live IEX ingestion, exact-session SQL persistence, pipeline accounting, and scanner drain.

**Milestone A.1 is blocked by one evidence gap:** session-specific sanitized launcher/application logs and browser/circuit evidence are not present in the reviewed artifacts. The exact shutdown trigger and process exit code, absence of fatal host logs, and live Blazor behavior therefore remain unverified. No application or provider was restarted during the analysis.

## Current proven capabilities

- Real Alpaca IEX events have traversed the application and been persisted.
- Feature calculations and Momentum Continuation evaluations have occurred on live IEX events.
- Blazor live behavior has previously been observed.
- Reconnect/publication safety is implemented.
- Opt-in live-validation instrumentation is implemented.
- Graceful shutdown drains accepted in-process work within a bounded timeout; deterministic and integration tests verify the drain and its accounting.
- No analytical or trading behavior changed during instrumentation or shutdown fixes.

## Remaining A.1 blocker

Recover the missing sanitized logs and browser/circuit evidence for the completed session, or otherwise leave the dashboard and host-exit criteria unverified. Do not infer either from backend diagnostics. See the [consolidated validation history](./VALIDATION_HISTORY.md#completed-instrumented-iex-observation-2026-10-29).

The launcher and app now record self-describing session evidence for any final run: a sanitized `<SessionId>.app.log`, the recorded `shutdownTrigger`/`shutdownRequestedUtc`/`configuredDurationSeconds`, per-stage latency counts above 1/5/10/15 s, and a `<SessionId>.launcher-result.json` with the child exit code and exit time. This is verified by offline tests only; no provider connection was made. Follow the README [final integrated-browser validation workflow](./README.md#final-integrated-browser-validation-workflow). Ingestion, channel capacity, analysis interval, persistence, formulas, thresholds, scoring, classifications, and quote handling are unchanged.

## Post-A.1 housekeeping

After A.1, move the provider-specific universe setting:

`MarketData:Massive:Symbols` -> `Scanner:Universe:Symbols`

The provider-neutral symbol universe should normalize, trim, and deduplicate symbols. This refactor is not part of A.1 and is not implemented.
