# ADR-002: EF Core and SQL Server persistence conventions

- **Status:** Accepted
- **Decision:** Use EF Core 10 with `Microsoft.EntityFrameworkCore.SqlServer` for persistence.

## Context

The application stores an auditable record of normalized market events and analysis decisions. SQL Server is required locally and Azure SQL Database is the production target.

## Decision

Infrastructure owns the EF Core `DbContext`, entity mappings, migrations, and repository implementation. Domain and analytics contracts remain EF-free. Map tables explicitly in singular snake_case; index symbol/session/event time and opportunity/symbol/session time. Use decimal for prices, retain event and receive timestamps separately, and store feature/configuration snapshots and evidence alongside each immutable opportunity. Development creates a new schema with `EnsureCreated`; when tables already exist it applies reviewed EF migrations. The session-identity migration labels pre-existing rows `legacy-unknown` rather than inferring sessions. Production schema changes must be applied as reviewed EF migrations before deployment. SQL Server connections use transient retry support; credentials are configuration/secret-store values outside source in deployed environments.

## Consequences

- Local development uses SQL Server Developer Edition in Docker; Azure uses Azure SQL Database.
- Historical opportunities are append-only; replays may produce new analysis records but do not update old decisions.
- Bulk ingestion and specialized partitioning are deferred until measured throughput justifies them.
- The initial app bootstraps its development schema; production migration orchestration is a follow-up before deployment.
