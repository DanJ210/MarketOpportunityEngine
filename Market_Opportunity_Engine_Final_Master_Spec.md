# Real-Time AI Market Opportunity Engine

**Application name:** Real-Time AI Market Opportunity Engine  
**Working solution/repository name:** `MarketOpportunityEngine`

## Master Architecture and Roadmap

### Purpose
A personal-use .NET decision-support application that analyzes US stock/ETF market data with transparent, testable rules; explains candidate setups and their structural trade geometry; and records immutable decisions for later research. The current product defaults to recorded fixtures and supports optional read-only Alpaca and Massive market-data adapters. Fixture, provider, and broker permissions are distinct.

The system must present setup quality and uncertainty rather than imply guaranteed profitability. It does not place orders or know a user's holdings. Autonomous real-money execution is outside the initial delivery plan.

---

# 1. Build-Now Strategy

The complete architecture is intentionally broader than the first release. **Do not build the entire platform before producing a useful scanner.**

## Milestone A: Usable Scanner

The current vertical slice is a recorded-fixture scanner with one Momentum Continuation detector. The architecture supports optional read-only live market-data adapters behind provider-independent contracts. Milestone A work proceeds incrementally; future roadmap entries do not authorize implementing them ahead of an approved checkpoint.

```text
Fixture or optional live adapter
  -> canonical events / bounded in-process live channel
  -> event-time state and versioned features
  -> implemented setup detector
  -> immutable opportunity and candidate TradePlan
  -> current publication-health projection
  -> API and Blazor Interactive Server dashboard
```

Start with a configurable small universe, approximately 20 liquid symbols for development. This is not a permanent limit.

The initial product does not require machine learning. It should be useful as a transparent rules-based intraday analyst while simultaneously creating trustworthy data for later research.

## Milestone B: Research Platform

```text
Recorded/Historical Data -> Deterministic Replay -> Backtesting -> Outcomes -> Reports
```

## Milestone C: AI Enhancement

```text
Versioned Dataset -> ML Training -> Calibration -> Shadow Mode -> Ranking Enhancement
```

ML must compete against the deterministic Milestone A baseline before influencing production rankings.

## Milestone D: Optional Trading Integration

```text
Paper Broker -> Manual Approval -> Order State -> Reconciliation -> Risk Controls
```

---

# 2. Required Microsoft/Azure Technology Baseline

These are fixed architectural decisions for this application. A coding agent must not substitute competing products without an explicit specification change.

- **Backend/runtime:** .NET 10 LTS and ASP.NET Core 10, using C#. Target `net10.0`. Keep the deployed runtime on a supported .NET 10 servicing patch.
- **Frontend:** ASP.NET Core Blazor on .NET 10. Blazor is the required frontend framework; do not substitute Vue, React, or Angular.
- **Database engine:** Microsoft SQL Server. Local development uses SQL Server Developer Edition or the appropriate Microsoft SQL development container/image. Azure deployment uses Azure SQL Database unless a SQL Server-specific capability later requires another Azure SQL option.
- **Data access:** Entity Framework Core 10 for application persistence, with Microsoft SQL Server provider; use carefully reviewed SQL where a demonstrated high-throughput market-data path requires it.
- **Realtime application transport:** Blazor Interactive Server circuits for the current UI. Do not add a separate application SignalR hub without an approved requirement.
- **Cloud:** Microsoft Azure is the required cloud platform.
- **Backend hosting:** Azure App Service is the default Milestone A production hosting target for the ASP.NET Core API/background application. Revisit Azure Container Apps only if measured runtime/scaling requirements justify it.
- **Frontend hosting:** Host the Blazor Web App with the ASP.NET Core application on Azure App Service by default. Do not introduce a separate frontend hosting service unless a later hosting decision requires it.
- **Managed database:** Azure SQL Database.
- **Secrets:** Azure Key Vault for deployed secrets. Never expose market-data credentials to browser/client code.
- **Identity:** Microsoft Entra ID when deployed authentication is introduced. Keep authentication replaceable enough for simple local development.
- **Observability:** Azure Monitor and Application Insights using OpenTelemetry-compatible .NET instrumentation.
- **Infrastructure as code:** Bicep for Azure resources.
- **CI/CD:** Azure DevOps Pipelines is the preferred Microsoft-first implementation. Repository/build decisions must be captured in an ADR.

The market-data vendor is an external dependency because live exchange data does not originate from Azure. Isolate it behind `IMarketDataSource`; all application hosting, persistence, secrets, identity, monitoring, and deployment infrastructure should remain Microsoft/Azure products unless an explicit documented exception is approved.

## 2.1 Required deployment topology

```text
External Market Data WebSocket/API
             |
             v
Azure App Service
  ASP.NET Core 10 API + hosted workers
             |\
             | \ Blazor circuit / HTTPS
             |  \
             v   v
 Azure SQL     Blazor Web App
 Database       on Azure App Service
      
Azure Key Vault -> application secrets
Azure Monitor / Application Insights -> telemetry
```

Milestone A remains a modular monolith. Azure does not imply microservices, Kubernetes, distributed messaging, Redis, or other infrastructure unless measurements justify them later.

---

