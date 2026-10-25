# Real-Time AI Market Opportunity Engine

**Application name:** Real-Time AI Market Opportunity Engine  
**Working solution/repository name:** `MarketOpportunityEngine`

## Master Architecture and Roadmap

### Purpose
A personal-use .NET decision-support application that monitors live US stock/ETF market data, analyzes intraday conditions using transparent and testable trading rules, ranks opportunities, explains why a symbol is interesting, and records outcomes for later replay, backtesting, and machine-learning research.

The system must present setup quality and uncertainty rather than imply guaranteed profitability. Autonomous real-money execution is outside the initial delivery plan.

---

# 1. Build-Now Strategy

The complete architecture is intentionally broader than the first release. **Do not build the entire platform before producing a useful scanner.**

## Milestone A: Usable Scanner

```text
Live Market Data
      -> Normalization and Data Quality
      -> Event-Time Symbol State
      -> Multi-Horizon Features
      -> Deterministic Trading Setup Detectors
      -> Risk and Liquidity Gates
      -> Opportunity Ranking
      -> REST + SignalR as appropriate
      -> Blazor Live Dashboard
      -> Recorded Events and Outcomes
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
- **Realtime application transport:** ASP.NET Core SignalR for real-time server-to-Blazor UI updates where the selected Blazor render mode requires explicit hub-based application messaging.
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
             | \ SignalR/HTTPS
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
10. Is expected reward/risk acceptable under configured rules?
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

A level crossing alone is insufficient. Breakout quality should consider participation, momentum, relative strength, liquidity, broader context, and whether price holds or rejects the level.

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

---

# 5. Initial Setup Detectors

Each setup owns required evidence, supporting evidence, contradictions, vetoes, and thresholds. Do not average all indicators into one generic signal.

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
  + RiskRewardFit
  - ContradictionPenalty
```

Exact coefficients and thresholds must be versioned configuration, never unexplained magic constants. The 0-100 score represents rules-based setup quality, not probability of profit.

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
- Estimated reward/risk below setup requirement.
- Opportunity expired or its defining structure invalidated.

A veto should be visible in the explanation instead of silently hiding the candidate.

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

# 10. Outcome Recording

Record published, watched, expired, invalidated, and vetoed candidates so future research is not restricted to setups that appeared successful.

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

Use a modular monolith initially:

```text
Market Data Provider
       |
       v
Provider Adapter -> Normalizer -> Data Quality Gate -> Storage
                                           |
                                           v
                                  Event-Time State
                                           |
                 +-------------------------+-------------------------+
                 |                         |                         |
                 v                         v                         v
            Feature Engine            Regime Engine             Scanner
                 |                         |                         |
                 +-------------------------+-------------------------+
                                           |
                                           v
                                   Setup/Strategy Agents
                                           |
                                           v
                                      Meta Scorer
                                           |
                                           v
                                       Risk Gate
                                           |
                                           v
                               Opportunity + Explanation
                                  |                 |
                                  v                 v
                           REST / SignalR      Outcome Tracker
                                  |
                                  v
                              Web Dashboard
```

## Required stack

- .NET 10 LTS / ASP.NET Core 10 / C#; target `net10.0`.
- ASP.NET Core Blazor on .NET 10 for the frontend.
- ASP.NET Core SignalR for live UI updates.
- Microsoft SQL Server locally and Azure SQL Database in Azure.
- Entity Framework Core 10 with the Microsoft SQL Server provider for normal persistence.
- In-process `Channel<T>` for first-release event transport.
- Azure App Service hosts the ASP.NET Core 10 + Blazor Web App by default.
- Azure App Service for the ASP.NET Core backend by default.
- Azure Key Vault for deployed secrets.
- Azure Monitor + Application Insights with OpenTelemetry instrumentation.
- Bicep for infrastructure as code.
- Azure DevOps Pipelines for CI/CD by default.
- xUnit plus integration/component/replay tests.

---

# 12. Solution Structure

