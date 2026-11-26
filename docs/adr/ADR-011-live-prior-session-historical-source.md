# ADR-011: Live prior-session historical source

- **Status:** Accepted
- **Decision:** Add provider-independent market-calendar and historical-trade infrastructure, with Alpaca as the initial A.2.5 historical provider, and production-wire bounded live Alpaca warm-up. A.2.5 and all four batches are COMPLETE.

## Context

A.2.3a can represent prior-session context, but live sources currently lack an authoritative calendar and historical trade input. A read-only Alpaca capability test established calendar and SIP historical-trade access for the user's account and returned a complete regular session across 81 paginated responses. It also observed that a bare Alpaca historical trade ID is not globally unique enough.

## Decision

- `IMarketSessionCalendar` resolves an immediately preceding valid regular session through provider calendar semantics. It does not subtract dates, infer holidays, or roll UTC dates.
- `IHistoricalMarketDataSource` remains separate from live `IMarketDataSource`. A historical result carries the requested symbol/session bounds, provider/feed, source kind, normalized trades, transport status, pagination status, and provenance.
- Alpaca calendar requests use the market-calendar API and preserve provider regular-session open/close values, including early closes, normalized from `America/New_York` to UTC.
- Alpaca historical trade requests explicitly use `feed=sip`, ascending order, and the documented stock-trades REST endpoint. There is no IEX fallback.
- Historical trade identity includes provider, feed, symbol, exchange, historical trade ID, and event timestamp. The composite value is hashed before use as the canonical sequence identity; a bare provider trade ID is never used as the global identity.
- `EventTimeUtc` is the provider trade timestamp. `ReceivedTimeUtc` records the HTTP retrieval/normalization time because no original live-receive time exists. Retrieval time is not part of the composite historical identity or analytical event ordering.
- Retrieval distinguishes pagination-exhausted success from calendar/session analytical completeness. `SuccessPaginationExhausted` reports transport completion; the adapter may separately declare full request coverage only under the approved Batch 3 equivalence below.
- Pagination detects repeated tokens and is bounded by explicit configured page and normalized-record limits. Reaching a bound returns `LimitExceeded` with the partial result; it never silently truncates a session and calls it complete.
- Default infrastructure safeguards are 100 pages, 1,000,000 normalized records, 10,000 records per request, and a 14-calendar-day lookup range. These are resource bounds, not analytical completeness claims.
- The existing Alpaca credentials are reused. Shared configuration keeps `MarketData:Provider = Fixture`; when Alpaca is explicitly selected, the production live path registers and uses this approved historical capability.
- Batch 2 reconstructs `prior-session-context-v1` through the provider-independent `PriorSessionHistoricalReconstructor`. The provider/feed/source kind and requested symbol/session/bounds must match explicit reconstruction requirements.
- Historical request bounds are inclusive `MarketSessionDefinition.OpenAtUtc` through `CloseAtUtc`. Trades do not need to occur exactly at either boundary.
- A context is analytically `Available` only when retrieval status is `SuccessPaginationExhausted`, pagination actually exhausted, the result explicitly declares complete gap-free coverage, provider coverage spans the full inclusive session bounds, there are no known gaps, and canonical trades/provenance are valid. Pagination alone never promotes a context; Batch 3's documented full-request pagination equivalence is what permits the adapter to declare the separate coverage evidence for subsequent reconstruction.
- Incomplete transport with observations is `Partial`; provider/authentication/entitlement failure and no eligible trades are `Unavailable`; malformed responses, contradictory request/provenance, invalid canonical values, and identity conflicts are `Invalid`.
- A usable session high/low is the decimal maximum/minimum eligible in-session canonical trade price. Analytical close remains the last eligible canonical trade by event time at or before the inclusive regular-session close, not an official closing print.
- The existing `session-swing-fractal-v1` algorithm runs once over the deduplicated event-time-ordered trades inside that prior session. Current-session events cannot confirm prior-session pivots. End-of-session pivots without two later prior-session trades remain unconfirmed.
- Existing `PriorSessionContext` representation is retained: latest/previous confirmed high/low summaries plus all confirmed prior swing candidates. The latter remains bounded by the historical record safety limit; no significance filtering, truncation, clustering, or ranking is added in Batch 2.
- Historical retrieval time is excluded from reconstructed context identity and analytical values. Invalid canonical price or size invalidates the context.
- Batch 2 adds neither historical SQL persistence/cache nor production scanner invocation.
- Batch 3 uses a `PriorSessionContextProvider` as the live warm-up coordinator. The scope contains the current live provider/feed, symbol, explicit current session, resolved prior session, historical provider/feed/source kind, and relevant context/feature/configuration versions.
- Startup policy is terminal-before-first-eligible-analysis for each symbol/session. A scope starts when its first canonical event establishes identity; pending analyses are skipped while unrelated scopes continue. A completed or terminal failed task is frozen and reused. Analysis never starts from non-deterministic late enrichment, and no earlier immutable assessment is rewritten.
- Warm-ups are single-flight and bounded by infrastructure-only timeout/concurrency settings. Cancellation applies to an individual waiter without cancelling shared work for another waiter on the same scope. Authentication, entitlement, limit, provider, invalid-data, reconstruction, cancellation, and timeout outcomes are distinct terminal results. Each falls back to honest `CurrentSessionOnly` behavior without changing live provider health or stopping the host.
- A later explicit session activates an independent scope and cancels obsolete warm-up work where possible. Terminal state is keyed so an old completion cannot replace the new session's current projection.
- For Alpaca SIP, successful exhaustion of bounded pagination for the exact inclusive full regular-session request is the approved transport equivalence for declared request coverage. This is not a market-quality or profit claim: the provider-independent reconstructor still independently requires matching provenance, full boundaries, valid canonical trades, no known gaps, and a complete result before it can produce `Available`.
- Reconstructed context is retained as an immutable projection for the symbol/session: the existing latest/previous swing summaries and all confirmed candidates remain bounded by the historical record limit. Downloaded historical trade arrays are not persisted, cached as SQL, or retained after reconstruction.
- Historical SIP provenance remains distinct from the configured live stream feed. A live Alpaca IEX analysis may consume Alpaca SIP prior history while reporting mixed provenance.
- Prior context may add approved MarketSituation levels and those levels may start ordinary MarketReaction episodes. Setup detection, TradePlan geometry, tracked opportunity, readiness, and their versions remain unchanged. Reaction episodes carry immutable level/source/provider/feed/session provenance without a new SQL table.

