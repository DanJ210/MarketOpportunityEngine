using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261023190000_AddExplicitSessionIdentity")]
public sealed class AddExplicitSessionIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_event', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.market_event', N'SessionId') IS NULL
            BEGIN
                ALTER TABLE dbo.market_event
                    ADD SessionId nvarchar(64) NOT NULL
                        CONSTRAINT DF_market_event_SessionId DEFAULT N'legacy-unknown';
            END;

            IF OBJECT_ID(N'dbo.feature_snapshot', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.feature_snapshot', N'SessionId') IS NULL
            BEGIN
                ALTER TABLE dbo.feature_snapshot
                    ADD SessionId nvarchar(64) NOT NULL
                        CONSTRAINT DF_feature_snapshot_SessionId DEFAULT N'legacy-unknown';
            END;

            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'SessionId') IS NULL
            BEGIN
                ALTER TABLE dbo.opportunity
                    ADD SessionId nvarchar(64) NOT NULL
                        CONSTRAINT DF_opportunity_SessionId DEFAULT N'legacy-unknown';
            END;

            IF OBJECT_ID(N'dbo.market_event', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.market_event')
                     AND name = N'IX_market_event_Symbol_SessionId_EventTimeUtc')
            BEGIN
                CREATE INDEX IX_market_event_Symbol_SessionId_EventTimeUtc
                    ON dbo.market_event (Symbol, SessionId, EventTimeUtc);
            END;

            IF OBJECT_ID(N'dbo.feature_snapshot', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.feature_snapshot')
                     AND name = N'IX_feature_snapshot_Symbol_SessionId_AsOfUtc')
            BEGIN
                CREATE INDEX IX_feature_snapshot_Symbol_SessionId_AsOfUtc
                    ON dbo.feature_snapshot (Symbol, SessionId, AsOfUtc);
            END;

            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.opportunity')
                     AND name = N'IX_opportunity_Symbol_SessionId_AsOfUtc')
            BEGIN
                CREATE INDEX IX_opportunity_Symbol_SessionId_AsOfUtc
                    ON dbo.opportunity (Symbol, SessionId, AsOfUtc);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the added audit dimension on rollback rather than erasing session assignments.
    }
}
