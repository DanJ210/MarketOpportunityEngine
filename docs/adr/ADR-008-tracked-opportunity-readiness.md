# ADR-008: Tracked-candidate and pre-trade readiness persistence and supersession

- **Status:** Accepted
- **Decision:** Pin one immutable TradePlan per symbol/session as a persisted tracked candidate with a controlled lifecycle, persist every analysis-time readiness assessment as an immutable history row, and never replace a tracked candidate in place. Feed/data-health suspension is a projection state, not a lifecycle terminal.

## Context

A.2.3b answers "what is happening with a found candidate opportunity now?". `trade-plan-v1` includes the current reference price in its entry zone, so a constantly replaced newest plan would keep price trivially inside a moving entry area. Readiness must therefore evaluate later market state against one pinned immutable `PlanId`, survive restart, and leave an auditable history for future outcome research, without mutating TradePlan or inferring the user's holdings.

## Decision

- `tracked_opportunity` persists the association/lifecycle. Selection (`tracked-opportunity-selection-v1`) pins the newest eligible `PlanDefined` plan only when the opportunity direction agrees, publication is Active, required current data is valid, risk and reward to Target 1 are positive, and no active tracked candidate exists for the symbol/session. Selection time is the analysis event time, so identical replay reproduces identical deterministic identity.
- Mutable columns are exactly `Status`, `HasEstablishedEntryReady`, `TerminalAtUtc`, `TerminalReasonCode`, and `TerminalReason`. `Status` may move from `Active` to one terminal value (`Expired`, `Invalidated`, `TargetReachedBeforeEntry`, `ExtendedBeyondEntry`, `SessionEnded`, `PlanUnavailable`) and never back; `HasEstablishedEntryReady` is monotonic. All other columns are immutable and a conflicting write fails.
- A newer `PlanId`, a changed quality score, recalculated entry geometry, or price movement never supersedes an active tracked candidate. After a terminal condition, a subsequent evaluation may select the newest eligible candidate as a new row; the terminal row is never rewritten.
- `pre_trade_readiness_assessment` persists immutable `pre-trade-readiness-v1` assessments produced at analysis time (geometry from market state at `AsOfUtc` only). Identical replay is idempotent; a different payload for an existing identity fails. `EvaluatedAtUtc` is audit timing, excluded from deterministic identity and from the material-content comparison.
- On restart, active tracked candidates are restored and their plans resolved from the immutable `trade_plan` rows; readiness is recomputed from the next trustworthy current market state. Prior readiness states are never restored blindly, and no offline market events are inferred or backfilled.
- The in-memory `ScannerState` projection re-derives publication and readiness on health changes (for example Suspended while the feed is unhealthy) without persisting those re-derivations; SQL history holds only analysis-time assessments, matching the existing publication-projection convention.

## Consequences

- Readiness history supports future outcome research (which `PlanId` was followed, which states occurred, when, and on what evidence) without any position or execution state.
- Suspension never terminates or mutates a tracked candidate; recovery reevaluates the same `PlanId`.
- The approach boundary (`Scanner:PreTrade:ApproachDistanceRiskMultiple`, provisional development default 1.0, not market calibrated) is versioned through the scanner configuration; changing it requires a new configuration version, which participates in assessment identity.
- Projection-only re-derivations (health suspensions between analyses) are intentionally absent from SQL history.
