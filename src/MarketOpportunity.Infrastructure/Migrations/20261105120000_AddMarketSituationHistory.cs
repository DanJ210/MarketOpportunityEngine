using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261105120000_AddMarketSituationHistory")]
public sealed class AddMarketSituationHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_situation_assessment', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.market_situation_assessment
                (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_market_situation_assessment PRIMARY KEY,
                    FeatureSnapshotId uniqueidentifier NOT NULL,
                    PriorSessionContextId uniqueidentifier NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    AsOfUtc datetimeoffset NOT NULL,
                    EvaluatedAtUtc datetimeoffset NOT NULL,
                    Status nvarchar(32) NOT NULL,
                    ContextMode nvarchar(32) NOT NULL,
                    PrimarySituationType nvarchar(32) NULL,
                    SupportSituationType nvarchar(32) NULL,
                    ResistanceSituationType nvarchar(32) NULL,
                    SupportLevelSource nvarchar(32) NULL,
                    ResistanceLevelSource nvarchar(32) NULL,
                    DefinitionVersion nvarchar(64) NOT NULL,
                    ConfigurationVersion nvarchar(64) NOT NULL,
                    AssessmentJson nvarchar(max) NOT NULL
                );
            END;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                  AND name = N'IX_market_situation_assessment_Symbol_SessionId_AsOfUtc'
            )
                CREATE INDEX IX_market_situation_assessment_Symbol_SessionId_AsOfUtc
                    ON dbo.market_situation_assessment (Symbol, SessionId, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                  AND name = N'IX_market_situation_assessment_SupportSituationType_SupportLevelSource_AsOfUtc'
            )
                CREATE INDEX IX_market_situation_assessment_SupportSituationType_SupportLevelSource_AsOfUtc
                    ON dbo.market_situation_assessment (SupportSituationType, SupportLevelSource, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                  AND name = N'IX_market_situation_assessment_ResistanceSituationType_ResistanceLevelSource_AsOfUtc'
            )
                CREATE INDEX IX_market_situation_assessment_ResistanceSituationType_ResistanceLevelSource_AsOfUtc
                    ON dbo.market_situation_assessment (ResistanceSituationType, ResistanceLevelSource, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                  AND name = N'IX_market_situation_assessment_PrimarySituationType_AsOfUtc'
            )
                CREATE INDEX IX_market_situation_assessment_PrimarySituationType_AsOfUtc
                    ON dbo.market_situation_assessment (PrimarySituationType, AsOfUtc);
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_situation_assessment', N'U') IS NOT NULL
                DROP TABLE dbo.market_situation_assessment;
            """);
}
