# MarketOpportunityEngine Copilot Instructions

## Sources of truth

- **Implementation and tests:** what currently exists and behaves in this repository.
- [PROJECT_STATUS](../PROJECT_STATUS.md): concise current checkpoint, completed checkpoints, blockers, latest validation summary, and immediate next step.
- [Master specification](../Market_Opportunity_Engine_Final_Master_Spec.md): approved product direction, architecture contracts, milestone roadmap, and scope.
- [ADRs](../docs/adr/): durable architectural decisions and their reasoning.
- [README](../README.md): current developer/operator guide, including what the application is today, its capabilities and limitations, and how to configure, run, and test it.
- [VALIDATION_HISTORY](../VALIDATION_HISTORY.md): chronological historical evidence.

Older `VALIDATION_HISTORY.md` sections describe what was true at that checkpoint. Historical statements may be superseded; they do not override current implementation, `PROJECT_STATUS.md`, later validation evidence, or accepted ADRs.

### Resolving conflicts

When current documentation conflicts:

1. Inspect implementation, tests, schema/migrations, and git state.
2. Identify which document is stale.
3. Do not expand scope based on the contradiction.
4. Reconcile the stale current document before proceeding through a prerequisite gate.
5. Never rewrite historical validation records merely to make them match current state.
6. If implementation and approved architecture genuinely conflict, stop for architectural review instead of silently correcting either side.

`PROJECT_STATUS.md` cannot override evidence that source or tests differ from its claim. Source existence alone does not establish a checkpoint as complete; required validation must also have passed.

## Scope and technology

- Deliver Milestone A in small, tested vertical slices. Roadmap items are not authorization to implement them.
- Keep the recorded-fixture scanner as the offline default. Do not connect a live provider without an explicit request and review under [ADR-005](../docs/adr/ADR-005-market-data-provider-criteria.md). Provider selection never implies broker or order permissions.
- Use .NET 10, ASP.NET Core 10, C#, Blazor, EF Core 10, and SQL Server locally / Azure SQL Database in Azure. Keep a modular monolith.
- Do not substitute frontend, database, cloud, or hosting platforms, or add microservices, Kubernetes, Redis, distributed brokers, ML, Level 2 data, broker integration, automated trading, or LLM-generated decisions.
- Keep provider DTOs and persistence in Infrastructure; Domain and Analytics remain independent of EF Core, SQL Server, HTTP, SignalR, Blazor, Azure SDKs, and vendor SDKs.
- Keep credentials out of source, logs, and browser code. Follow README configuration precedence and guarded test-database instructions; never dump secrets while diagnosing configuration.

## Architectural and product invariants

Keep these concepts separate in this conceptual order:

`Canonical MarketEvent -> FeatureSnapshot / current-session features -> MarketStructure + PriorSessionContext -> MarketSituation -> MarketReaction -> Opportunity / analytical setup -> TradePlan -> TrackedOpportunity -> PreTradeReadiness`

This is a conceptual architecture ordering, not a causal dependency: MarketSituation and MarketReaction are parallel descriptive market evidence and do not determine or alter Opportunity/setup or PreTradeReadiness unless a future approved contract explicitly says otherwise.

- **Opportunity:** the analytical setup detected.
- **MarketSituation:** descriptive interaction with eligible support/resistance levels; it is independent of opportunity, TradePlan, publication, tracking, and readiness.
- **MarketReaction:** versioned descriptive classification of what price subsequently did after interacting with a pinned MarketSituation structural level/zone, using later eligible event-time observations; it is independent of setup detection, TradePlan geometry, and PreTradeReadiness.
- **TradePlan:** immutable hypothetical candidate geometry for a setup; persistence does not mean a trade occurred.
- **TrackedOpportunity:** the immutable `PlanId` currently followed as a candidate.
- **PreTradeReadiness:** current pre-trade market assessment against that pinned candidate.
- **PriorSessionContext:** stable previous-session contextual structure, separate from live ingestion and current-session state.
- **PublicationDecision:** a current data-health/presentation projection, separate from immutable analytical history.

`PriorSessionContext` is historical/contextual evidence consumed only where approved. Incomplete or unavailable context is never complete; current-session analysis may continue in explicitly labeled `CurrentSessionOnly` mode where allowed. `PriorSessionEnhanced` requires usable, complete prior context to actually participate. Do not invent exchange calendars, call fixture partial extrema official previous-day high/low/close, or treat persisted live events as an approved historical research source. Follow [ADR-007](../docs/adr/ADR-007-prior-session-context.md).

`MarketSituation` v1 may report approaching, testing, or crossed support and resistance from independently selected eligible levels; it can report both sides simultaneously. Current swings require complete current-session structure/data, and prior swings/extrema require complete, usable, gap-free prior context. Preserve deterministic level provenance and immutable assessment history. Its provisional thresholds are not market calibrated. Do not infer a hold, bounce, reversal, breakdown, rejection, breakout, or failed breakout, and do not use MarketSituation to mutate setup, TradePlan, publication, tracked opportunity, or readiness.