## Consequences

- Alpaca calendar and historical SIP adapters are registered as the production calendar/historical interfaces only when `MarketData:Provider` explicitly selects Alpaca. Fixture remains the shared default, and Massive has no A.2.5 historical adapter.
- Fixture prior-context behavior remains unchanged, while an offline approved complete historical result can now reconstruct the existing usable context contract.
- Historical retrieval can be tested offline with deterministic HTTP responses and bounded synthetic pagination.
- Live warm-up completion is separate from live-feed health. A symbol's historical failure cannot suspend another symbol's current-session analysis solely because of the historical request.
- Batch 4 completed final offline, hosted, and guarded provider validation. A 2026-11-26 AAPL completed-session validation resolved the 2026-11-24 regular session from the Alpaca calendar, exhausted 73 bounded SIP pages (722,167 normalized records, zero duplicates), declared no known gaps, and reconstructed usable complete `prior-session-context-v1` without retaining raw trades. A separate after-hours hosted run authenticated the IEX stream but produced no market events; regular-session ScannerPipeline startup, MarketSituation, and MarketReaction participation were therefore not observed and were not claimed.

See [ADR-005](./ADR-005-market-data-provider-criteria.md), [ADR-007](./ADR-007-prior-session-context.md), and the [A.2.5 product direction](../../PRODUCT_ROADMAP.md#a25-live-prior-session-historical-context).