```text
MarketOpportunityEngine.sln

src/
  MarketOpportunity.Api/
  MarketOpportunity.Web/
  MarketOpportunity.Domain/
  MarketOpportunity.Application/
  MarketOpportunity.Contracts/
  MarketOpportunity.Infrastructure/
  MarketOpportunity.MarketData/
  MarketOpportunity.Analytics/
  MarketOpportunity.Models/
  MarketOpportunity.Replay/
  MarketOpportunity.Observability/

tools/
  MarketOpportunity.Cli/
  MarketOpportunity.ModelTrainer/
  MarketOpportunity.DataImporter/

tests/
  MarketOpportunity.UnitTests/
  MarketOpportunity.ComponentTests/
  MarketOpportunity.IntegrationTests/
  MarketOpportunity.ReplayTests/
  MarketOpportunity.ArchitectureTests/
  MarketOpportunity.UiTests/
```

Domain and analytics projects must not depend on provider SDKs, Entity Framework, HTTP, SignalR, or UI code.

---

# 13. Core Contracts

```csharp
public abstract record MarketEvent(
    string Symbol,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedTimeUtc,
    string Provider,
    string? SequenceId);

public sealed record FeatureSnapshot(
    Guid Id,
    string Symbol,
    DateTimeOffset AsOfUtc,
    string FeatureSetVersion,
    IReadOnlyDictionary<string, double> Values,
    DataQualityStatus DataQuality);

public interface IStrategyAgent
{
    string Name { get; }
    AgentAssessment Evaluate(FeatureSnapshot snapshot, MarketContext context);
}
```

Use decimal/integer types for prices and quantities in transactional contracts. Convert to floating-point only inside numerical pipelines where required.

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

Initial logical tables:

- `instrument`
- `market_session`
- `trade_event`
- `quote_event`
- `aggregate_bar`
- `data_gap`
- `feature_snapshot`
- `regime_snapshot`
- `agent_assessment`
- `opportunity`
- `opportunity_evidence`
- `opportunity_outcome`
- `paper_observation`
- `model_registry` later
- `model_evaluation` later
- `configuration_snapshot`
- `system_incident`

Indexes should prioritize symbol/event time, feature version, opportunity state/score, and outcome lookup.

---

# 16. API and Live UI

Initial REST surface:

```text
GET /api/v1/system/status
GET /api/v1/market/regime
GET /api/v1/opportunities
GET /api/v1/opportunities/{id}
GET /api/v1/symbols/{symbol}/snapshot
GET /api/v1/symbols/{symbol}/features
GET /api/v1/configuration/effective
```

SignalR events:

```text
SystemStatusChanged
MarketRegimeChanged
SymbolSnapshotChanged
OpportunityPublished
OpportunityUpdated
OpportunityExpired
DataQualityChanged
```

The dashboard should prioritize:

1. Feed health.
2. Current market regime/context.
3. Ranked opportunities.
4. Symbol detail/explanation.
5. Explicit `No qualified opportunities` state.

---

# 17. Replay and Research

Live, fixture, and historical/replay inputs must eventually feed the same normalized `MarketEvent` pipeline.

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
- SignalR contracts.
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
- SignalR failures/client count.

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

## Phase 0: Minimal foundations

- Repository/solution.
- CI and analyzers.
- Provider-independent contracts.
- SQL Server local development environment plus Azure SQL Database deployment configuration.
- `TimeProvider` and telemetry scaffolding.
- Recorded-fixture adapter.

**Exit:** fixture events normalize, persist, and preserve deterministic ordering.

## Phase 1: Market-data spine

- Initial live provider adapter.
- Reconnect/heartbeat/staleness.
- Feed-health endpoint/UI.
- Gap recording.

**Current repository note:** Fixture remains the default. Optional Alpaca and Massive trade/quote adapters are present; Alpaca is used for development stream testing, while neither adapter provides brokerage or order execution. See [ADR-005](./docs/adr/ADR-005-market-data-provider-criteria.md) for the accepted provider decision. These adapters do not satisfy the reconnect, heartbeat, or gap-recovery exit criteria below.

**Exit:** full-session soak has no unexplained data loss and recovery tests pass.

## Phase 2: State and features

- Bounded per-symbol event-time state.
- Session calendar.
- Initial trend/momentum/VWAP/volume/liquidity/context features.
- Feature-set versioning.

**Exit:** live and replay/fixture calculations match given the same input events.

## Phase 3: Milestone A opportunity engine

- Four initial setup detectors.
- Hard risk/liquidity gates.
- Setup quality score.
- Opportunity lifecycle.
- Structured explanations.

