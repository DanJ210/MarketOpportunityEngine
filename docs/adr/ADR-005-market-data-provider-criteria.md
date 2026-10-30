# ADR-005: Market-data provider selection criteria

- **Status:** Accepted
- **Decision:** Keep provider implementations behind `IMarketDataSource`. Massive remains supported; Alpaca Market Data is the current development/live-stream testing provider. The fixture remains the offline default. Neither provider is authorized for order execution by this decision.

## Required evaluation criteria

1. Real-time US equities/ETF trade and quote coverage for the intended universe.
2. Exchange/provider event timestamps, receive-time observability, and stable sequence/event identity.
3. Historical trades/quotes sufficient for deterministic replay and gap recovery, with clearly stated retention.
4. WebSocket transport, subscription limits, heartbeat behavior, reconnect guidance, and duplicate/out-of-order semantics.
5. Reliability/SLA evidence, incident communication, and practical reconnect/backfill behavior.
6. Data entitlement, display/non-display usage, derived-data rights, retention restrictions, and redistribution limits.
7. Transparent pricing at the expected symbol count and message rate, including exchange fees.
8. Credential security, test/sandbox access, and operational support.

## Consequences

- The recorded fixture remains the default source for local development and offline tests.
- Alpaca is used to exercise the real WebSocket ingestion path during development, including the synthetic test stream; it does not establish a final-provider commitment.
- Massive remains a supported real-time trades-and-quotes adapter. Provider selection does not imply brokerage, account-linking, or order permissions.
- The operator configures provider, feed, credentials, and explicit session ID separately from the scanner-owned `Scanner:Universe:Symbols` universe; the application does not infer sessions from dates. Both live adapters use the same normalized universe. The fixture does not depend on it.
- The old provider-specific symbol settings are not read or merged. Existing local configurations require an explicit one-time migration to the scanner universe.
- Provider/feed coverage and real-time/delayed capability metadata travel with analysis provenance. Analytics must not infer feed semantics by matching provider names.
- Confirm applicable real-time feed entitlements, personal-use/display/retention terms, and costs for the intended usage before relying on live data beyond local evaluation.
- Provider-specific DTOs will live in an infrastructure adapter behind `IMarketDataSource`.
- A provider change or additional provider requires an explicit decision; familiarity alone is not a selection criterion.
