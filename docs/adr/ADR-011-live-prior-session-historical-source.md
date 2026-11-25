# ADR-011: Live prior-session historical source

- **Status:** Accepted
- **Decision:** Add provider-independent market-calendar and historical-trade infrastructure, with Alpaca as the initial A.2.5 historical provider. Batch 1 adds no production prior-session warm-up.

## Context

A.2.3a can represent prior-session context, but live sources currently lack an authoritative calendar and historical trade input. A read-only Alpaca capability test established calendar and SIP historical-trade access for the user's account and returned a complete regular session across 81 paginated responses. It also observed that a bare Alpaca historical trade ID is not globally unique enough.

## Decision

- `IMarketSessionCalendar` resolves an immediately preceding valid regular session through provider calendar semantics. It does not subtract dates, infer holidays, or roll UTC dates.
- `IHistoricalMarketDataSource` remains separate from live `IMarketDataSource`. A historical result carries the requested symbol/session bounds, provider/feed, source kind, normalized trades, transport status, pagination status, and provenance.
- Alpaca calendar requests use the market-calendar API and preserve provider regular-session open/close values, including early closes, normalized from `America/New_York` to UTC.
- Alpaca historical trade requests explicitly use `feed=sip`, ascending order, and the documented stock-trades REST endpoint. There is no IEX fallback.
- Historical trade identity includes provider, feed, symbol, exchange, historical trade ID, and event timestamp. The composite value is hashed before use as the canonical sequence identity; a bare provider trade ID is never used as the global identity.
- `EventTimeUtc` is the provider trade timestamp. `ReceivedTimeUtc` records the HTTP retrieval/normalization time because no original live-receive time exists. Retrieval time is not part of the composite historical identity or analytical event ordering.
- Retrieval distinguishes pagination-exhausted success from calendar/session completeness. `SuccessPaginationExhausted` reports transport completion only.
- Pagination detects repeated tokens and is bounded by explicit configured page and normalized-record limits. Reaching a bound returns `LimitExceeded` with the partial result; it never silently truncates a session and calls it complete.
- Default infrastructure safeguards are 100 pages, 1,000,000 normalized records, 10,000 records per request, and a 14-calendar-day lookup range. These are resource bounds, not analytical completeness claims.
- The existing Alpaca credentials are reused. Shared configuration keeps `MarketData:Provider = Fixture`; selecting live Alpaca does not itself select or wire historical warm-up.
- Batch 2 reconstructs `prior-session-context-v1` through the provider-independent `PriorSessionHistoricalReconstructor`. The provider/feed/source kind and requested symbol/session/bounds must match explicit reconstruction requirements.
- Historical request bounds are inclusive `MarketSessionDefinition.OpenAtUtc` through `CloseAtUtc`. Trades do not need to occur exactly at either boundary.
- A context is analytically `Available` only when retrieval status is `SuccessPaginationExhausted`, pagination actually exhausted, the result explicitly declares complete gap-free coverage, provider coverage spans the full inclusive session bounds, there are no known gaps, and canonical trades/provenance are valid. Pagination alone never promotes a context. A current Alpaca Batch 1 result that does not declare complete coverage remains `Partial`; absent provider guarantees are not invented.
- Incomplete transport with observations is `Partial`; provider/authentication/entitlement failure and no eligible trades are `Unavailable`; malformed responses, contradictory request/provenance, invalid canonical values, and identity conflicts are `Invalid`.
- A usable session high/low is the decimal maximum/minimum eligible in-session canonical trade price. Analytical close remains the last eligible canonical trade by event time at or before the inclusive regular-session close, not an official closing print.
- The existing `session-swing-fractal-v1` algorithm runs once over the deduplicated event-time-ordered trades inside that prior session. Current-session events cannot confirm prior-session pivots. End-of-session pivots without two later prior-session trades remain unconfirmed.
- Existing `PriorSessionContext` representation is retained: latest/previous confirmed high/low summaries plus all confirmed prior swing candidates. The latter remains bounded by the historical record safety limit; no significance filtering, truncation, clustering, or ranking is added in Batch 2.
- Historical retrieval time is excluded from reconstructed context identity and analytical values. Invalid canonical price or size invalidates the context.
- Batch 2 adds neither historical SQL persistence/cache nor production scanner invocation.

## Consequences

- Alpaca calendar and historical SIP adapters are available as independently registered concrete infrastructure services. They are not registered as the production calendar/historical interfaces and are not used by the live scanner in Batches 1-2.
- Fixture prior-context behavior remains unchanged, while an offline approved complete historical result can now reconstruct the existing usable context contract.
- Historical retrieval can be tested offline with deterministic HTTP responses and bounded synthetic pagination.
- Batch 2 owns no production invocation. Batch 3 owns startup warm-up and production integration. Batch 4 owns final validation and checkpoint closeout.

See [ADR-005](./ADR-005-market-data-provider-criteria.md), [ADR-007](./ADR-007-prior-session-context.md), and the approved [A.2.5 scope](../../Market_Opportunity_Engine_Final_Master_Spec.md#a25-live-prior-session-historical-context).