# 3. YAGNI Rules for Milestone A

- Keep the application a modular monolith; do not introduce future platform components solely because they appear in this roadmap.
- Fixture remains the offline default. Do not connect a provider unless explicitly requested and its provider criteria have been reviewed.
- Do not add broker/order integration, position tracking, outcome labeling, backtesting, ML, ONNX inference, or extra setup detectors before their approved milestones.
- Keep current publication health separate from immutable analysis and plan records.
---

# 4. Milestone A Trading Analysis Specification

Milestone A is a deterministic intraday analysis engine. For every eligible symbol it classifies the current setup as:

- **Probable Long**
- **Probable Short**
- **Watch**
- **Avoid / No Trade**

`Probable` is a qualitative rules-based label in Milestone A. It must not be displayed as a statistical probability until later research demonstrates and calibrates a probability model.

## 4.1 Questions the scanner answers continuously

1. Is market data current and trustworthy?
2. Is the symbol sufficiently liquid?
3. What is the intraday trend?
4. Is momentum strengthening or deteriorating?
5. Is volume confirming the move relative to normal activity for the same time of day?
6. Is the stock outperforming or underperforming the broad market and relevant sector?
7. How is price behaving around VWAP and important intraday levels?
8. Does the state match a supported trading setup?
9. Is there a defensible invalidation condition?
10. What descriptive risk/share, reward/share, and reward/risk geometry does a candidate structural plan have?
11. Is there sufficient agreement to publish, or should the engine abstain?

## 4.2 Trend engine

Inputs should initially include:

- Price relative to session VWAP.
- VWAP slope.
- Fast/medium EMA relationship.
- EMA slopes.
- Higher-high/higher-low or lower-high/lower-low structure.
- Position within the session range.
- ADX or another versioned trend-strength measure.

Output:

```text
Direction: Bullish | Bearish | Neutral | Conflicting
Quality: 0..100
Evidence: [...]
Contradictions: [...]
```

## 4.3 Momentum engine

Inputs:

- Rate of change over multiple short horizons.
- RSI as context, never as a standalone buy/sell trigger.
- MACD direction/histogram as context.
- EMA velocity.
- Short-horizon acceleration/deceleration.

Correlated indicators must not be counted as independent confirmation simply because multiple formulas describe the same price behavior.

## 4.4 Volume and participation

Analyze:

- Rolling volume.
- Session cumulative volume.
- Volume acceleration.
- Price/volume confirmation and divergence.
- Relative volume normalized to the same point in the trading session when historical reference data is available.

For example, a 10:17 AM observation must not be compared naively with the volume of an entire historical trading day.

## 4.5 Relative strength

Compare each symbol with configurable context instruments:

- Broad-market benchmark such as SPY.
- QQQ where appropriate.
- Relevant sector ETF/benchmark.

Measure multi-horizon relative returns and directional divergence. This is market-relative strength, not the RSI indicator.

## 4.6 VWAP behavior

Classify:

- Above/below VWAP.
- VWAP slope.
- VWAP reclaim.
- VWAP rejection.
- Pullback/hold.
- Volatility-normalized distance from VWAP.

Price above/below VWAP alone must never create an opportunity.

## 4.7 Structure and breakout analysis

Track:

- Session high/low.
- Opening range when enabled.
- Recent swing highs/lows.
- Consolidation boundaries.
- Prior-session reference levels when historical data is available.

**A.2.3a Prior-Session Market Context defines the only approved prior-session input to A.2.3b pre-trade readiness.** When readiness uses prior-session levels, it must consume an explicit, versioned context for the immediately preceding eligible session; it must not select a session by subtracting a calendar day. The context must preserve provider/feed provenance, structural-observation identity, and explicit completeness. Prior-session swings reuse the versioned two-left/two-right trade-point algorithm and cannot be confirmed by later-session events. Partial, unavailable, or invalid history must remain distinguishable, must not be presented as complete prior-session levels, and must not be used as confirmed historical support or resistance. If no approved historical source or authoritative calendar is configured, readiness must treat prior context as unavailable rather than infer it. Readiness may fall back to explicitly labeled current-session-only analysis when the current-session setup, TradePlan, publication health, and data-quality requirements remain satisfied, and must expose the context mode it used: `PriorSessionEnhanced` when complete prior-session context participated, or `CurrentSessionOnly` when readiness was derived without usable prior-session context (see [ADR-007](./docs/adr/ADR-007-prior-session-context.md)).

A level crossing alone is insufficient for breakout-quality analysis. Participation, momentum, relative strength, liquidity, broader context, and whether price holds or rejects the level may be relevant to separate setup-quality work; these inputs do not define A.2.4b `market-reaction-v1` confirmation.

### A.2.4a MarketSituation

`market-situation-v1` is a separate descriptive assessment of price interaction with eligible structural levels. It may independently report approaching, testing, or crossed support and resistance, including both sides at once. Current-session levels require confirmed swings and complete retained current-session data; prior-session swings and extrema require complete, usable, gap-free prior context and full coverage. The assessment preserves level provenance, classifies a test zone centered on the level, and measures approach/crossed relevance from the zone edge using versioned provisional thresholds. It does not classify reactions or change opportunity, TradePlan, publication, tracking, or readiness. See [ADR-009](./docs/adr/ADR-009-market-situation-level-interaction.md).

