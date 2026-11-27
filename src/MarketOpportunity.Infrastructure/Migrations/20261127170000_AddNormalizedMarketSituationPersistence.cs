using MarketOpportunity.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketOpportunity.Infrastructure.Migrations;

[DbContext(typeof(MarketOpportunityDbContext))]
[Migration("20261127170000_AddNormalizedMarketSituationPersistence")]
public sealed class AddNormalizedMarketSituationPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_situation_candidate_set', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.market_situation_candidate_set
                (
                    Id char(64) COLLATE Latin1_General_100_BIN2 NOT NULL
                        CONSTRAINT PK_market_situation_candidate_set PRIMARY KEY,
                    CanonicalizationVersion nvarchar(64) NOT NULL,
                    FeatureSnapshotId uniqueidentifier NOT NULL,
                    PriorSessionContextId uniqueidentifier NULL,
                    Symbol nvarchar(32) NOT NULL,
                    SessionId nvarchar(64) NOT NULL,
                    AsOfUtc datetimeoffset(7) NOT NULL,
                    ContextMode nvarchar(32) NOT NULL,
                    SupportCandidateCount int NOT NULL,
                    ResistanceCandidateCount int NOT NULL,
                    TotalCandidateCount int NOT NULL,
                    TestZonePercent decimal(38, 18) NOT NULL,
                    ApproachDistancePercent decimal(38, 18) NOT NULL,
                    DefinitionVersion nvarchar(64) NOT NULL,
                    FeatureSetVersion nvarchar(64) NOT NULL,
                    ConfigurationVersion nvarchar(64) NOT NULL,
                    MarketDataProvider nvarchar(64) NOT NULL,
                    MarketDataFeed nvarchar(64) NOT NULL,
                    MarketDataCoverage nvarchar(32) NOT NULL,
                    MarketDataIsRealTime bit NOT NULL,
                    MarketDataSupportsTrades bit NOT NULL,
                    MarketDataSupportsQuotes bit NOT NULL,
                    DataQuality nvarchar(32) NOT NULL,
                    MarketStructureStatus nvarchar(32) NOT NULL,
                    HasCompleteCurrentSessionData bit NOT NULL,
                    MaterialDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    CandidateMaterialDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    IsComplete bit NOT NULL,
                    CreatedAtUtc datetimeoffset(7) NOT NULL,
                    CompletedAtUtc datetimeoffset(7) NULL,
                    CONSTRAINT CK_market_situation_candidate_set_Counts CHECK
                    (
                        SupportCandidateCount >= 0
                        AND ResistanceCandidateCount >= 0
                        AND TotalCandidateCount = SupportCandidateCount + ResistanceCandidateCount
                    ),
                    CONSTRAINT CK_market_situation_candidate_set_Completion CHECK
                    (
                        (IsComplete = 0 AND CompletedAtUtc IS NULL)
                        OR (IsComplete = 1 AND CompletedAtUtc IS NOT NULL)
                    )
                );
            END;

            IF OBJECT_ID(N'dbo.market_situation_candidate_source', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.market_situation_candidate_source
                (
                    Id char(64) COLLATE Latin1_General_100_BIN2 NOT NULL
                        CONSTRAINT PK_market_situation_candidate_source PRIMARY KEY,
                    SourceContextId uniqueidentifier NULL,
                    SourceSessionId nvarchar(64) NOT NULL,
                    Symbol nvarchar(32) NOT NULL,
                    CurrentSessionId nvarchar(64) NOT NULL,
                    SourceKind nvarchar(32) NOT NULL,
                    ContextMode nvarchar(32) NOT NULL,
                    SourceCompleteness nvarchar(256) NOT NULL,
                    HistoricalSource nvarchar(128) NULL,
                    SourceCoverageStartUtc datetimeoffset(7) NULL,
                    SourceCoverageEndUtc datetimeoffset(7) NULL,
                    StructureAlgorithmVersion nvarchar(64) NULL,
                    FeatureSetVersion nvarchar(64) NULL,
                    ContextDefinitionVersion nvarchar(64) NULL,
                    HistoricalProvider nvarchar(64) NULL,
                    HistoricalFeed nvarchar(64) NULL,
                    MaterialDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    CreatedAtUtc datetimeoffset(7) NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.market_situation_candidate_member', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.market_situation_candidate_member
                (
                    CandidateSetId char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    Ordinal int NOT NULL,
                    LevelId uniqueidentifier NOT NULL,
                    CandidateSourceId char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    Role nvarchar(16) NOT NULL,
                    Price decimal(38, 18) NOT NULL,
                    PriceCanonical nvarchar(64) NOT NULL,
                    SourceStructuralIdentity uniqueidentifier NOT NULL,
                    SourceEventIdentity nvarchar(256) NULL,
                    ConfirmationEventIdentity nvarchar(256) NULL,
                    SourceEventTimeUtc datetimeoffset(7) NULL,
                    ConfirmedAtEventTimeUtc datetimeoffset(7) NULL,
                    CONSTRAINT PK_market_situation_candidate_member
                        PRIMARY KEY (CandidateSetId, Ordinal),
                    CONSTRAINT FK_market_situation_candidate_member_Set
                        FOREIGN KEY (CandidateSetId)
                        REFERENCES dbo.market_situation_candidate_set (Id),
                    CONSTRAINT FK_market_situation_candidate_member_Source
                        FOREIGN KEY (CandidateSourceId)
                        REFERENCES dbo.market_situation_candidate_source (Id)
                );
            END;

            IF COL_LENGTH(N'dbo.market_situation_assessment', N'PersistenceVersion') IS NULL
                ALTER TABLE dbo.market_situation_assessment
                    ADD PersistenceVersion nvarchar(64) NULL;
            IF COL_LENGTH(N'dbo.market_situation_assessment', N'CandidateSetId') IS NULL
                ALTER TABLE dbo.market_situation_assessment
                    ADD CandidateSetId char(64) COLLATE Latin1_General_100_BIN2 NULL;
            IF COL_LENGTH(N'dbo.market_situation_assessment', N'AssessmentMaterialDigest') IS NULL
                ALTER TABLE dbo.market_situation_assessment
                    ADD AssessmentMaterialDigest char(64) COLLATE Latin1_General_100_BIN2 NULL;
            IF COL_LENGTH(N'dbo.market_situation_assessment', N'SupportLevelId') IS NULL
                ALTER TABLE dbo.market_situation_assessment
                    ADD SupportLevelId uniqueidentifier NULL;
            IF COL_LENGTH(N'dbo.market_situation_assessment', N'ResistanceLevelId') IS NULL
                ALTER TABLE dbo.market_situation_assessment
                    ADD ResistanceLevelId uniqueidentifier NULL;
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_candidate_set')
                  AND name = N'IX_market_situation_candidate_set_Symbol_SessionId_AsOfUtc'
            )
                CREATE INDEX IX_market_situation_candidate_set_Symbol_SessionId_AsOfUtc
                    ON dbo.market_situation_candidate_set (Symbol, SessionId, AsOfUtc);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_candidate_set')
                  AND name = N'IX_market_situation_candidate_set_MaterialDigest'
            )
                CREATE INDEX IX_market_situation_candidate_set_MaterialDigest
                    ON dbo.market_situation_candidate_set (MaterialDigest);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_candidate_source')
                  AND name = N'IX_market_situation_candidate_source_MaterialDigest'
            )
                CREATE INDEX IX_market_situation_candidate_source_MaterialDigest
                    ON dbo.market_situation_candidate_source (MaterialDigest);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_candidate_member')
                  AND name = N'UX_market_situation_candidate_member_Set_LevelId'
            )
                CREATE UNIQUE INDEX UX_market_situation_candidate_member_Set_LevelId
                    ON dbo.market_situation_candidate_member (CandidateSetId, LevelId);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_candidate_member')
                  AND name = N'IX_market_situation_candidate_member_CandidateSourceId'
            )
                CREATE INDEX IX_market_situation_candidate_member_CandidateSourceId
                    ON dbo.market_situation_candidate_member (CandidateSourceId);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                  AND name = N'IX_market_situation_assessment_CandidateSetId'
            )
                CREATE INDEX IX_market_situation_assessment_CandidateSetId
                    ON dbo.market_situation_assessment (CandidateSetId);
            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.foreign_keys AS fk
                INNER JOIN sys.foreign_key_columns AS fkc
                    ON fkc.constraint_object_id = fk.object_id
                INNER JOIN sys.columns AS parent_column
                    ON parent_column.object_id = fkc.parent_object_id
                   AND parent_column.column_id = fkc.parent_column_id
                WHERE fk.parent_object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                  AND fk.referenced_object_id = OBJECT_ID(N'dbo.market_situation_candidate_set')
                  AND parent_column.name = N'CandidateSetId'
            )
                ALTER TABLE dbo.market_situation_assessment
                    ADD CONSTRAINT FK_market_situation_assessment_CandidateSet
                        FOREIGN KEY (CandidateSetId)
                        REFERENCES dbo.market_situation_candidate_set (Id);
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.check_constraints
                WHERE name = N'CK_market_situation_assessment_PersistenceEnvelope'
                  AND parent_object_id = OBJECT_ID(N'dbo.market_situation_assessment')
            )
                ALTER TABLE dbo.market_situation_assessment
                    ADD CONSTRAINT CK_market_situation_assessment_PersistenceEnvelope CHECK
                    (
                        (PersistenceVersion IS NULL AND CandidateSetId IS NULL
                            AND AssessmentMaterialDigest IS NULL)
                        OR
                        (PersistenceVersion = N'market-situation-persistence-v2'
                            AND CandidateSetId IS NOT NULL
                            AND AssessmentMaterialDigest IS NOT NULL)
                    );

            IF NOT EXISTS
            (
                SELECT 1 FROM sys.check_constraints
                WHERE name = N'CK_market_situation_candidate_set_Counts'
                  AND parent_object_id = OBJECT_ID(N'dbo.market_situation_candidate_set')
            )
                ALTER TABLE dbo.market_situation_candidate_set
                    ADD CONSTRAINT CK_market_situation_candidate_set_Counts CHECK
                    (
                        SupportCandidateCount >= 0
                        AND ResistanceCandidateCount >= 0
                        AND TotalCandidateCount = SupportCandidateCount + ResistanceCandidateCount
                    );
            IF NOT EXISTS
            (
                SELECT 1 FROM sys.check_constraints
                WHERE name = N'CK_market_situation_candidate_set_Completion'
                  AND parent_object_id = OBJECT_ID(N'dbo.market_situation_candidate_set')
            )
                ALTER TABLE dbo.market_situation_candidate_set
                    ADD CONSTRAINT CK_market_situation_candidate_set_Completion CHECK
                    (
                        (IsComplete = 0 AND CompletedAtUtc IS NULL)
                        OR (IsComplete = 1 AND CompletedAtUtc IS NOT NULL)
                    );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.market_situation_assessment', N'U') IS NOT NULL
            BEGIN
                DECLARE @candidateSetForeignKey sysname =
                (
                    SELECT TOP (1) fk.name
                    FROM sys.foreign_keys AS fk
                    INNER JOIN sys.foreign_key_columns AS fkc
                        ON fkc.constraint_object_id = fk.object_id
                    INNER JOIN sys.columns AS parent_column
                        ON parent_column.object_id = fkc.parent_object_id
                       AND parent_column.column_id = fkc.parent_column_id
                    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.market_situation_assessment')
                      AND fk.referenced_object_id = OBJECT_ID(N'dbo.market_situation_candidate_set')
                      AND parent_column.name = N'CandidateSetId'
                );
                IF @candidateSetForeignKey IS NOT NULL
                    EXEC sys.sp_executesql N'ALTER TABLE dbo.market_situation_assessment DROP CONSTRAINT '
                        + QUOTENAME(@candidateSetForeignKey);
                IF EXISTS
                (
                    SELECT 1 FROM sys.check_constraints
                    WHERE name = N'CK_market_situation_assessment_PersistenceEnvelope'
                )
                    ALTER TABLE dbo.market_situation_assessment
                        DROP CONSTRAINT CK_market_situation_assessment_PersistenceEnvelope;
                DROP INDEX IF EXISTS IX_market_situation_assessment_CandidateSetId
                    ON dbo.market_situation_assessment;
                ALTER TABLE dbo.market_situation_assessment
                    DROP COLUMN IF EXISTS PersistenceVersion;
                ALTER TABLE dbo.market_situation_assessment
                    DROP COLUMN IF EXISTS CandidateSetId;
                ALTER TABLE dbo.market_situation_assessment
                    DROP COLUMN IF EXISTS AssessmentMaterialDigest;
                ALTER TABLE dbo.market_situation_assessment
                    DROP COLUMN IF EXISTS SupportLevelId;
                ALTER TABLE dbo.market_situation_assessment
                    DROP COLUMN IF EXISTS ResistanceLevelId;
            END;

            DROP TABLE IF EXISTS dbo.market_situation_candidate_member;
            DROP TABLE IF EXISTS dbo.market_situation_candidate_source;
            DROP TABLE IF EXISTS dbo.market_situation_candidate_set;
            """);
}
