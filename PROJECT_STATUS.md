# Current Project Status

**Current checkpoint:** Milestone A.2.3a - Prior-Session Market Context
**Updated:** 2026-10-31

## Milestones

- **A.1 Live Scanner Validation:** COMPLETE
- **A.2.1 Market Structure Foundation:** COMPLETE
- **A.2.2 Trade Plan Foundation:** COMPLETE
- **A.2.3a Prior-Session Market Context:** COMPLETE
- **A.2.3b Pre-Trade Readiness:** NOT STARTED

## Current state

The app defaults to the recorded fixture and supports optional read-only Alpaca and Massive market-data adapters. A.2.1 added versioned, descriptive confirmed swing/structure values. A.2.2 adds deterministic candidate TradePlans persisted as immutable SQL records, exposed through opportunity API responses and dashboard cards. Setup decisions and classifications remain unchanged; current publication health is a separate projection from historical plan geometry.

A.2.3a adds the separate `prior-session-context-v1` contract: `IHistoricalMarketDataSource` with the recorded fixture as the only implemented historical source, an explicit fixture session calendar, and prior-session pivots, reference levels, completeness, and provenance exposed at `/api/v1/symbols/{symbol}/prior-context` and in the dashboard's **PRIOR SESSION** section. Context is deterministically rebuilt from approved fixture input, cached per symbol/current session, and not persisted; live provider history and an authoritative live calendar remain unavailable/unverified, so live scanning continues with explicit unavailable context. No feature/configuration/Momentum/TradePlan version or schema change was made. See [ADR-007](./docs/adr/ADR-007-prior-session-context.md).

The scanner does not know the user's actual holdings. Plan status is not execution readiness. Position-management states such as `MANAGE`, `TAKE PROFIT`, and `EXIT` require a `PositionContext`, which is not implemented. Reward/risk is descriptive; no minimum reward/risk veto has been approved or implemented.

## Latest validation

**A.2.3a validation: COMPLETE (offline).** Focused prior-context unit tests passed **10/10**, A.2.1 market-structure regressions **13/13**, A.2.2 TradePlan regressions **17/17**, Momentum Continuation regressions **22/22**, the full offline unit suite **289/289**, and hosted fixture/API/dashboard tests **2/2** with no SQL or provider connection; the solution build succeeded with **0 warnings/errors**. SQL persistence/migration suites were **not run** (no guarded `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was configured; no schema change was made). See the [A.2.3a record](./VALIDATION_HISTORY.md#milestone-a23a---prior-session-market-context-2026-10-31).

**A.2.2 SQL validation: COMPLETE.** Final validation passed SQL persistence/migrations **6/6**, same-database hosted fixture tests **1/1 twice** without clearing records, full integrations **13/13**, offline units **279/279**, and the solution build (**0 warnings/errors**). Only the offline diagnostic test's database guard required correction; production code was unchanged. The prior closeout also passed focused regressions **52/52**. See the [final six-stage validation evidence](./VALIDATION_HISTORY.md#final-a222-six-stage-sql-validation-2026-10-30) and [earlier A.2.2 validation record](./VALIDATION_HISTORY.md#milestone-a22---sql-validation-closeout-2026-10-30). The final A.1 live browser observation is documented [here](./VALIDATION_HISTORY.md#final-same-page-browser-observation-2026-10-30).

## Immediate next checkpoint

**A.2.3b Pre-Trade Readiness.** Define and implement only pre-trade user-facing readiness/action states over current setup, plan, and publication evidence. Keep readiness separate from analytical setup, immutable TradePlan geometry, and current publication health. Do not infer holdings or implement position-management actions; require a separately approved `PositionContext` contract first. The exact remaining prerequisite is an approved historical market-data source with verified access, retention, and licensing terms, plus an authoritative session calendar for live sessions; incomplete prior context must not drive readiness.

**Current blockers:** None for completed checkpoints. A.2.3b has not started.

No live-provider validation, outcome tracking, backtesting, ML/ONNX inference, broker integration, or order execution is part of A.2.3. See the [master specification](./Market_Opportunity_Engine_Final_Master_Spec.md) for product and architecture contracts.
