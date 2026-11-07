using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261106150000_AddMarketReactionResearchContext")]
public sealed class AddMarketReactionResearchContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_reaction_research_context', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.market_reaction_research_context
                (
                    AssessmentId uniqueidentifier NOT NULL
                        CONSTRAINT PK_market_reaction_research_context PRIMARY KEY,
                    EpisodeId uniqueidentifier NOT NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    AsOfUtc datetimeoffset NOT NULL,
                    ContextJson nvarchar(max) NOT NULL,
                    CONSTRAINT FK_market_reaction_research_context_market_reaction_assessment_AssessmentId
                        FOREIGN KEY (AssessmentId) REFERENCES dbo.market_reaction_assessment (Id)
                        ON DELETE NO ACTION
                );
            END;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_research_context')
                  AND name = N'IX_market_reaction_research_context_Symbol_SessionId_AsOfUtc'
            )
                CREATE INDEX IX_market_reaction_research_context_Symbol_SessionId_AsOfUtc
                    ON dbo.market_reaction_research_context (Symbol, SessionId, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_research_context')
                  AND name = N'IX_market_reaction_research_context_EpisodeId'
            )
                CREATE INDEX IX_market_reaction_research_context_EpisodeId
                    ON dbo.market_reaction_research_context (EpisodeId);
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_reaction_research_context', N'U') IS NOT NULL
                DROP TABLE dbo.market_reaction_research_context;
            """);
}
