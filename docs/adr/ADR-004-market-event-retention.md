# ADR-004: Raw and normalized event retention

- **Status:** Accepted
- **Decision:** Persist canonical normalized trade and quote events for this checkpoint; do not retain vendor-specific raw payloads yet.

## Context

Fixture, Alpaca, and Massive adapters now produce normalized canonical events; live Alpaca evaluation has occurred. Retaining provider-specific payloads would create a separate vendor schema and a data-use/licensing commitment. The application persists normalized events and provider-specific diagnostic metadata, but not raw vendor messages.

## Decision

Persist provider-independent events with provider name, symbol, explicit session identity, event time, receive time, stable event identity, and trade/quote values. A fixture or provider supplies session identity; the scanner does not infer a US market session from a UTC date. Keep the source fixture as a version-controlled replay input. Do not persist source DTOs or raw vendor messages unless a separately reviewed decision establishes that need and verifies applicable rights. Provider-specific diagnostic metadata is bounded operational evidence, not raw-payload retention.

## Consequences

- Analysis can be replayed from fixtures and canonical event rows, but exact reconstruction of a vendor payload is unavailable.
- Event and analysis persistence is scoped by symbol and explicit session identity.
- Local evaluation retention of normalized events has occurred. Before sustained historical research, backtesting, or model training, decide and verify provider data-use/license terms, normalized/derived-data retention rights and duration, permitted research/backtesting and ML-training use, deletion/expiration policy, and required provider/feed provenance.
- Do not infer or claim provider licensing rights; the applicable terms and permissions require verification and an explicit decision before those uses.
