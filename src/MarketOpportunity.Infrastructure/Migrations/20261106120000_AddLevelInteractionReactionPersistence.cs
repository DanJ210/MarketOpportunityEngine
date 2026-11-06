using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261106120000_AddLevelInteractionReactionPersistence")]
public sealed class AddLevelInteractionReactionPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.level_interaction_episode', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.level_interaction_episode
                (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_level_interaction_episode PRIMARY KEY,
                    LevelId uniqueidentifier NOT NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    StartedAtUtc datetimeoffset NOT NULL,
                    LatestAsOfUtc datetimeoffset NOT NULL,
                    Status nvarchar(32) NOT NULL,
                    EpisodeJson nvarchar(max) NOT NULL
                );
            END;
            IF OBJECT_ID(N'dbo.market_reaction_assessment', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.market_reaction_assessment
                (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_market_reaction_assessment PRIMARY KEY,
                    EpisodeId uniqueidentifier NOT NULL,
                    LevelId uniqueidentifier NOT NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    AsOfUtc datetimeoffset NOT NULL,
                    ReactionType nvarchar(40) NULL,
                    AssessmentJson nvarchar(max) NOT NULL,
                    CONSTRAINT FK_market_reaction_assessment_level_interaction_episode_EpisodeId
                        FOREIGN KEY (EpisodeId) REFERENCES dbo.level_interaction_episode (Id)
                );
            END;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.level_interaction_episode')
                  AND name = N'IX_level_interaction_episode_Symbol_SessionId_Status'
            )
                CREATE INDEX IX_level_interaction_episode_Symbol_SessionId_Status
                    ON dbo.level_interaction_episode (Symbol, SessionId, Status);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.level_interaction_episode')
                  AND name = N'IX_level_interaction_episode_LevelId'
            )
                CREATE INDEX IX_level_interaction_episode_LevelId
                    ON dbo.level_interaction_episode (LevelId);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.level_interaction_episode')
                  AND name = N'IX_level_interaction_episode_StartedAtUtc'
            )
                CREATE INDEX IX_level_interaction_episode_StartedAtUtc
                    ON dbo.level_interaction_episode (StartedAtUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_assessment')
                  AND name = N'IX_market_reaction_assessment_Symbol_SessionId_AsOfUtc'
            )
                CREATE INDEX IX_market_reaction_assessment_Symbol_SessionId_AsOfUtc
                    ON dbo.market_reaction_assessment (Symbol, SessionId, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_assessment')
                  AND name = N'IX_market_reaction_assessment_EpisodeId'
            )
                CREATE INDEX IX_market_reaction_assessment_EpisodeId
                    ON dbo.market_reaction_assessment (EpisodeId);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_assessment')
                  AND name = N'UX_market_reaction_assessment_EpisodeId_Confirmed'
            )
                CREATE UNIQUE INDEX UX_market_reaction_assessment_EpisodeId_Confirmed
                    ON dbo.market_reaction_assessment (EpisodeId)
                    WHERE ReactionType IS NOT NULL;
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_assessment')
                  AND name = N'IX_market_reaction_assessment_LevelId'
            )
                CREATE INDEX IX_market_reaction_assessment_LevelId
                    ON dbo.market_reaction_assessment (LevelId);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_reaction_assessment')
                  AND name = N'IX_market_reaction_assessment_ReactionType'
            )
                CREATE INDEX IX_market_reaction_assessment_ReactionType
                    ON dbo.market_reaction_assessment (ReactionType);
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_reaction_assessment', N'U') IS NOT NULL
                DROP TABLE dbo.market_reaction_assessment;
            IF OBJECT_ID(N'dbo.level_interaction_episode', N'U') IS NOT NULL
                DROP TABLE dbo.level_interaction_episode;
            """);
}
