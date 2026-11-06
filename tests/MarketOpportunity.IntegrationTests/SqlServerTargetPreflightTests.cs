using Microsoft.Data.SqlClient;

namespace MarketOpportunity.IntegrationTests;

public sealed class SqlServerTargetPreflightTests
{
    [Fact]
    public async Task Configured_sql_connection_resolves_to_the_dedicated_integration_database()
    {
        var connectionString = IntegrationTestConfiguration.GetSqlTestConnectionString();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME();";

        Assert.Equal("MarketOpportunityEngine_IntegrationTests", await command.ExecuteScalarAsync());
    }
}
