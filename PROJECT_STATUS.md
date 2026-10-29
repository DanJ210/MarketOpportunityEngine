# Current Project Status

**Checkpoint:** Milestone A.1 - Live Scanner Validation
**Updated:** 2026-10-29

## Current status

The final instrumented market-hours IEX run remains outstanding.

## Current proven capabilities

- Real Alpaca IEX events have traversed the application and been persisted.
- Feature calculations and Momentum Continuation evaluations have occurred on live IEX events.
- Blazor live behavior has previously been observed.
- Reconnect/publication safety is implemented.
- Opt-in live-validation instrumentation is implemented.
- Graceful shutdown drains accepted in-process work within a bounded timeout; deterministic and integration tests verify the drain and its accounting.
- No analytical or trading behavior changed during instrumentation or shutdown fixes.

## Remaining A.1 requirement

Complete one final instrumented IEX run during market hours to validate:

- timing instrumentation;
- provider metadata on actual events;
- pipeline/backpressure behavior;
- Blazor live updates; and
- graceful-shutdown accounting against actual live data.

The fixture remains the offline default. Do not treat synthetic feed evidence or earlier uninstrumented IEX sessions as a substitute for this run. See the [operational procedure](./README.md#one-final-instrumented-a1-observation) and [consolidated validation history](./VALIDATION_HISTORY.md#consolidated-a1-validation-record-2026-10-29).

## Post-A.1 housekeeping

After A.1, move the provider-specific universe setting:

`MarketData:Massive:Symbols` -> `Scanner:Universe:Symbols`

The provider-neutral symbol universe should normalize, trim, and deduplicate symbols. This refactor is not part of A.1 and is not implemented.
