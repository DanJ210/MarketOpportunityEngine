# Current Project Status

**Current checkpoint:** Milestone A.2.4a - Structural Level Interaction (NOT STARTED)
**Updated:** 2026-11-02

## Milestones

- **A.1 Live Scanner Validation:** COMPLETE
- **A.2.1 Market Structure Foundation:** COMPLETE
- **A.2.2 Trade Plan Foundation:** COMPLETE
- **A.2.3a Prior-Session Market Context:** COMPLETE
- **A.2.3b Tracked Opportunity + Pre-Trade Readiness:** COMPLETE

## Current state

The app defaults to the recorded fixture and supports optional read-only Alpaca and Massive market-data adapters. A.2.1 added versioned confirmed swing/structure values, A.2.2 immutable candidate TradePlans, and A.2.3a the separate `prior-session-context-v1` contract.

A.2.3b adds the tracked-candidate and readiness layer. `tracked-opportunity-selection-v1` pins one immutable `PlanDefined` TradePlan per symbol/session when the opportunity direction agrees, publication is Active, required current data is valid, and risk/reward to Target 1 are positive; newer plans never move a tracked candidate's geometry. `pre-trade-readiness-v1` evaluates the pinned geometry against market state at `AsOfUtc` into Unavailable, Suspended, Watching, SetupForming, EntryZoneActive, EntryReady, or NoLongerActionable with structured reasons, and exposes the context mode: `PriorSessionEnhanced` only when complete prior-session context participated, otherwise explicit `CurrentSessionOnly` fallback (per the amended [ADR-007](./docs/adr/ADR-007-prior-session-context.md)). EntryReady confirmation only combines existing approved outputs (direction agreement, qualifying classification, no vetoes, Active publication, valid/fresh quote, approved spread limit, price inside the pinned entry zone); no new indicator was added. The approach boundary `Scanner:PreTrade:ApproachDistanceRiskMultiple` (PROVISIONAL development default 1.0, not market calibrated) is normalized by the plan's RiskPerShare and versioned through `scanner-config-v17`.

Tracked associations/lifecycle and immutable readiness assessments persist to SQL Server via the hand-written idempotent migration `20261101120000_AddTrackedOpportunityReadiness` (`tracked_opportunity`, `pre_trade_readiness_assessment`); only documented lifecycle fields may change, terminal candidates are never replaced in place, and active candidates are restored on restart and reevaluated from current trustworthy market state. Feed/data-health problems suspend readiness in the in-memory `ScannerState` projection without terminating or mutating the tracked plan; recovery reevaluates the same `PlanId`. See [ADR-008](./docs/adr/ADR-008-tracked-opportunity-readiness.md). The dashboard adds a PRE-TRADE READINESS section with plain-language states ("Candidate entry conditions are aligned." for Entry Ready) and the opportunity API responses expose the tracked identity, readiness state/reason, context mode, entry zone, distances, invalidation, Target 1, and reward/risk. The recorded fixture yields no confirmed structure, so it produces no tracked candidates and readiness honestly reports no tracked candidate.

The scanner does not know the user's actual holdings; readiness states describe market conditions, not positions, and Entry Ready is not an instruction to trade. Position-management states such as `MANAGE`, `TAKE PROFIT`, and `EXIT` require a `PositionContext`, which is not implemented. Reward/risk is descriptive; no minimum reward/risk veto has been approved or implemented.

## Latest validation

**A.2.3b validation: COMPLETE (offline + guarded SQL).** The guarded SQL closeout ran against the dedicated `MarketOpportunityEngine_IntegrationTests` database on the existing healthy Compose SQL Server with an in-memory process-scoped connection (no credentials printed, cleared after each run). SQL persistence/migrations passed **9/9** (including the tracked/readiness round-trip, lifecycle/collision enforcement, representative readiness states and context modes with exact decimals, and clean + legacy migration paths with `tracked_opportunity` / `pre_trade_readiness_assessment` schema and index assertions), focused tracked/readiness/restart tests **3/3** (restart restored the active candidate, resolved the exact pinned immutable `PlanId` despite a newer persisted plan, and reevaluated readiness from current market state instead of restoring the historical state), and the SQL-backed hosted fixture suite **1/1 twice against the same database without reset** (asserting `scanner-config-v17` and the persisted `preTrade.approachDistanceRiskMultiple = 1.0` snapshot). Full integrations passed **16/16**, the full offline unit suite **345/345**, and the solution build succeeded with **0 warnings/errors**. Earlier offline evidence (focused readiness **55/55** and regressions) is unchanged. Two test-only corrections were made (a stale `scanner-config-v16` literal in the hosted fixture test and new SQL coverage); **no production code changed**. See the [A.2.3b SQL closeout record](./VALIDATION_HISTORY.md#milestone-a23b---sql-validation-closeout-2026-11-01).

**A.2.2 SQL validation: COMPLETE.** Final validation passed SQL persistence/migrations **6/6**, same-database hosted fixture tests **1/1 twice** without clearing records, full integrations **13/13**, offline units **279/279**, and the solution build (**0 warnings/errors**). See the [final six-stage validation evidence](./VALIDATION_HISTORY.md#final-a222-six-stage-sql-validation-2026-10-30).

## Immediate next checkpoint

**A.2.4a Structural Level Interaction (NOT STARTED).** Describe price relative to eligible structural support and resistance levels as parallel, descriptive evidence. Keep it separate from opportunities, TradePlans, and pre-trade readiness; reaction classifications remain future A.2.4b work. An observation is not a trade signal.

**Current blockers:** [The A.2.3b pull request](https://github.com/DanJ210/MarketOpportunityEngine/pull/2) has unresolved review findings involving quote freshness, terminal readiness/history, and context provenance. Resolve them before merging or beginning A.2.4a.

No live-provider validation, outcome tracking, backtesting, ML/ONNX inference, broker integration, or order execution is part of A.2.3. See the [master specification](./Market_Opportunity_Engine_Final_Master_Spec.md) for product and architecture contracts.
