using MarketOpportunity.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MarketOpportunity.IntegrationTests;

public sealed class ExplicitSessionMigrationTests
{
    private const string MigrationId = "20261023190000_AddExplicitSessionIdentity";
    private const string ProvenanceMigrationId = "20261024232400_AddOpportunityMarketDataProvenance";
    private const string TradePlansMigrationId = "20261030210000_AddImmutableTradePlans";

    [Fact]
    public async Task Clean_schema_accepts_explicit_session_migration()
    {
        var database = await CreateIsolatedDatabaseAsync(GetTestConnectionString());
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                Assert.True(await context.Database.EnsureCreatedAsync());
                await context.Database.MigrateAsync();

                Assert.Contains(MigrationId, await context.Database.GetAppliedMigrationsAsync());
                Assert.Contains(ProvenanceMigrationId, await context.Database.GetAppliedMigrationsAsync());
                Assert.Contains(TradePlansMigrationId, await context.Database.GetAppliedMigrationsAsync());
            }

            await AssertSessionColumnsAndIndexesAsync(database.ConnectionString);
            await AssertTradePlanSchemaAsync(database.ConnectionString);
        }
        finally
        {
            await DropIsolatedDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task Legacy_schema_migration_labels_existing_rows_without_rewriting_analysis()
    {
        var database = await CreateIsolatedDatabaseAsync(GetTestConnectionString());
        var eventId = Guid.NewGuid();
        var featureId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();
        var eventTime = new DateTimeOffset(2026, 10, 22, 14, 30, 0, TimeSpan.Zero);
        var receivedTime = eventTime.AddMilliseconds(125);
        var featureTime = eventTime.AddMinutes(1);
        var validUntil = featureTime.AddMinutes(5);
        const string symbol = "LEGACY";
        const string snapshotJson = """{"featureSetVersion":"features-v1","value":42}""";

        try
        {
            await CreateLegacySchemaAndRowsAsync(
                database.ConnectionString,
                eventId,
                featureId,
                opportunityId,
                eventTime,
                receivedTime,
                featureTime,
                validUntil,
                symbol,
                snapshotJson);

            await using (var context = CreateContext(database.ConnectionString))
            {
                Assert.False(await context.Database.EnsureCreatedAsync());
                await context.Database.MigrateAsync();

                Assert.Contains(MigrationId, await context.Database.GetAppliedMigrationsAsync());
                Assert.Contains(TradePlansMigrationId, await context.Database.GetAppliedMigrationsAsync());

                var marketEvent = await context.MarketEvents.AsNoTracking().SingleAsync(item => item.Id == eventId);
                Assert.Equal("legacy-unknown", marketEvent.SessionId);
                Assert.Equal(symbol, marketEvent.Symbol);
                Assert.Equal("Trade", marketEvent.EventType);
                Assert.Equal(eventTime, marketEvent.EventTimeUtc);
                Assert.Equal(receivedTime, marketEvent.ReceivedTimeUtc);
                Assert.Equal("LegacyFixture", marketEvent.Provider);
                Assert.Equal("legacy-sequence-1", marketEvent.SequenceId);
                Assert.Equal("legacy-identity-1", marketEvent.EventIdentity);
                Assert.Equal(123.45m, marketEvent.TradePrice);
                Assert.Equal(250L, marketEvent.TradeSize);
                Assert.Null(marketEvent.BidPrice);
                Assert.Null(marketEvent.AskPrice);
                Assert.Null(marketEvent.BidSize);
                Assert.Null(marketEvent.AskSize);

                var featureSnapshot = await context.FeatureSnapshots.AsNoTracking()
                    .SingleAsync(item => item.Id == featureId);
                Assert.Equal("legacy-unknown", featureSnapshot.SessionId);
                Assert.Equal(symbol, featureSnapshot.Symbol);
                Assert.Equal(featureTime, featureSnapshot.AsOfUtc);
                Assert.Equal("features-v1", featureSnapshot.FeatureSetVersion);
                Assert.Equal(snapshotJson, featureSnapshot.SnapshotJson);

                var opportunity = await context.Opportunities.AsNoTracking()
                    .SingleAsync(item => item.Id == opportunityId);
                Assert.Equal("legacy-unknown", opportunity.SessionId);
                Assert.Equal(symbol, opportunity.Symbol);
                Assert.Equal(featureTime, opportunity.AsOfUtc);
                Assert.Equal(validUntil, opportunity.ValidUntilUtc);
                Assert.Equal("MomentumContinuation", opportunity.SetupType);
                Assert.Equal("Long", opportunity.Bias);
                Assert.Equal("Watch", opportunity.Classification);
                Assert.Equal(73, opportunity.QualityScore);
                Assert.Equal(123.45m, opportunity.ReferencePrice);
                Assert.Equal("features-v1", opportunity.FeatureSetVersion);
                Assert.Equal("setup-v1", opportunity.SetupDefinitionVersion);
                Assert.Equal("config-v1", opportunity.ConfigurationVersion);
            }

            await AssertSessionColumnsAndIndexesAsync(database.ConnectionString);
            await AssertTradePlanSchemaAsync(database.ConnectionString);
        }
        finally
        {
            await DropIsolatedDatabaseAsync(database);
        }
    }

    private static string GetTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETOPPORTUNITY_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server integration test was not run: set MARKETOPPORTUNITY_SQL_TEST_CONNECTION to a dedicated SQL Server test database.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || !builder.InitialCatalog.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require a dedicated database whose name contains 'test'.");
        }

        return builder.ConnectionString;
    }

    private static async Task<IsolatedDatabase> CreateIsolatedDatabaseAsync(string connectionString)
    {
        var databaseName = $"MarketOpportunityEngine_MigrationTest_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        };

        await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
            Pooling = false
        };
        return new IsolatedDatabase(databaseName, databaseBuilder.ConnectionString);
    }

    private static async Task DropIsolatedDatabaseAsync(IsolatedDatabase database)
    {
        var masterBuilder = new SqlConnectionStringBuilder(database.ConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        };

        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE [{database.Name}]";
        await command.ExecuteNonQueryAsync();
    }

    private static MarketOpportunityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MarketOpportunityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new MarketOpportunityDbContext(options);
    }

    private static async Task CreateLegacySchemaAndRowsAsync(
        string connectionString,
        Guid eventId,
        Guid featureId,
        Guid opportunityId,
        DateTimeOffset eventTime,
        DateTimeOffset receivedTime,
        DateTimeOffset featureTime,
        DateTimeOffset validUntil,
        string symbol,
        string snapshotJson)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE dbo.market_event (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                Symbol nvarchar(32) NOT NULL,
                EventType nvarchar(16) NOT NULL,
                EventTimeUtc datetimeoffset NOT NULL,
                ReceivedTimeUtc datetimeoffset NOT NULL,
                Provider nvarchar(64) NOT NULL,
                SequenceId nvarchar(128) NULL,
                EventIdentity nvarchar(64) NOT NULL,
                TradePrice decimal(18, 2) NULL,
                TradeSize bigint NULL,
                BidPrice decimal(18, 2) NULL,
                AskPrice decimal(18, 2) NULL,
                BidSize bigint NULL,
                AskSize bigint NULL
            );
            CREATE TABLE dbo.feature_snapshot (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                Symbol nvarchar(32) NOT NULL,
                AsOfUtc datetimeoffset NOT NULL,
                FeatureSetVersion nvarchar(64) NOT NULL,
                SnapshotJson nvarchar(max) NOT NULL
            );
            CREATE TABLE dbo.opportunity (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                Symbol nvarchar(32) NOT NULL,
                AsOfUtc datetimeoffset NOT NULL,
                ValidUntilUtc datetimeoffset NOT NULL,
                SetupType nvarchar(128) NOT NULL,
                Bias nvarchar(16) NOT NULL,
                Classification nvarchar(32) NOT NULL,
                QualityScore int NOT NULL,
                ReferencePrice decimal(18, 2) NULL,
                FeatureSetVersion nvarchar(64) NOT NULL,
                SetupDefinitionVersion nvarchar(64) NOT NULL,
                ConfigurationVersion nvarchar(64) NOT NULL
            );
            INSERT INTO dbo.market_event
                (Id, Symbol, EventType, EventTimeUtc, ReceivedTimeUtc, Provider, SequenceId,
                 EventIdentity, TradePrice, TradeSize, BidPrice, AskPrice, BidSize, AskSize)
            VALUES
                (@eventId, @symbol, N'Trade', @eventTime, @receivedTime, N'LegacyFixture',
                 N'legacy-sequence-1', N'legacy-identity-1', 123.45, 250, NULL, NULL, NULL, NULL);
            INSERT INTO dbo.feature_snapshot
                (Id, Symbol, AsOfUtc, FeatureSetVersion, SnapshotJson)
            VALUES
                (@featureId, @symbol, @featureTime, N'features-v1', @snapshotJson);
            INSERT INTO dbo.opportunity
                (Id, Symbol, AsOfUtc, ValidUntilUtc, SetupType, Bias, Classification,
                 QualityScore, ReferencePrice, FeatureSetVersion, SetupDefinitionVersion, ConfigurationVersion)
            VALUES
                (@opportunityId, @symbol, @featureTime, @validUntil, N'MomentumContinuation',
                 N'Long', N'Watch', 73, 123.45, N'features-v1', N'setup-v1', N'config-v1');
            """;
        command.Parameters.AddWithValue("@eventId", eventId);
        command.Parameters.AddWithValue("@featureId", featureId);
        command.Parameters.AddWithValue("@opportunityId", opportunityId);
        command.Parameters.AddWithValue("@symbol", symbol);
        command.Parameters.AddWithValue("@eventTime", eventTime);
        command.Parameters.AddWithValue("@receivedTime", receivedTime);
        command.Parameters.AddWithValue("@featureTime", featureTime);
        command.Parameters.AddWithValue("@validUntil", validUntil);
        command.Parameters.AddWithValue("@snapshotJson", snapshotJson);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSessionColumnsAndIndexesAsync(string connectionString)
    {
        var expectedIndexColumns = new[]
        {
            ("feature_snapshot", "IX_feature_snapshot_Symbol_SessionId_AsOfUtc", "Symbol"),
            ("feature_snapshot", "IX_feature_snapshot_Symbol_SessionId_AsOfUtc", "SessionId"),
            ("feature_snapshot", "IX_feature_snapshot_Symbol_SessionId_AsOfUtc", "AsOfUtc"),
            ("market_event", "IX_market_event_Symbol_SessionId_EventTimeUtc", "Symbol"),
            ("market_event", "IX_market_event_Symbol_SessionId_EventTimeUtc", "SessionId"),
            ("market_event", "IX_market_event_Symbol_SessionId_EventTimeUtc", "EventTimeUtc"),
            ("opportunity", "IX_opportunity_Symbol_SessionId_AsOfUtc", "Symbol"),
            ("opportunity", "IX_opportunity_Symbol_SessionId_AsOfUtc", "SessionId"),
            ("opportunity", "IX_opportunity_Symbol_SessionId_AsOfUtc", "AsOfUtc"),
            ("trade_plan", "IX_trade_plan_OpportunityId", "OpportunityId"),
            ("trade_plan", "IX_trade_plan_Symbol_SessionId_AsOfUtc", "Symbol"),
            ("trade_plan", "IX_trade_plan_Symbol_SessionId_AsOfUtc", "SessionId"),
            ("trade_plan", "IX_trade_plan_Symbol_SessionId_AsOfUtc", "AsOfUtc")
        };
        var actualIndexColumns = new List<(string Table, string Index, string Column)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM sys.columns AS c
                INNER JOIN sys.tables AS t ON c.object_id = t.object_id
                WHERE t.schema_id = SCHEMA_ID(N'dbo')
                  AND t.name IN (N'market_event', N'feature_snapshot', N'opportunity')
                  AND c.name = N'SessionId'
                  AND c.is_nullable = 0;
                """;
            Assert.Equal(3, Convert.ToInt32(await columnsCommand.ExecuteScalarAsync()));
        }

        await using (var provenanceCommand = connection.CreateCommand())
        {
            provenanceCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM sys.columns AS c
                INNER JOIN sys.tables AS t ON c.object_id = t.object_id
                WHERE t.schema_id = SCHEMA_ID(N'dbo')
                  AND t.name = N'opportunity'
                  AND c.name IN (
                    N'DataProvider', N'DataFeed', N'DataCoverage',
                    N'IsRealTimeData', N'SupportsTrades', N'SupportsQuotes')
                  AND c.is_nullable = 0;
                """;
            Assert.Equal(6, Convert.ToInt32(await provenanceCommand.ExecuteScalarAsync()));
        }

        await using (var indexesCommand = connection.CreateCommand())
        {
            indexesCommand.CommandText =
                """
                SELECT t.name, i.name, c.name
                FROM sys.indexes AS i
                INNER JOIN sys.tables AS t ON i.object_id = t.object_id
                INNER JOIN sys.index_columns AS ic
                    ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns AS c
                    ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                WHERE i.name IN (
                    N'IX_market_event_Symbol_SessionId_EventTimeUtc',
                    N'IX_feature_snapshot_Symbol_SessionId_AsOfUtc',
                    N'IX_opportunity_Symbol_SessionId_AsOfUtc',
                    N'IX_trade_plan_Symbol_SessionId_AsOfUtc',
                    N'IX_trade_plan_OpportunityId')
                  AND ic.key_ordinal > 0
                ORDER BY t.name, i.name, ic.key_ordinal;
                """;
            await using var reader = await indexesCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                actualIndexColumns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        Assert.Equal(expectedIndexColumns, actualIndexColumns);
    }

    private static async Task AssertTradePlanSchemaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON c.object_id = t.object_id
            WHERE t.schema_id = SCHEMA_ID(N'dbo')
              AND t.name = N'trade_plan'
              AND c.name IN (
                N'Id', N'Symbol', N'SessionId', N'AsOfUtc', N'OpportunityId',
                N'PlanDefinitionVersion', N'TradePlanJson');
            """;
        Assert.Equal(7, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private sealed record IsolatedDatabase(string Name, string ConnectionString);
}
