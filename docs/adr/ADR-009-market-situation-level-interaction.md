# ADR-009: Descriptive structural level interaction

- **Status:** Accepted
- **Decision:** Add a versioned, descriptive `MarketSituation` assessment for price interaction with eligible structural support and resistance. It is separate from setup detection, TradePlan, publication health, tracked-candidate lifecycle, and pre-trade readiness.

## Context

A.2.4a needs to explain where the current reference price is relative to structural levels without implying a trade, a confirmed reaction, or a likely outcome. Incomplete current or prior-session data must not be presented as complete levels. Each assessment must remain reproducible and auditable without rewriting earlier decisions.

## Decision

- `market-situation-v1` evaluates the feature snapshot at `AsOfUtc`; only matching-symbol, matching-session events at or before that time contribute. Events are identity-deduplicated and deterministically ordered.
- Eligible current-session levels are confirmed current swing highs/lows from the retained trade history, and are included only when market structure, price data, and retained session data are complete. Eligible prior-session levels are confirmed prior swings and prior-session high/low from a usable, complete, gap-free prior context with full session coverage, trade capability, and matching symbol/session provenance. Partial, unavailable, invalid, or gapped context contributes no levels.
- Support and resistance are selected independently by absolute distance from the reference price. Ties prefer prior confirmed swings, current confirmed swings, then prior-session extremes, followed by deterministic level ID. The assessment preserves all eligible level provenance.
- A level's test-zone half-width is `level price * TestZonePercent / 100`; the band is inclusive. Outside the band, distance to the nearest band edge divided by that edge, as a percentage, is compared inclusively with `ApproachDistancePercent`. This labels an approach from the expected side and a crossed level from the opposite side. Current configuration requires `0 < TestZonePercent < 100` and `ApproachDistancePercent > TestZonePercent`.
- Both support and resistance interactions may be reported at once. The primary situation is the active interaction nearest its zone edge; equal distances prefer support. A crossed interaction is relevant only within the configured distance from the zone edge.
- Shipped defaults `TestZonePercent = 0.10` and `ApproachDistancePercent = 0.25` are provisional development values, not market-calibrated. They are part of `scanner-config-v18`; changing them requires a new scanner configuration version.
- The result labels only `ApproachingSupport`, `TestingSupport`, `CrossedSupport`, `ApproachingResistance`, `TestingResistance`, or `CrossedResistance`. It does not label a hold, bounce, reversal, breakdown, rejection, breakout, or failed breakout, and it does not change setup, TradePlan geometry, publication, tracked-candidate state, or readiness.
- Assessment identity is deterministic from material analytical inputs and excludes `EvaluatedAtUtc`. SQL history in `market_situation_assessment` is immutable: identical replay is accepted, while conflicting material content for an existing identity fails. The API and dashboard expose the current assessment without turning it into an execution instruction.

## Consequences

- The assessment is evaluated and persisted independently of tracked readiness; it does not require an active tracked candidate.
- Complete prior context may enrich the assessment; absent or incomplete context remains distinguishable and current-session analysis may continue as `CurrentSessionOnly`.
- The thresholds have no profitability or predictive claim and require later calibration evidence before any such interpretation.
- Reaction classification requires a separately approved checkpoint with explicit event-time confirmation and data-completeness rules.

See [ADR-007](./ADR-007-prior-session-context.md) for prior-context completeness and [ADR-008](./ADR-008-tracked-opportunity-readiness.md) for the separate readiness contract.
