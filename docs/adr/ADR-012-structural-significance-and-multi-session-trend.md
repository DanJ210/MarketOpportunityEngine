# ADR-012: Structural significance and multi-session trend context

- **Status:** Accepted
- **Decision:** Add bounded, provider-independent `multi-session-structure-v1` contracts and deterministic offline analysis. A.2.6 remains IN PROGRESS; Batch 1 is COMPLETE and later production reconstruction, persistence, pipeline, API, and UI batches are not yet implemented.

## Context

Single-session swing observations and immediately preceding session context describe useful intraday references, but they do not distinguish recurring multi-session structural areas or summarize the completed high/low structure of a bounded historical horizon.

## Decision

- A significant structural observation is categorically eligible; there is no weighted or 0-100 significance score.
- A complete-session high is resistance evidence and a complete-session low is support evidence. Such extrema are eligible without recurrence and are not claims of level strength, prediction, or profitability.
- A mechanically confirmed swing can qualify only when its complete-link structural area is represented in at least two distinct COMPLETE historical sessions. Multiple pivots in one session do not create distinct-session recurrence.
- Support and resistance cluster independently. Zone bounds are the exact minimum and maximum included source prices. Zones use percentage distance `abs(a - b) / min(a, b) * 100`, complete-link bounded span, and stable decimal ordering. Proximity chains such as A-B-C never merge A and C when their total span exceeds the threshold.
- A.2.6 uses exactly the three immediately preceding calendar-resolved complete regular sessions. Missing, partial, invalid, or incompatible sessions cannot be skipped. Trend requires all three contiguous sessions and is insufficient until their complete evidence is known.
- Trend compares only completed-session high/low structure. Both intervals `higher high + higher low` produce `Bullish`; `lower high + lower low` produces `Bearish`; equal high and low within tolerance produce `Range`; mixed high/low movement is `Conflicting`. Different valid interval states across the two intervals are `Transitioning`. Current indicators never affect this trend.
- Zone merging (`ZoneMergeDistancePercent`) and high/low equality (`StructuralEqualityTolerancePercent`) are separate material settings. Their `0.10%` defaults are PROVISIONAL / UNCALIBRATED development values, not market truth, predictive confidence, or profitability thresholds.
- Evidence is event-time bounded. Unconfirmed pivots, incomplete session extrema, future session evidence, and missing recurrence are unavailable before their required analytical evidence exists. Assessment, zone, and observation identities exclude wall-clock, retrieval, insertion, and UI evaluation times.
- The assessment preserves bounded representatives, exact source identities, session provenance, evidence-known time, and omitted counts. Projection limits are resource/presentation bounds rather than significance. Raw historical trades and all raw pivots are not persisted, and mutable zone rows are not introduced.
- The future persistence direction is one immutable bounded multi-session assessment per analytical scope. Batch 1 creates no SQL schema or migration.
- Structural roles do not reverse in v1. A.2.6 is parallel descriptive evidence and does not gate or change `MarketSituation`, `MarketReaction`, Opportunity, TradePlan, tracked selection, readiness, publication, or setup detection. MarketSituation v2 and structural-zone participation are explicitly deferred.

## Consequences

- Batch 1 remains offline and provider independent and can be tested without ScannerPipeline registration.
- `scanner-config-v20` carries the material A.2.6 settings while existing feature, swing, setup, TradePlan, readiness, situation, and reaction definition versions remain unchanged.
- Later batches must reconstruct the bounded historical horizon before production wiring; they must not invent continuity by skipping sessions or infer official closes from last eligible trades.
- Threshold outcome research may refine defaults only through new material configuration/version decisions.

See [ADR-007](./ADR-007-prior-session-context.md), [ADR-009](./ADR-009-market-situation-level-interaction.md), [ADR-010](./ADR-010-market-reaction-confirmation.md), [ADR-011](./ADR-011-live-prior-session-historical-source.md), and the [product roadmap](../../PRODUCT_ROADMAP.md#a26-structural-significance-and-multi-session-trend-context).
