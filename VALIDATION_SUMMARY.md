# Validation Summary

This document records concise final evidence for completed milestone-level capabilities. It is not a chronological log. Detailed attempts, failures, troubleshooting, SQL/browser provenance, and superseded checkpoints remain in [docs/archive/VALIDATION_HISTORY.md](./docs/archive/VALIDATION_HISTORY.md). Current status and blockers remain authoritative in [PROJECT_STATUS.md](./PROJECT_STATUS.md).

## A.1 Live Scanner Validation

- **Status:** COMPLETE.
- **Capability validated:** Bounded live Alpaca IEX ingestion through canonicalization, event-time analysis, persistence, live UI observation, deterministic data flow, and graceful shutdown.
- **Final evidence:** The self-describing 30-minute run completed normally with exit code 0, processed 209,616 canonical events and 2,433 snapshots/evaluations, acknowledged all accepted events, drained with zero remaining work, and produced no application Error/Critical log entries. SQL rows matched the exact session. A later same-page 65-second browser observation plus extended monitoring showed continuing Live health and changing market values with no reconnect/error UI or navigation during the run; post-shutdown disconnect behavior was correctly identified as shutdown-related.
- **Important limitation:** Alpaca IEX was limited coverage, one provider run is not exchange-wide proof, unresolved gap reconciliation/backfill was not implemented, and the retained clock evidence did not establish an authoritative provider-to-application UTC bound.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#consolidated-a1-validation-record-2026-10-29) and [same-page closeout](./docs/archive/VALIDATION_HISTORY.md#final-same-page-browser-observation-2026-10-30).

## A.2.1 Market Structure Foundation

- **Status:** COMPLETE.
- **Capability validated:** Scanner-owned provider-neutral universe and confirmed trade-point swing/market-structure features using eligible event-time observations only.
- **Final evidence:** Focused structure tests **13/13**, complete offline units **262/262**, hosted fixture/API/dashboard **2/2**, offline launcher **17/17**, and build **0 warnings/errors**. Replay and provider-option coverage passed **169/169**.
- **Important limitation:** No SQL schema change was required; SQL and live-provider validation were not run at this checkpoint. Structure is descriptive and incomplete retained data yields insufficient structure.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#milestone-a21---provider-neutral-universe-and-market-structure-2026-10-30).

## A.2.2 Trade Plan Foundation

- **Status:** COMPLETE.
- **Capability validated:** Immutable deterministic `trade-plan-v1` geometry, SQL persistence, replay idempotency, collision rejection, API exposure, and Blazor presentation.
- **Final evidence:** Final SQL persistence/migrations **6/6**, same guarded database hosted fixture test **1/1 twice without reset**, full integrations **13/13**, offline units **279/279**, and build **0 warnings/errors**. Clean and legacy schema paths, decimal geometry, full round-trip fields, and immutable replay were verified.
- **Important limitation:** Reward/risk remains descriptive geometry; no minimum veto, execution readiness, position state, or trade instruction was introduced.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#final-a22-six-stage-sql-validation-2026-10-30).

## A.2.3a Prior-Session Market Context

- **Status:** COMPLETE.
- **Capability validated:** Separate versioned `prior-session-context-v1`, explicit fixture calendar/session provenance, prior-session-only pivot confirmation, honest partial/unavailable completeness, and all eligible confirmed candidate selection.
- **Final evidence:** Corrected focused prior-context tests **11/11**, full offline units **290/290**, hosted fixture/API/dashboard **2/2**, and build **0 warnings/errors**. The correction proved nearest support/resistance selection uses all confirmed pivots rather than truncating to latest/previous.
- **Important limitation:** Fixture coverage remained partial and could not establish complete official high/low/close; A.2.3a introduced no live historical source or SQL persistence.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#milestone-a23a-correction---prior-context-candidate-truncation-2026-10-31).

## A.2.3b Tracked Opportunity + Pre-Trade Readiness

- **Status:** COMPLETE.
- **Capability validated:** One immutable tracked `PlanId` per symbol/session, controlled lifecycle, restart restoration, and versioned `pre-trade-readiness-v2` assessment/history without moving pinned geometry.
- **Final evidence:** Initial guarded SQL persistence/migrations **9/9**, focused tracked/readiness/restart **3/3**, same-database hosted fixture **1/1 twice**, and full integrations **16/16**. PR #2 final guarded revalidation passed tracked/readiness SQL **3/3**, persistence **7/7**, migration **2/2**, hosted fixture **1/1 twice**, full integrations **16/16**, units **361/361**, and build **0 warnings/errors**.
- **Important limitation:** Readiness describes current market conditions only, reports `CurrentSessionOnly`, and is not an execution instruction. The application does not know holdings, and position/account management is not implemented.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#milestone-a23b---sql-validation-closeout-2026-11-01) and [PR #2 correction revalidation](./docs/archive/VALIDATION_HISTORY.md#pr-2---guarded-sql-correction-revalidation-2026-11-05).

