# MarketOpportunityEngine Copilot Instructions

Treat [the master specification](../Market_Opportunity_Engine_Final_Master_Spec.md) as the product and architecture source of truth. Before each Milestone A slice, read [README](../README.md) and [PROJECT_STATUS](../PROJECT_STATUS.md), and inspect the current implementation. Use the status document as current-state and validation evidence, not as authorization to expand scope; do not mistake roadmap items for permission to build them now.

## Scope and technology

- Deliver Milestone A in small, tested vertical slices. The app defaults to a recorded-fixture scanner with one Momentum Continuation setup and supports optional read-only Alpaca and Massive market-data adapters; extend it incrementally rather than building the future platform.
- Use .NET 10 (`net10.0`), ASP.NET Core 10, C#, Blazor, EF Core 10, and Microsoft SQL Server locally / Azure SQL Database in Azure. Keep the app a modular monolith.
- Do not introduce a different frontend, database, cloud, or hosting platform. Do not add microservices, Kubernetes, Redis, distributed brokers, ML, Level 2 data, broker integration, automated trading, or LLM-generated decisions.
- Do not connect a live market-data provider until explicitly requested and provider selection criteria are reviewed. Keep provider DTOs in infrastructure adapters behind provider-independent interfaces.
- Fixture remains the offline default. Alpaca is an approved market-data alternative; neither provider selection implies broker or order permissions. Follow [ADR-005](../docs/adr/ADR-005-market-data-provider-criteria.md) before changing provider behavior.
- For provider-specific WebSocket protocol details and current adapter limitations, consult the [Alpaca reference](../docs/providers/alpaca-websocket.md) or [Massive reference](../docs/providers/massive-websocket.md) and their linked official documentation.

## Architecture and correctness

- Keep Domain and Analytics independent of EF Core, SQL Server, HTTP, SignalR, Blazor, Azure SDKs, and vendor SDKs. Respect existing project references and place persistence and provider adapters in Infrastructure.
- Use canonical market-event contracts with symbol, explicit session identity where applicable, event time, receive time, provider, stable event identity, and trade/quote fields. Use decimal/integer types for prices and quantities.
- Analytical results must depend only on events at or before `AsOfUtc`. Keep event time distinct from receive time; order late events deterministically, handle duplicate identities idempotently, and keep symbol/session state isolated and bounded.
- Use `TimeProvider` for application/system time. Never use wall-clock time to determine analytical values; derive features from market event time.
- Make data-quality and risk vetoes explicit in structured results and the UI. Never silently publish through a veto or hide why a candidate was rejected.
- Keep analytical opportunities immutable. The application publication lifecycle separately suspends live candidates for Connecting, Stale, Offline, or Error source health and reports expiration/reasons; do not describe that in-memory projection as persisted lifecycle history.
- Keep thresholds and scoring weights in validated, versioned configuration. Persist configuration, feature/setup versions, evidence, contradictions, vetoes, invalidation, and immutable opportunity decisions. A 0–100 setup score is not a probability of profit.
- Do not allow later observations to rewrite decision-time records. Identical event input and versions should replay deterministically; changed analysis should create a new version/record.

## Implementation and validation

- Inspect existing patterns and tests before editing. Make focused changes, preserve unrelated work, and update directly affected documentation.
- For analytical slices, update affected test fixtures when snapshot or configuration shapes change, including positional constructors and identity/serialization assertions. Derive expected values independently from the canonical ordered eligible inputs, and keep test helper symbol/session/configuration identities aligned with the scenario.
- Add or update unit tests for analytical behavior, event ordering, duplicates, bounded state, vetoes, and future-event exclusion. Test setup logic without a web server, database, network, or Azure; run focused replay tests before broader validation.
- Use SQL Server for persistence integration tests; do not substitute an in-memory or other database provider for SQL behavior. Follow the README's guarded SQL test procedure and the current validation evidence in PROJECT_STATUS: confirm the configured connection targets a dedicated test database before running SQL tests. If setup or connection validation fails before tests execute, report SQL coverage as not run/unavailable, not as a test pass or test failure.
- Run builds, tests, and app-host verification sequentially rather than concurrently. If output files are locked, identify the owning process and stop only processes started for the current verification before retrying; do not overwrite, kill, or clean up unrelated processes.
- Run the smallest relevant tests, then build the solution. For end-to-end changes, verify the running ASP.NET Core/Blazor app and its persisted results, not just compilation.
- Use the documented commands in [README](../README.md). Never commit secrets or expose provider credentials to browser code; use local-only development configuration and Azure Key Vault for deployed secrets.
- Do not claim a strategy is profitable or present a quality score as a calibrated probability. No real-money order placement is in scope.

## Documentation decisions

Keep architecture decisions in concise ADRs under [`docs/adr`](../docs/adr/). Follow the existing decisions for Blazor Interactive Server rendering, EF Core/SQL Server, in-process transport, event retention, and provider-selection criteria; update an ADR when an implementation changes its decision.