`MarketReaction` v1 is an implemented, production-wired descriptive layer that tracks event-time episodes against pinned structural levels/zones and classifies `SupportHeld`, `SupportBreakdown`, `SupportReclaimed`, `ResistanceRejected`, `ResistanceBreakout`, or `ResistanceFailedBreakout`. Batches 1-3 are COMPLETE. Batch 2 provides conservative restart continuity and SQL persistence; Batch 3 integrates the tracker with `ScannerPipeline`/`ScannerState`, persists non-gating research context, exposes the reaction API and current opportunity projections, and renders MARKET REACTION in Blazor. A.2.4b remains IN PROGRESS only because Batch 4 final regression/SQL/documentation/checkpoint closeout remains; Batch 4 is the immediate next authorized slice. MarketReaction remains separate from Opportunity, TradePlan, TrackedOpportunity, and PreTradeReadiness; it is not an `EntryReady` gate, setup detector, trading instruction, calibrated probability, or profitability claim. Follow [ADR-009](../docs/adr/ADR-009-market-situation-level-interaction.md) and [ADR-010](../docs/adr/ADR-010-market-reaction-confirmation.md) for the architectural decisions and the [master specification](../Market_Opportunity_Engine_Final_Master_Spec.md#a24b-structural-level-reaction-classification) for the approved A.2.4b scope.

Follow [ADR-006](../docs/adr/ADR-006-immutable-trade-plan-persistence.md) and [ADR-008](../docs/adr/ADR-008-tracked-opportunity-readiness.md): never move an active candidate's pinned `PlanId` or geometry to a newer plan; reevaluate readiness from current trustworthy market state. Stale/offline feed health suspends current presentation without mutating the immutable plan. `EntryReady` means candidate entry conditions are aligned, not that an order should be placed. Do not use “BUY NOW,” “SELL NOW,” or “GUARANTEED PROFIT”; a quality score is not probability of profit.

The engine analyzes market conditions and candidate opportunities; it does not need actual ownership to describe potential entry conditions, structural invalidation, or market situations in approved checkpoints. Never infer holdings, fills, execution, realized/unrealized profit, or account state. Do not introduce `PositionContext` as a prerequisite for market-opportunity analysis. Actual position/account management is a separate future concern, only if approved.

## Correctness and SQL

- Analytical results use only events at or before `AsOfUtc`; keep event and receive time distinct, order late events deterministically, deduplicate identities, and bound symbol/session state. Use decimal/integer prices and quantities.
- Use `TimeProvider` for application/system time; never use wall-clock time to determine analytical values. Make data-quality and risk vetoes explicit.
- Preserve immutable decision-time records and deterministic replay. Changed analysis creates a new version/record rather than rewriting history.
- Follow [ADR-002](../docs/adr/ADR-002-ef-sqlserver-conventions.md): SQL Server is required for persistence integration behavior. Clean development/test schemas may use documented `EnsureCreated`; existing schemas use reviewed hand-written/idempotent migrations. There is no normal EF initial migration/model snapshot convention; do not run `dotnet ef migrations add` blindly.
- SQL tests require a verified dedicated test database. Independently guard the SQL connection; never retarget tests to development or live-validation databases to make them pass.

## Validation and documentation

Inspect existing patterns and tests before editing; make focused changes and update directly affected current documentation. Use the smallest relevant tests, then build. Run tests/build/app-host verification sequentially. For analytical changes, test event ordering, duplicates, bounded state, vetoes, and future-event exclusion. For end-to-end changes, verify the hosted app and persisted results.

Before any implementation task returns a checkpoint **COMPLETE**, verify this closeout gate:

- required implementation exists and required focused tests pass;
- SQL/integration tests pass when the checkpoint changes SQL behavior;
- solution build passes;
- `PROJECT_STATUS.md` marks the checkpoint COMPLETE and names the correct next checkpoint;
- README current-milestone/capability statements agree with status and implementation;
- the master spec recognizes approved new architectural concepts, and relevant ADRs capture durable decisions;
- concise validation evidence is appended to `VALIDATION_HISTORY.md`;
- current documents have been searched for contradictory current-status claims.

If these cannot be reconciled, return **PARTIAL** or **BLOCKED**, not COMPLETE. Historical validation entries need not match current state and must remain unrewritten. Keep ADRs concise; link to the master spec, README, and ADRs rather than duplicating their detailed contents.

## Internal slice/batch closeout

When an approved checkpoint is explicitly divided into implementation batches or slices, distinguish completion of the current batch from completion of the overall checkpoint.

A batch may be marked COMPLETE when:

- every requirement explicitly assigned to that batch is implemented;
- the validation explicitly required for that batch has passed;
- work reserved for later approved batches has not been implemented;
- PROJECT_STATUS.md records:
  - the overall checkpoint as IN PROGRESS,
  - the completed batch as COMPLETE,
  - the next approved batch/slice as the immediate next task;
- README/master-spec wording does not contradict the batch boundary.

Do not keep a successfully validated batch in IMPLEMENTED or "validation pending" state merely because later batches contain SQL, integration, API, UI, or final checkpoint validation that was explicitly excluded from the completed batch.

Likewise, completion of an internal batch does not authorize marking the overall checkpoint COMPLETE.

When the current batch's required validation passes, reconcile PROJECT_STATUS.md in the same task before returning the batch closeout. Do not require a separate documentation-only reconciliation prompt for evidence already produced by that task.

Only stop for a prerequisite contradiction when repository evidence shows a genuine unresolved requirement, failed/missing validation, or scope conflict, not merely because a status label failed to advance after validation that the same task successfully completed.