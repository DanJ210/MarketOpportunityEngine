using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261101120000_AddTrackedOpportunityReadiness")]
public sealed class AddTrackedOpportunityReadiness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.tracked_opportunity', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tracked_opportunity
                (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_tracked_opportunity PRIMARY KEY,
                    PlanId uniqueidentifier NOT NULL,
                    OpportunityId uniqueidentifier NOT NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    Direction nvarchar(8) NOT NULL,
                    SelectedAtUtc datetimeoffset NOT NULL,
                    PlanAsOfUtc datetimeoffset NOT NULL,
                    SelectionDefinitionVersion nvarchar(64) NOT NULL,
                    Status nvarchar(32) NOT NULL,
                    SelectionReasonCode nvarchar(128) NOT NULL,
                    SelectionReason nvarchar(1024) NOT NULL,
                    HasEstablishedEntryReady bit NOT NULL,
                    TerminalAtUtc datetimeoffset NULL,
                    TerminalReasonCode nvarchar(128) NULL,
                    TerminalReason nvarchar(1024) NULL
                );
            END;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.tracked_opportunity')
                  AND name = N'IX_tracked_opportunity_Symbol_SessionId_Status'
            )
                CREATE INDEX IX_tracked_opportunity_Symbol_SessionId_Status
                    ON dbo.tracked_opportunity (Symbol, SessionId, Status);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.tracked_opportunity')
                  AND name = N'IX_tracked_opportunity_Symbol_SessionId_PlanAsOfUtc'
            )
                CREATE INDEX IX_tracked_opportunity_Symbol_SessionId_PlanAsOfUtc
                    ON dbo.tracked_opportunity (Symbol, SessionId, PlanAsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.tracked_opportunity')
                  AND name = N'IX_tracked_opportunity_PlanId'
            )
                CREATE INDEX IX_tracked_opportunity_PlanId
                    ON dbo.tracked_opportunity (PlanId);
            IF OBJECT_ID(N'dbo.pre_trade_readiness_assessment', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.pre_trade_readiness_assessment
                (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_pre_trade_readiness_assessment PRIMARY KEY,
                    TrackedOpportunityId uniqueidentifier NOT NULL,
                    PlanId uniqueidentifier NOT NULL,
                    OpportunityId uniqueidentifier NOT NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    AsOfUtc datetimeoffset NOT NULL,
                    EvaluatedAtUtc datetimeoffset NOT NULL,
                    State nvarchar(32) NOT NULL,
                    Reason nvarchar(64) NOT NULL,
                    ContextMode nvarchar(32) NOT NULL,
                    ReadinessDefinitionVersion nvarchar(64) NOT NULL,
                    AssessmentJson nvarchar(max) NOT NULL
                );
            END;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.pre_trade_readiness_assessment')
                  AND name = N'IX_pre_trade_readiness_assessment_Symbol_SessionId_AsOfUtc'
            )
                CREATE INDEX IX_pre_trade_readiness_assessment_Symbol_SessionId_AsOfUtc
                    ON dbo.pre_trade_readiness_assessment (Symbol, SessionId, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.pre_trade_readiness_assessment')
                  AND name = N'IX_pre_trade_readiness_assessment_TrackedOpportunityId'
            )
                CREATE INDEX IX_pre_trade_readiness_assessment_TrackedOpportunityId
                    ON dbo.pre_trade_readiness_assessment (TrackedOpportunityId);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.pre_trade_readiness_assessment')
                  AND name = N'IX_pre_trade_readiness_assessment_PlanId'
            )
                CREATE INDEX IX_pre_trade_readiness_assessment_PlanId
                    ON dbo.pre_trade_readiness_assessment (PlanId);
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.pre_trade_readiness_assessment', N'U') IS NOT NULL
                DROP TABLE dbo.pre_trade_readiness_assessment;
            IF OBJECT_ID(N'dbo.tracked_opportunity', N'U') IS NOT NULL
                DROP TABLE dbo.tracked_opportunity;
            """);
}