**Exit:** every published opportunity is explainable and reproducible; stale data cannot publish.

## Phase 4: Milestone A dashboard

- SignalR.
- Ranked list and detail view.
- Feed-health presentation.
- Watchlist/filters as needed.

**Exit:** a user can monitor current qualified, watch, and avoid states live and understand why.

**STOP AND EVALUATE MILESTONE A HERE.**

## Phase 5: Milestone B replay/research

- Historical replay.
- Matured outcomes.
- Transaction-cost model.
- Research reports.
- Leakage tests.

## Phase 6: Milestone C ML shadow mode

- Dataset builder.
- Baselines and candidate models.
- Walk-forward validation.
- Calibration.
- Model registry.
- Shadow inference.

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
- Use Microsoft SQL Server locally and Azure SQL Database for Azure deployment.
- Use Microsoft Azure as the cloud platform; do not substitute AWS/GCP services.
- Default to Azure App Service, Azure Key Vault, Azure Monitor/Application Insights, Bicep, and Azure DevOps Pipelines as specified in Section 2.
- Do not implement future infrastructure solely because it appears in this master document.
- No provider DTOs in domain/application projects.
- Inject `TimeProvider`.
- No future events in feature generation.
- Never overwrite historical opportunity versions.
- No random train/test split for time-series research.
- No secrets in logs.
- No live-order placement in Milestone A.
- No LLM-generated numeric scores/stops/targets/orders.
- No hidden hard-coded scoring thresholds.
- Do not claim profitability from in-sample results.
- Do not add Redis, distributed brokers, microservices, Kubernetes, Level 2, or separate ML services during Milestone A without a measured requirement.

---

# 24. First Coding-Agent Prompt

```text
You are beginning Milestone A of the Real-Time AI Market Opportunity Engine (working solution name: MarketOpportunityEngine).

Read this specification completely. The immediate objective is the smallest useful end-to-end scanner, not the entire future platform.

Build a .NET modular monolith progressing vertically through:

recorded fixture initially, then live provider
-> normalized MarketEvents
-> bounded per-symbol event-time state
-> initial trend/momentum/VWAP/volume/liquidity/context features
-> one setup detector first
-> risk/liquidity vetoes
-> opportunity result
-> REST/SignalR as appropriate
-> Blazor dashboard
-> persistence

Use a configurable small development universe, approximately 20 liquid symbols, not a hard-coded limit.

Start with a recorded-fixture source before live-provider integration. Implement one setup detector end to end before creating all four. The initial detector should expose its evidence, contradictions, quality score, vetoes, AsOfUtc, ValidUntilUtc, feature version, setup version, and configuration version.

Use .NET 10 LTS, target `net10.0`, use current supported .NET 10 servicing patches, ASP.NET Core Blazor on .NET 10, EF Core 10, and the Microsoft SQL Server provider. Add nullable reference types, analyzers, tests, a Microsoft SQL Server local-development setup and Azure SQL Database deployment configuration, health checks, and lightweight OpenTelemetry.

Do NOT add Redis, distributed messaging, microservices, Kubernetes, machine learning, Level 2 processing, broker integration, autonomous trading, or LLM decision logic.

Write only ADRs needed for immediate decisions:
1. Blazor Web App render mode (Interactive Server, WebAssembly, or Auto) and component-structure conventions; Blazor itself is fixed.
2. EF Core 10 / SQL Server persistence conventions (technology choice is fixed).
3. In-process event transport choice.
4. Raw/normalized retention format.
5. Initial market-data provider selection criteria.

Keep domain and analytics independent of provider SDKs, EF Core, HTTP, SignalR, and UI dependencies.

At each checkpoint return:
- repository tree,
- build/test/run commands,
- test results,
- ADRs created,
- what works visibly end to end,
- known limitations,
- next smallest vertical slice,
- confirmation no future-only infrastructure was introduced.
```

---

# 25. Final Recommendation

Build a trustworthy, deterministic trading-analysis baseline before adding ML. The highest-value foundations are accurate market data, event-time correctness, transparent setup rules, hard risk/liquidity gates, complete outcome recording, and deterministic replayability. Once Milestone A is producing and recording real observations, Milestone B can determine which rules have evidence behind them, and only then should Milestone C attempt to improve ranking with machine learning.
