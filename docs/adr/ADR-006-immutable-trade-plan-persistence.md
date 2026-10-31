# ADR-006: Immutable candidate trade-plan persistence

- **Status:** Accepted
- **Decision:** Persist each deterministic candidate `TradePlan` as a separate immutable SQL Server entity keyed by `PlanId`.

## Context

A.2.2 introduces structural entry zones, numeric invalidation, targets, and risk/reward as analytical geometry. Future outcome research must be able to identify the exact plan that existed at a decision time, independently of later analyses and publication-health changes.

## Decision

Persist each plan in its own `trade_plan` row. Use the deterministic `PlanId` as the primary key and store indexed symbol, explicit session, and as-of fields alongside the opportunity reference, plan-definition version, and complete serialized plan. Replays with the same ID and content are idempotent; a collision with different content fails rather than rewriting the record. A changed input produces a new ID and a new row.

## Consequences

- Plan geometry and its evidence remain available for future labeling without implementing outcome tracking now.
- Queries can locate plans by symbol, session, and analysis time without indexing UI-specific fields.
- The plan JSON preserves the complete provider provenance and decimal values; publication health remains an in-memory application projection and is not written into the plan.
- SQL Server schema changes use an explicit EF Core migration.
