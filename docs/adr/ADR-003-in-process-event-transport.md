# ADR-003: In-process market-event transport

- **Status:** Accepted
- **Decision:** Keep the fixture scanner synchronous and in-process; use a bounded `Channel<T>` only when a producer/consumer boundary is required. A live scanner must drain accepted in-process work on normal shutdown within a bounded timeout.

## Context

The current fixture is finite and replayed once at startup. Adding a queue to this path would add lifecycle and backpressure complexity without improving the vertical slice.

## Decision

The fixture source yields canonical events directly into the event-time state. For future continuously producing sources, use a bounded .NET `Channel<MarketEvent>` between ingestion and analysis, with explicit capacity, single-writer/reader settings, cancellation, and a documented wait/drop policy. Start with backpressure (`BoundedChannelFullMode.Wait`) so market events are not silently discarded.

On normal live-scanner shutdown, stop accepting new input, complete and drain the channel, finish analysis of already accepted pending work, and attempt its persistence within a validated bounded timeout. Host cancellation alone must not make accepted work appear successfully drained. A timeout or processing/persistence failure is surfaced with explicit remaining-work accounting. The current operational default is 20 seconds; this value is configuration, not an unbounded guarantee.

## Consequences

- No distributed broker or in-process channel is introduced for the one-shot fixture.
- A live adapter must make pressure, shutdown, and dropped-event behavior observable when the channel boundary is added.
- Successful shutdown means accepted work has reconciled; channel depth zero alone is insufficient. Work that exceeds the drain budget is reported as incomplete, not silently treated as success.
- The canonical event contract remains the transport boundary.
