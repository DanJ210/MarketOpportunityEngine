# ADR-002: EF Core and SQL Server persistence conventions

- **Status:** Accepted
- **Decision:** Use EF Core 10 with `Microsoft.EntityFrameworkCore.SqlServer` for persistence.

## Context

The application stores normalized market events, immutable feature snapshots, opportunities and evidence, configuration snapshots, and immutable candidate TradePlans. SQL Server is required locally and Azure SQL Database is the production target.

## Decision

Infrastructure owns the EF Core `DbContext`, entity mappings, hand-written migrations, and repository implementation. Domain and analytics contracts remain EF-free. Map tables explicitly in singular snake_case; index symbol/session/event time and opportunity/symbol/session time. Use decimal for prices, retain event and receive timestamps separately, and persist feature snapshots, opportunity evidence, configuration snapshots, and TradePlans as separate records. Development creates a clean schema with `EnsureCreated`; when an existing schema is detected it applies the available reviewed migrations. The session-identity migration labels pre-existing rows `legacy-unknown` rather than inferring sessions.

The current convention deliberately has no EF model snapshot or initial migration. Existing schemas use reviewed, hand-written, idempotent migrations. Generating an EF migration without a deliberate strategy review may attempt to describe the entire model; do not silently change this convention. The TradePlan migration is explicit and idempotent. Production schema-management and migration-generation strategy remain to be reviewed before deployment. SQL Server connections use transient retry support; credentials are configuration/secret-store values outside source in deployed environments.

## Consequences

- Local development uses SQL Server Developer Edition in Docker; Azure uses Azure SQL Database.
- Events, feature snapshots, opportunities/evidence, configuration snapshots, and TradePlans are persisted separately; historical decisions are immutable.
- Bulk ingestion and specialized partitioning are deferred until measured throughput justifies them.
- The app bootstraps a clean development schema and migrates an existing one; production migration orchestration is a follow-up before deployment.