### A.2.4b Structural Level Reaction Classification

**A.2.4b status: COMPLETE. Batches 1-4 are COMPLETE.** Reaction episodes/assessments persist with conservative live restart continuity; the event-driven tracker is wired into the production pipeline and `ScannerState`, with current-projection API and Blazor surfaces. A separate immutable, event-time-bounded research context is persisted without participating in confirmation. A.2.4b describes what happens after an A.2.4a structural-level interaction. It is separate from `MarketSituation` (current geometric relationship to a level), setup detection, `TradePlan` geometry, tracked-candidate identity, and `PreTradeReadiness` (`pre-trade-readiness-v2`). A reaction is not an entry-readiness gate, a trade instruction, a new setup detector, or a probability/profitability claim. Final closeout evidence is recorded in [VALIDATION_HISTORY.md](./VALIDATION_HISTORY.md#a24b-final-closeout-2026-11-10). See [ADR-010](./docs/adr/ADR-010-market-reaction-confirmation.md).

The approved v1 reaction vocabulary is:

- **Support:** `SupportHeld`, `SupportBreakdown`, `SupportReclaimed`.
- **Resistance:** `ResistanceRejected`, `ResistanceBreakout`, `ResistanceFailedBreakout`.
- An interaction may also remain unresolved until event-time expiration.

An episode starts from an A.2.4a interaction and pins the original `LevelId` and test-zone geometry. `TestingSupport` or `CrossedSupport` may start a support episode; `TestingResistance` or `CrossedResistance` may start a resistance episode. The initiating observation cannot confirm its own reaction. Confirmation requires a later eligible canonical market observation, deterministic canonical ordering and event time, and price displacement beyond the appropriate pinned test-zone edge. A tiny zone crossing alone remains an A.2.4a `Crossed` observation, not a confirmed breakdown or breakout. The first confirmed terminal reaction wins; immutable confirmed history is not rewritten by later price action, although a later interaction may start a new episode. Unresolved episodes expire by event-time duration. Restart/continuity handling must conservatively preserve what is known and never fabricate observations from the scanner-offline interval.

The only approved v1 reaction-policy configuration values are `Scanner:MarketReaction:ConfirmationDistancePercent` and `Scanner:MarketReaction:MaxEpisodeDurationSeconds`. The initial defaults are `0.10` percent and `300` seconds; both are **PROVISIONAL / NOT MARKET CALIBRATED**. Confirmation is not based on volume, Momentum, VWAP, EMA, ADX, trend direction, Opportunity classification, TradePlan, PreTradeReadiness, or reward/risk; such values may later be retained as research context only.

Episodes are scoped by exact symbol/session. Support and resistance interactions can have simultaneous episodes; per-level latches avoid repeated starts until an interaction leaves the eligible start situations. Only event batches processed by analysis advance reactions; health/publication ticks do not. In live mode, restore runs before the first observation for each symbol/session, and unresolved restored episodes become terminal `ContinuityUnknown` before new prices can be applied. `Confirmed`, `Expired`, and `ContinuityUnknown` are terminal. `level_interaction_episode` stores the actual mutable Batch 1 episode lifecycle with immutable start/geometry fields, and `market_reaction_assessment` stores deterministic immutable assessment history. The hand-written idempotent SQL migration is `20261106120000_AddLevelInteractionReactionPersistence`.

The separately versioned `market-reaction-research-context-v1` captures feature identity and event-time market context for each assessment, including market-situation status/context mode, data completeness, momentum/volume/VWAP/spread/trend, opportunity classification/score, and feed metadata. It is persisted immutably in one-to-one `market_reaction_research_context` by `20261106150000_AddMarketReactionResearchContext`. This context is not passed to the reaction evaluator and cannot create, advance, confirm, or veto an episode. `GET /api/v1/reactions` and opportunity responses expose current scanner projections; historical SQL data is not queried by the endpoint. The dashboard displays the pinned interaction and descriptive context with a non-gating disclaimer. `market-reaction-v1`, Momentum, and PreTradeReadiness behavior remain unchanged. The Batch 1 records do not define separate `EvaluatedAtUtc` or evidence-collection fields.

Approved implementation batches:

1. **Batch 1 - Domain + Reaction Algorithm (COMPLETE):** reaction/episode domain contracts; `market-reaction-v1`; the two policy values and scanner configuration version advancement; episode lifecycle/evaluator; six mechanical reaction algorithms; offline deterministic tests/regressions.
2. **Batch 2 - Restart/Continuity Safety + SQL Persistence/Migration (COMPLETE):** terminal restart continuity handling; explicit-session unresolved restore/read path; mutable episode and immutable assessment persistence; guarded SQL and migration tests; ADR-010.
3. **Batch 3 - Pipeline Integration + Research Context + API + Blazor (COMPLETE):** event-driven runtime tracking, conservative live restart wiring, immutable non-gating research context, current reaction API projections, dashboard display, and hosted/integration coverage.
4. **Batch 4 - Final Documentation and Complete Regression/SQL/Checkpoint Closeout (COMPLETE).**

All four A.2.4b batches are complete. Completing this checkpoint does not authorize role reversal, confluence scoring, ATR or relative-volume confirmation, new momentum thresholds or setup detectors, a reaction requirement for `EntryReady`, Target 2, hard reward/risk vetoes, position/account management, broker execution, outcome labeling, backtesting, ML/ONNX, LLM trading decisions, provider changes, or historical-provider integration.

## 4.8 Volatility and risk

Inputs:

- ATR or equivalent versioned volatility estimate.
- Short-horizon realized volatility.
- Range expansion/contraction.
- Distance to invalidation.
- Potential target distance.

Elevated or poorly bounded risk can veto an otherwise attractive setup.

## 4.9 Liquidity and execution quality

Inputs:

- Bid/ask spread in price and basis points.
- Quote freshness.
- Dollar volume.
- Trade/quote activity where available.
- Quote-size information where available.

Stale data or unacceptable liquidity is a hard veto according to versioned configuration.

## 4.10 Market, sector, and time-of-day context

Evaluate broad-market direction, sector direction, symbol alignment/divergence, and the freshness of context data.

Time of day should influence expectations for normal volume, volatility, and activity. It must not become an arbitrary clock-based ban unless later research supports such a rule.

## 4.11 Market-structure foundation

The current structure foundation identifies confirmed trade-point swing highs and lows using event-time-ordered eligible trades. It can expose latest/previous swings, their high/low relationships, a combined descriptive structure state, and support/resistance candidates with provider-independent source-swing provenance. These are provider-independent analytical observations, not guaranteed reversal levels or trading instructions. Structure is versioned and may be insufficient when retained evidence is incomplete.

## 4.12 Candidate TradePlan

A candidate TradePlan answers: **“If this setup were traded, what is its structural geometry?”** It is a separate immutable analytical contract, not an instruction, execution readiness, or position state. It contains:

- Direction and plan status.
- Entry zone and explicit entry reference.
- Numeric invalidation price.
- Structural targets and source provenance.
- Decimal risk/share, reward/share, and reward/risk.
- Deterministic, versioned identity and the source opportunity/feature/configuration provenance.
- SQL Server persistence keyed by immutable `PlanId`.

The current `trade-plan-v1` derives these values from confirmed structure. `PlanDefined` means the geometry is calculable; it does not mean “entry ready.” No synthetic levels or minimum reward/risk cutoff may be invented.

---

# 5. Initial Setup Detectors

Each setup owns required evidence, supporting evidence, contradictions, vetoes, and thresholds. Do not average all indicators into one generic signal.

Momentum Continuation is the only implemented detector. The other detectors below remain intended future scope and must not be described as complete.

## 5.1 Momentum continuation

Potential evidence:

- Established intraday trend.
- Momentum aligned with trend.
- VWAP behavior aligned.
- Participation/relative volume supportive.
- Relative strength aligned.
- Acceptable liquidity, volatility, and reward/risk.

## 5.2 Confirmed breakout/breakdown

Potential evidence:

- Identified structural level.
- Price moves through the level.
- Participation confirms.
- Momentum and relative strength support direction.
- Spread/liquidity remains acceptable.
- Defensible invalidation condition exists.

## 5.3 VWAP reclaim/rejection

Bullish reclaim candidate:

- Price was below VWAP.
- Price reclaims it.
- Reclaim satisfies configured confirmation requirements.
- Momentum/participation improves.
- Market context does not strongly contradict the move.

Invert the relevant logic for bearish rejection.

## 5.4 Failed breakout/reversal warning

Potential evidence:

- Initial break of an important level.
- Failure to hold it.
- Deteriorating momentum/participation.
- Move back through the level.
- Context invalidates the original directional setup or supports reversal.

This detector may emit `Avoid Long`, `Avoid Short`, or a reversal candidate if all additional qualifications are met.

---

# 6. Scoring Philosophy

Use setup-specific qualification:

```text
Feature Evidence
       |
       v
Setup Detector
  + Required conditions
  + Supporting evidence
  + Contradictions
  + Hard vetoes
       |
       v
Setup Quality 0-100
       |
       v
Cross-Setup Opportunity Ranking
```

Conceptually:

```text
SetupQuality =
    TrendFit
  + MomentumFit
  + ParticipationFit
  + RelativeStrengthFit
  + ContextFit
  + StructureFit
  + LiquidityFit
  - ContradictionPenalty
```

Exact coefficients and thresholds must be versioned configuration, never unexplained magic constants. The 0-100 score represents rules-based setup quality, not probability of profit. Reward/risk is currently descriptive TradePlan geometry; a hard reward/risk veto is deferred until a separate threshold/calibration decision is approved. Do not invent a minimum.

---

# 7. Hard Vetoes

Do not publish when an applicable veto is active:

- Feed stale/offline or recovering from unresolved gaps.
- Symbol quote stale.
- Spread above configured maximum.
- Liquidity/dollar volume below configured minimum.
- Invalid price/quote state.
- Required benchmark data unavailable or stale.
- Volatility/risk outside supported bounds.
- No defensible invalidation condition.
- Opportunity expired or its defining structure invalidated.

A veto should be visible in the explanation instead of silently hiding the candidate.

Current gates are those implemented and versioned for the active setup; this list does not authorize implementation of missing gates. In particular, reward/risk is not currently a hard veto.

---

# 8. Opportunity Lifecycle

```text
Detected -> Watch -> Published -> Updated -> Expired/Invalidated
```

Every opportunity should contain:

- `AsOfUtc`
- `ValidUntilUtc`
- setup-definition version
- feature-set version
- configuration version
- reference market state

Material changes create a new version rather than rewriting historical reasoning.

Keep these concepts distinct:

- **Market situation:** What is happening around an eligible structural level right now? A.2.4a implements a descriptive, versioned assessment; it remains distinct from setup, plan, tracking, readiness, and publication state.
- **Analytical setup:** What pattern or condition exists?
- **TradePlan:** If the setup were traded, what is its structural geometry?
- **Publication:** Is current source health sufficient to present this result? Publication may suspend or expire a candidate without mutating its immutable analysis or plan.
- **Tracked opportunity:** Which immutable TradePlan is currently being followed as the candidate for a symbol/session?
- **Pre-trade readiness:** Where is that tracked candidate relative to current market conditions?

These are separate contracts: Market Situation ≠ Opportunity ≠ TradePlan ≠ Publication Health ≠ Tracked Opportunity ≠ Pre-Trade Readiness. Do not collapse them into one enum or mutate TradePlan to express any of them.

A.2.3b implements the pre-trade readiness states `Unavailable`, `Suspended`, `Watching`, `SetupForming`, `EntryZoneActive`, `EntryReady`, and `NoLongerActionable` (`pre-trade-readiness-v2`) over a pinned tracked candidate (`tracked-opportunity-selection-v1`). Readiness is a deterministic current-state evaluation, not a linear workflow; active states may be skipped, while terminal assessments and terminal lifecycle fields remain final. A newer recalculated plan never moves a tracked candidate's geometry; terminal conditions end the candidate and a subsequent evaluation may pin the newest eligible plan. v2 checks quote freshness at evaluation time without rewriting analytical snapshots and always reports `CurrentSessionOnly` because no historical levels participate in its rules. Prior context remains separately available. Live startup and health ticks persist lifecycle reconciliation independently of analysis batches; expiry uses the pinned validity boundary and observed session changes use strictly later event time, not inferred calendar boundaries. Lifecycle-only updates do not manufacture readiness-history observations. `EntryReady` means candidate entry conditions are aligned; it must never be worded as "buy now", "sell now", "short now", or a guaranteed or probable outcome. The application does not need to know whether the user owns a position to describe potential market entry/opportunity conditions; readiness describes market conditions, never holdings. Position/account management is a separate future concern if approved; it is not a prerequisite for market-opportunity analysis.

A.2.4a **Structural Level Interaction** implements `MarketSituation` with the descriptive labels approaching support, testing support, crossed support, approaching resistance, testing resistance, and crossed resistance. It reports both support and resistance independently where relevant and preserves eligible-level provenance. It must not claim that support will hold, a bounce is confirmed, a breakdown is confirmed, resistance rejection is confirmed, or a breakout is confirmed. `MarketSituation` does not alter setup, TradePlan geometry, publication, tracking, or readiness.

Future detector work follows `Detector -> TradePlan -> Readiness layer -> Outcome`. These are separate contracts and milestones; a detector does not itself produce a user action or outcome.

---

# 9. Explanation Contract

Every displayed opportunity must explain:

- Setup detected.
- Why it matters now.
- Supporting evidence.
- Contradictory evidence.
- Market/sector context.
- Invalidation condition.
- Risk/liquidity/data-quality concerns.
- Expiration.
- Rule and configuration versions.

Illustrative UI:

```text
NVDA                    PROBABLE LONG
Setup: Momentum Continuation
Quality: 87 / 100

Supporting
+ Intraday trend aligned
+ Above rising VWAP
+ Relative participation elevated
+ Outperforming configured benchmark
+ Liquidity within limits

Contradictions
! Short-horizon volatility elevated

Invalidation: derived structure/price condition
Status: Current
```

This example is illustrative and is not a current recommendation.

---

# 10. Outcome Recording (Future Research Milestone)

Outcome tracking is deferred until stable immutable TradePlans and a separately approved research/data-retention policy exist. Future outcomes must link to the exact immutable `PlanId`, so later labels cannot rewrite the plan or imply that current outcomes already exist.

At decision time persist:

- Symbol/timestamps.
- Setup type/version.
- Classification and quality score.
- Feature snapshot/version.
- Component scores.
- Evidence/contradictions.
- Vetoes.
- Market/sector context.
- Reference price, spread, liquidity state.
- Invalidation/target structure when present.
- Configuration version.

After an observation matures derive separately:

- Forward returns at configured horizons.
- Maximum favorable excursion.
- Maximum adverse excursion.
- Target versus invalidation first-touch result.
- Time to target/invalidation.
- Outcome after configured transaction-cost assumptions.

Never rewrite the original decision-time record with later information.

---

# 11. Architecture

Use a modular monolith. Current analysis and plan geometry remain deterministic and separate from publication health, future user action state, and future outcome research:

```text
Fixture (default) or optional read-only market-data provider
       |
       v
Provider Adapter -> Canonical Events -> Data Quality Gate -> Storage
                                           |
                                           v
                                  Event-Time State
                                           |
                                  Versioned Features
                                           |
                                           v
                              Implemented Setup Detector
                                           |
                            Immutable Opportunity + TradePlan
                                           |
                             Publication-health projection
                                           |
                        Tracked candidate + readiness assessment
                                  |                 |
                                  v                 v
                              REST API      Blazor Interactive Server

The A.2.3b readiness layer consumes immutable analytical records and persists tracked associations and immutable readiness history; outcome/research layers remain future scope.
```

## Required stack

- .NET 10 LTS / ASP.NET Core 10 / C#; target `net10.0`.
- ASP.NET Core Blazor on .NET 10 for the frontend.
- Blazor Interactive Server; the current UI uses its server circuits, not a separate application SignalR hub.
- Microsoft SQL Server locally and Azure SQL Database in Azure.
- Entity Framework Core 10 with the Microsoft SQL Server provider for normal persistence.
- Bounded in-process `Channel<MarketEvent>` for live ingestion, with backpressure, bounded graceful drain, and explicit accounting; fixture processing remains in-process.
- Azure App Service hosts the ASP.NET Core 10 + Blazor Web App by default.
- Azure App Service for the ASP.NET Core backend by default.
- Azure Key Vault for deployed secrets.
- Azure Monitor + Application Insights with OpenTelemetry instrumentation.
- Bicep for infrastructure as code.
- Azure DevOps Pipelines for CI/CD by default.
- xUnit plus integration/component/replay tests.

---

# 12. Current Solution Structure

```text
MarketOpportunityEngine.sln

src/
  MarketOpportunity.Web/             # Blazor UI and HTTP API
  MarketOpportunity.Domain/
  MarketOpportunity.Application/
  MarketOpportunity.Analytics/
  MarketOpportunity.Infrastructure/  # EF Core and provider adapters

tests/
  MarketOpportunity.UnitTests/
  MarketOpportunity.IntegrationTests/
  MarketOpportunity.AlpacaConnectivityTests/  # separate, opt-in network tests
```

This is the current modular-monolith project layout. Do not split out API, contracts, providers, replay, model training, or observability projects without an approved architectural need. Domain and analytics must not depend on provider SDKs, Entity Framework, HTTP, SignalR, or UI code.

---

# 13. Core Contracts

Canonical market events carry symbol, explicit session identity, event and receive timestamps, provider, stable event identity, and decimal/integer trade or quote values. Provider DTOs remain in infrastructure adapters. Features and analytical decisions are versioned and evaluated only from eligible events at or before `AsOfUtc`.

`TradePlan` is an immutable provider-independent contract with deterministic `PlanId`, direction/status, entry zone/reference, numeric invalidation, structural targets, decimal risk/reward geometry, source opportunity, versions, provenance, and evidence. Publication health does not belong in this historical geometry.

---

# 14. Event-Time and Data Quality Rules

- UTC internally; local/ET conversion belongs in the presentation/session-calendar layer.
- Track event time and receive time separately.
- Use an exchange calendar including holidays and early closes.
- Record duplicate, late, corrected, and dropped events.
- Maintain per-symbol watermarks.
- Pause publication on stale or unresolved-gapped data.
- Reconcile/backfill after reconnect where provider capabilities permit.
- UI must clearly show `Live`, `Delayed`, `Recovering`, `Stale`, or `Offline`.
- Inject `.NET TimeProvider`; do not call wall-clock time directly in analytical code.

---

# 15. Storage Model

Current persisted records include normalized market events, feature snapshots, opportunities/evidence, configuration snapshots, and immutable TradePlans. `trade_plan` uses `PlanId` as its key and indexes symbol/session/as-of plus opportunity identity. Identical replay is idempotent; different content under the same ID fails. Outcomes, position state, model registry, and research tables are future scope, not current schema.

Schema management is deliberately mixed: a clean development database may be created with `EnsureCreated`; existing schemas use reviewed hand-written/idempotent migrations. There is no EF model snapshot or initial migration. Review future migration generation strategy deliberately rather than silently switching conventions.

---

# 16. API and Live UI

Current REST surface:

```text
GET /api/v1/system/status
GET /api/v1/opportunities
GET /api/v1/opportunities/{id}
GET /api/v1/symbols/{symbol}/snapshot
GET /api/v1/configuration/effective
```

Opportunity list/detail responses include the opportunity, current publication decision, and candidate TradePlan. Market structure is exposed in the versioned feature snapshot. `GET /api/v1/system/diagnostics` exists only in Development when opt-in validation diagnostics are enabled. The current Blazor UI updates through Interactive Server state notifications; there is no separate application SignalR hub.

The dashboard should prioritize:

1. Feed health and source provenance.
2. Current setup analyses and publication health.
3. Candidate TradePlan geometry and evidence.
4. Symbol detail/explanation.
5. Explicit `No qualified opportunities` state.

---

# 17. Replay and Research

Fixture and optional live inputs use the same normalized `MarketEvent` pipeline. Historical reads, deterministic replay tooling, and backtesting remain future research scope; persisted events are not yet a general historical read path.

Replay requirements:

- Deterministic clock.
- Original/accelerated/step modes later.
- Same feature/setup/risk code as live operation.
- Failure injection later for gaps, duplicate events, disconnects, and late events.

Anti-leakage rules:

- No event after `AsOfUtc` may affect a feature snapshot.
- Outcome labels are unavailable until matured.
- Session VWAP/high/low only use data known at `AsOfUtc`.
- Chronological research splits only.

---

# 18. Machine Learning, Later

Machine learning remains deferred until sufficient outcome-labeled data exists and the research/data-rights policy is approved. Intended architecture:

```text
deterministic features + structure + immutable TradePlan
                     + historical outcomes linked by PlanId
                         -> trained model
                         -> ONNX inference in .NET where appropriate
```

ML may augment or rank deterministic plans. It must not silently invent price levels, rewrite plan geometry, or bypass hard data-quality/risk constraints. Training, ONNX, model inference, and probability calibration are not current capabilities.

Milestone C may ask:

> Given only information available at `AsOfUtc`, what is the calibrated probability that a volatility-normalized target is reached before a volatility-normalized stop before expiry?

Suggested progression:

1. Deterministic Milestone A baseline.
2. Logistic regression baseline.
3. Gradient-boosted classifier.
4. Ranking model after sufficient cross-sectional observations.
5. Regime-specific models only when evidence justifies them.

Use chronological/walk-forward testing. A displayed numeric probability must be calibrated rather than treating an arbitrary model score as probability.

An LLM, if added, may turn structured analysis into natural-language explanation. It must not generate or alter numerical signals, targets, stops, risk decisions, or orders.

---

# 19. Testing Strategy

## Unit

- Indicators and features.
- Event ordering/lateness.
- VWAP, returns, volatility.
- Setup requirements.
- Hard vetoes.
- Opportunity expiry.

## Property/invariant

- No NaN/infinity escapes the feature engine.
- Scores remain in documented ranges.
- Duplicate event identity does not alter state.
- No future event influences a snapshot.

## Integration

- SQL Server/EF Core migrations.
- Provider fixtures/connectors.
- API and Blazor Interactive Server circuit behavior.
- Event-to-opportunity vertical slice.

## Golden replay

Store small recorded/sanitized fixtures with expected feature and opportunity snapshots. Changes to expected analytical output require an intentional setup/feature version change.

---

# 20. Observability

Track at minimum:

- Events received/normalized/dropped/duplicated.
- Arrival lag and watermark age.
- Reconnect count.
- In-process channel depth/backpressure.
- Feature and scoring duration.
- End-to-end opportunity latency.
- Opportunities published, expired, and vetoed.
- Database latency.
- Interactive Server circuit/reconnection failures.

Trace provider reception -> normalization -> state -> features -> setup detector -> scoring -> risk -> persistence -> UI publication.

---

# 21. Security and Safety

- Never expose provider/broker secrets to the browser.
- Redact credentials/account identifiers from logs.
- HTTPS and appropriate authentication/authorization when hosted.
- Version/audit configuration and model changes.
- Rate-limit expensive research endpoints later.
- Provide `DisablePublishing` operational switch.
- A future broker module requires an independent `DisableOrders` kill switch and server-side risk controls.
- No live trading endpoint in Milestone A.

---

# 22. Delivery Roadmap

## Current Milestone A checkpoint

- **A.1 Live Scanner Validation:** COMPLETE.
- **A.2.1 Market Structure Foundation:** COMPLETE.
- **A.2.2 Trade Plan Foundation:** COMPLETE.
- **A.2.3a Prior-Session Market Context:** COMPLETE.
- **A.2.3b Tracked Opportunity + Pre-Trade Readiness:** COMPLETE, including PR #2 correction guarded SQL revalidation.
- **A.2.4a Structural Level Interaction:** COMPLETE.
- **A.2.4b Structural Level Reaction Classification:** COMPLETE; Batches 1-4 COMPLETE.

A.2.3b defines the user-facing pre-trade readiness contract over current setup, pinned TradePlan, publication, and data-quality evidence. Readiness stays separate from setup, immutable TradePlan geometry, and current publication health, and does not require knowing the user's holdings. Position/account management is a separate future concern if approved and is not required for market-opportunity analysis.

A.2.4a adds the versioned `market-situation-v1` descriptive assessment and immutable SQL history. Its default test-zone half-width is 0.10% of level price and its default approach/crossed relevance is 0.25% of zone-edge price; both are provisional and not market calibrated. See [ADR-009](./docs/adr/ADR-009-market-situation-level-interaction.md).

**Next checkpoint:** REQUIRES DESIGN / APPROVAL. No subsequent product checkpoint is authorized by this closeout. A.2.4b's approved v1 vocabulary, confirmation principles, configuration boundary, architectural boundary, exclusions, and four-batch limits remain recorded in [A.2.4b Structural Level Reaction Classification](#a24b-structural-level-reaction-classification).

## Phase 0: Minimal foundations

- Repository/solution.
- CI and analyzers.
- Provider-independent contracts.
- SQL Server local development environment plus Azure SQL Database deployment configuration.
- `TimeProvider` and telemetry scaffolding.
- Recorded-fixture adapter.

**Exit:** fixture events normalize, persist, and preserve deterministic ordering.

## Phase 1: Market-data spine

- Provider-independent normalized ingestion and basic feed-health presentation.

**Current repository note:** Fixture remains the default. Optional Alpaca and Massive adapters are present; Alpaca live evaluation has occurred. Alpaca has bounded reconnect behavior, but no historical gap backfill is performed; Massive does not yet reconnect. Neither adapter provides brokerage or order execution. See [ADR-005](./docs/adr/ADR-005-market-data-provider-criteria.md).

**Exit:** full-session soak has no unexplained data loss and recovery tests pass.

## Phase 2: State and features

- Bounded per-symbol event-time state.
- Session calendar.
- Initial trend/momentum/VWAP/volume/liquidity/context features.
- Feature-set versioning.

**Exit:** live and replay/fixture calculations match given the same input events.

## Phase 3: Milestone A opportunity engine

- Add the remaining approved setup detectors incrementally; Momentum Continuation is currently implemented.
- Versioned risk/liquidity gates.
- Setup quality, opportunity lifecycle, and structured explanations.
- Market structure and immutable candidate TradePlans.

**Exit:** every published opportunity is explainable and reproducible; stale data cannot publish.

## Phase 4: Milestone A dashboard

- Blazor Interactive Server dashboard and current publication-health projection; no separate SignalR hub.
- Ranked list and detail view.
- Feed-health presentation.
- Watchlist/filters as needed.

**Exit:** a user can monitor current qualified, watch, and avoid states live and understand why.

**STOP AND EVALUATE MILESTONE A HERE.**

## Phase 5: Milestone B replay/research

- Historical replay.
- Matured outcomes linked to immutable `PlanId`.
- Transaction-cost model.
- Research reports.
- Leakage tests.
- Explicit provider-data licensing, normalized/derived retention, permitted research/backtesting and ML use, provenance, and deletion/expiration policy.

## Phase 6: Milestone C ML shadow mode

- Dataset builder.
- Baselines and candidate models.
- Walk-forward validation.
- Calibration.
- Model registry.
- Shadow inference, with ONNX in .NET where appropriate.
- ML may augment/rank deterministic plans only; it cannot invent plan price levels or bypass hard vetoes.

## Phase 7: Extended validation

- Paper-observation ledger.
- Simulated fills/positions when useful.
- Drift and calibration monitoring.
- Session reports.

## Phase 8: Optional Milestone D broker paper integration

- Broker abstraction/paper adapter.
- Manual approval.
- Idempotent order IDs.
- Reconciliation.
- Duplicate prevention.
- Kill switch.

No autonomous real-money execution is authorized by this phase.

---

# 23. Coding-Agent Non-Negotiables

- Work in small vertical slices.
- Use .NET 10 (`net10.0`) and ASP.NET Core 10 for the backend.
- Use ASP.NET Core Blazor on .NET 10 for the frontend.
- Keep the recorded fixture as the default; do not connect a live provider without an explicit request and provider-criteria review.
- Use Microsoft SQL Server locally and Azure SQL Database for Azure deployment.
- Use Microsoft Azure as the cloud platform; do not substitute AWS/GCP services.
- Default to Azure App Service, Azure Key Vault, Azure Monitor/Application Insights, Bicep, and Azure DevOps Pipelines as specified in Section 2.
- Do not implement future infrastructure solely because it appears in this master document.
- No provider DTOs in domain/application projects.
- Inject `TimeProvider`.
- No future events in feature generation.
- Never overwrite historical opportunity versions.
- Keep setup, immutable TradePlan, publication health, tracked opportunity, pre-trade readiness, position-management state, and outcomes conceptually and technically separate.
- Position-management states require supplied position context; do not infer holdings or add broker integration to obtain them.
- Treat TradePlan reward/risk as descriptive until a separately approved threshold decision; do not invent a minimum or hard veto.
- Link future outcomes to immutable `PlanId`; do not implement outcome tracking before the research milestone and retention decision.
- No random train/test split for time-series research.
- No secrets in logs.
- No live-order placement in Milestone A.
- No LLM-generated numeric scores/stops/targets/orders.
- No hidden hard-coded scoring thresholds.
- Do not claim profitability from in-sample results.
- Do not add Redis, distributed brokers, microservices, Kubernetes, Level 2, or separate ML services during Milestone A without a measured requirement.

---

# 24. Checkpoint Guidance

Deliver Milestone A in small tested vertical slices. Before each slice, read the current project status and README and inspect the implementation. Status is evidence of current state, not permission to expand scope; this specification is the product/architecture roadmap.

---

# 25. Final Recommendation

Build a trustworthy, deterministic trading-analysis baseline before adding ML. The highest-value foundations are accurate market data, event-time correctness, transparent setup rules, hard risk/liquidity gates, immutable structural plans, and later deterministic replay/outcome research. Preserve the user's position-management boundary and verify data-use rights before sustained historical research or model training. Only after outcome-labeled data exists should Milestone C assess whether ML improves deterministic ranking.
