# ADR-003: In-process market-event transport

- **Status:** Accepted
- **Decision:** Use an in-process bounded `Channel<MarketEvent>` for live ingestion and analysis, with backpressure, bounded graceful drain, and explicit accounting. Fixture replay remains in-process and does not require a separate distributed transport.

## Context

The fixture is finite and replayed once at startup. Live adapters produce continuously, so ingestion and analysis need a bounded producer/consumer boundary. The live channel path has been implemented and validated.

## Decision

Fixture events are processed in-process. Live canonical events pass through a bounded .NET `Channel<MarketEvent>` between ingestion and analysis, with explicit capacity, single-writer/reader settings, cancellation, and a documented wait/drop policy. Backpressure (`BoundedChannelFullMode.Wait`) prevents silent event loss.

On normal live-scanner shutdown, stop accepting new input, complete and drain the channel, finish analysis of already accepted pending work, and attempt its persistence within a validated bounded timeout. Host cancellation alone must not make accepted work appear successfully drained. A timeout or processing/persistence failure is surfaced with explicit remaining-work accounting. The current operational default is 20 seconds; this value is configuration, not an unbounded guarantee.

## Consequences

- No distributed broker is introduced. The fixture path remains independent of continuous live transport.
- The live path makes pressure, shutdown, failed work, and remaining work observable.
- Successful shutdown means accepted work has reconciled; channel depth zero alone is insufficient. Work that exceeds the drain budget is reported as incomplete, not silently treated as success.
- The canonical event contract remains the transport boundary.
