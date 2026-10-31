# ADR-006: Immutable candidate trade-plan persistence

- **Status:** Accepted
- **Decision:** Persist each deterministic candidate `TradePlan` as a separate immutable SQL Server entity keyed by `PlanId`.

## Context

A.2.2 introduces structural entry zones, numeric invalidation, targets, and risk/reward as analytical geometry. Future outcome research must be able to identify the exact candidate plan that existed at its analysis AsOfUtc, independently of later analyses and publication-health changes.

## Decision

Persist each immutable plan in its own `trade_plan` row. Its deterministic identity incorporates the source analytical identities and plan definition/content. Use `PlanId` as the primary key and store indexed symbol, explicit session, and as-of fields alongside the opportunity reference, plan-definition version, and complete serialized plan. Replays with the same ID and content are idempotent; a collision with different content fails rather than rewriting the record. A changed input produces a new ID and a new row.

## Consequences
- Plan geometry and its evidence remain available for future outcome linkage by immutable PlanId, without implementing outcome tracking now.
- A persisted TradePlan represents candidate analytical trade geometry. Its existence does not establish that the user entered, intended to enter, or held a position.
- Queries can locate plans by symbol, session, and analysis time without indexing UI-specific fields.
- The plan JSON preserves direction, entry zone/reference, numeric invalidation, structural targets, decimal risk/reward geometry, plan status, evidence, versions, and provider provenance.
- Current publication health remains a separate in-memory application projection and is not written into historical plan geometry.
- SQL Server schema changes use a reviewed, hand-written idempotent migration consistent with the repository's deliberate schema-management convention.