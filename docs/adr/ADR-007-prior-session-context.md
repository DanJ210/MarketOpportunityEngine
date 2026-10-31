# ADR-007: Prior-session context source and lifecycle

- **Status:** Accepted
- **Decision:** Define prior-session context independently from live market ingestion and rebuild it deterministically from the recorded fixture. Do not persist prior-session contexts or infer a live market calendar until an approved historical source and authoritative calendar are available.

## Context

A.2.3a needs stable, auditable historical reference levels for the immediately preceding eligible session. Existing providers implement read-only WebSocket streams, SQL stores normalized events without a historical read/replay interface, and `SessionId` is explicit but is not a market-calendar identity. Provider history entitlements, retention, and licensing are not established by ADR-004 or ADR-005.

## Decision

- Keep live `IMarketDataSource` separate from `IHistoricalMarketDataSource`.
- Use an explicit fixture calendar mapping with U.S. equity regular-session boundaries expressed in UTC from `America/New_York` semantics. It handles only the recorded fixture session identities; it does not calculate holidays or extrapolate dates.
- Use the fixture as the only historical source for this checkpoint. Its sample does not span the full exchange session, so it reports partial coverage and observed extrema; prior-session high, low, and close remain null. When complete coverage is available, close is defined as the last eligible normalized trade in event-time order at or before session end, not necessarily an official exchange closing print.
- Rebuild context using normalized provider-independent events and the existing `session-swing-fractal-v1` implementation. Include content, definition version, coverage, provenance, and explicit session identities in deterministic context identity. Current-session price distances are a separate projection and do not change context identity.
- Do not write contexts or historical vendor data to SQL. Fixture input is deterministic across restart. Existing persisted live events are not considered an approved historical replay source until a read path and retention policy are reviewed.
- For non-fixture sources, or unresolved calendar/data reads, report prior context unavailable and continue current-session scanning. Partial/invalid context remains explicit and must not be used as complete history.

## Consequences

- No SQL migration or provider-specific DTO is added.
- Live Alpaca/Massive historical retrieval and authoritative session-calendar resolution remain unavailable/unverified.
- Future readiness work must refuse incomplete historical context and may add persistence only with an approved source and its retention/entitlement decision.
