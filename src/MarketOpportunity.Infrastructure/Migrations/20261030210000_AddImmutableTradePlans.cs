using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261030210000_AddImmutableTradePlans")]
public sealed class AddImmutableTradePlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.trade_plan', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.trade_plan
                (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_trade_plan PRIMARY KEY,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    AsOfUtc datetimeoffset NOT NULL,
                    OpportunityId uniqueidentifier NOT NULL,
                    PlanDefinitionVersion nvarchar(64) NOT NULL,
                    TradePlanJson nvarchar(max) NOT NULL
                );
            END;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.trade_plan')
                  AND name = N'IX_trade_plan_Symbol_SessionId_AsOfUtc'
            )
                CREATE INDEX IX_trade_plan_Symbol_SessionId_AsOfUtc
                    ON dbo.trade_plan (Symbol, SessionId, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.trade_plan')
                  AND name = N'IX_trade_plan_OpportunityId'
            )
                CREATE INDEX IX_trade_plan_OpportunityId
                    ON dbo.trade_plan (OpportunityId);
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.trade_plan', N'U') IS NOT NULL
                DROP TABLE dbo.trade_plan;
            """);
}