## A.2.4a Structural Level Interaction

- **Status:** COMPLETE.
- **Capability validated:** `market-situation-v1` independently describes approaching, testing, and crossed support/resistance, including simultaneous sides, deterministic provenance, API/UI projection, and immutable SQL history.
- **Final evidence:** Evaluator **18/18**, expanded hosted API/dashboard **3/3**, SQL persistence/migrations **10/10**, same-database hosted SQL **1/1 twice**, full offline units **379/379**, and build **0 warnings/errors**. The guarded full integration run passed **17/17** before a supplemental hosted-only test; that added hosted suite passed **3/3**.
- **Important limitation:** Thresholds are provisional and not market calibrated. MarketSituation is descriptive and does not establish a hold, rejection, breakdown, breakout, failed breakout, or any setup/readiness change.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#a24a---structural-level-interaction-2026-11-05).

## A.2.4b Structural Level Reaction Classification

- **Status:** COMPLETE.
- **Capability validated:** Event-time `market-reaction-v1` episodes for the six approved structural reactions, pinned interaction geometry, conservative `ContinuityUnknown` restart handling, immutable assessments, non-gating research context, SQL persistence, API, and Blazor projection.
- **Final evidence:** Evaluator **16/16**, persistence/continuity/research SQL **10/10 twice**, tracker **3/3**, hosted structure/reaction **7/7**, SQL persistence **8/8**, migrations **2/2**, same-database hosted fixture **1/1 twice**, full guarded integrations **40/40**, units **398/398**, and build **0 warnings/errors**.
- **Important limitation:** Reactions remain descriptive/non-gating, are not setup detectors or trading instructions, use provisional uncalibrated thresholds, and do not imply outcome, probability, or profitability.
- [Detailed archived evidence](./docs/archive/VALIDATION_HISTORY.md#a24b-final-closeout-2026-11-10).

## A.2.5 Live Prior-Session Historical Context

- **Status:** COMPLETE; Batches 1-4 COMPLETE.
- **Capability validated:** Provider-independent calendar/history contracts, bounded Alpaca historical SIP pagination and normalization, strict completeness/provenance reconstruction, terminal-before-first-analysis warm-up, single-flight scope coordination, `CurrentSessionOnly` fallback, and frozen MarketSituation/MarketReaction provenance integration without changing swing/setup/plan/readiness behavior.
- **Final regression evidence:** Calendar/history **12/12**, reconstruction **22/22**, existing prior-context **11/11**, warm-up/single-flight **13/13**, MarketSituation **18/18**, MarketReaction **19/19**, readiness **71/71**, pipeline/publication **31/31**, TradePlan/Momentum **39/39**, fixture/replay **149/149**, full units **452/452**, hosted non-SQL integration **31/31**, launcher interception **18/18**, and build **0 warnings/errors**. SQL was not required because no SQL model, migration, store, or persistence behavior changed.
- **Provider/live evidence:** Bounded AAPL validation used provider **Alpaca**, historical feed **SIP**, and source **Historical REST**. The production calendar resolved the 2026-11-24 regular session `14:30Z`-`21:00Z`; pagination exhausted normally at **73 pages / 722,167 normalized records**, with **0 duplicates**, exact inclusive coverage, no known gaps, and final `CompleteUsable` / `Available`. Reconstructed high/low were **359.9797 / 303.42**, and the last eligible regular-session trade was **354.238** at **20:59:59.9936641Z** under last-eligible-trade semantics, not an official-close claim.
- **Retention/provenance evidence:** Raw historical trades were neither persisted nor exposed. SIP historical provenance remained distinct from configured live IEX provenance.
- **Important limitation:** The 120-second hosted IEX AAPL run authenticated after hours and received no market events, so naturally occurring regular-session warm-up, live `PriorSessionEnhanced` MarketSituation participation, and prior-level MarketReaction participation were not observed and were not claimed. Historical-source candidate counts, where needed for provenance, are trade-point micro-pivot candidates and are not ranked structural zones.
- [Detailed archived final closeout](./docs/archive/VALIDATION_HISTORY.md#a25-final-closeout-2026-11-26).
