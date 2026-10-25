using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261024232400_AddOpportunityMarketDataProvenance")]
public sealed class AddOpportunityMarketDataProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'DataProvider') IS NULL
                ALTER TABLE dbo.opportunity ADD DataProvider nvarchar(64) NOT NULL
                    CONSTRAINT DF_opportunity_DataProvider DEFAULT N'Unknown';
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'DataFeed') IS NULL
                ALTER TABLE dbo.opportunity ADD DataFeed nvarchar(64) NOT NULL
                    CONSTRAINT DF_opportunity_DataFeed DEFAULT N'Unknown';
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'DataCoverage') IS NULL
                ALTER TABLE dbo.opportunity ADD DataCoverage nvarchar(32) NOT NULL
                    CONSTRAINT DF_opportunity_DataCoverage DEFAULT N'ProviderDefined';
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'IsRealTimeData') IS NULL
                ALTER TABLE dbo.opportunity ADD IsRealTimeData bit NOT NULL
                    CONSTRAINT DF_opportunity_IsRealTimeData DEFAULT 0;
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'SupportsTrades') IS NULL
                ALTER TABLE dbo.opportunity ADD SupportsTrades bit NOT NULL
                    CONSTRAINT DF_opportunity_SupportsTrades DEFAULT 0;
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.opportunity', N'SupportsQuotes') IS NULL
                ALTER TABLE dbo.opportunity ADD SupportsQuotes bit NOT NULL
                    CONSTRAINT DF_opportunity_SupportsQuotes DEFAULT 0;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.opportunity', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.DF_opportunity_DataProvider', N'D') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP CONSTRAINT DF_opportunity_DataProvider;
                IF OBJECT_ID(N'dbo.DF_opportunity_DataFeed', N'D') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP CONSTRAINT DF_opportunity_DataFeed;
                IF OBJECT_ID(N'dbo.DF_opportunity_DataCoverage', N'D') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP CONSTRAINT DF_opportunity_DataCoverage;
                IF OBJECT_ID(N'dbo.DF_opportunity_IsRealTimeData', N'D') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP CONSTRAINT DF_opportunity_IsRealTimeData;
                IF OBJECT_ID(N'dbo.DF_opportunity_SupportsTrades', N'D') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP CONSTRAINT DF_opportunity_SupportsTrades;
                IF OBJECT_ID(N'dbo.DF_opportunity_SupportsQuotes', N'D') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP CONSTRAINT DF_opportunity_SupportsQuotes;
                IF COL_LENGTH(N'dbo.opportunity', N'DataProvider') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP COLUMN DataProvider;
                IF COL_LENGTH(N'dbo.opportunity', N'DataFeed') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP COLUMN DataFeed;
                IF COL_LENGTH(N'dbo.opportunity', N'DataCoverage') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP COLUMN DataCoverage;
                IF COL_LENGTH(N'dbo.opportunity', N'IsRealTimeData') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP COLUMN IsRealTimeData;
                IF COL_LENGTH(N'dbo.opportunity', N'SupportsTrades') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP COLUMN SupportsTrades;
                IF COL_LENGTH(N'dbo.opportunity', N'SupportsQuotes') IS NOT NULL
                    ALTER TABLE dbo.opportunity DROP COLUMN SupportsQuotes;
            END;
            """);
}
