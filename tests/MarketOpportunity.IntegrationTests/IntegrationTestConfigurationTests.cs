namespace MarketOpportunity.IntegrationTests;

public sealed class IntegrationTestConfigurationTests
{
    [Fact]
    public void Sql_test_connection_accepts_only_the_dedicated_local_integration_database()
    {
        const string connectionString =
            "Server=localhost,1433;Database=MarketOpportunityEngine_IntegrationTests;User Id=sa;Password=not-a-real-password;Encrypt=True;TrustServerCertificate=True";

        var validated = IntegrationTestConfiguration.ValidateSqlTestConnectionString(connectionString);

        Assert.Equal("localhost,1433", new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(validated).DataSource);
        Assert.Equal(
            "MarketOpportunityEngine_IntegrationTests",
            new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(validated).InitialCatalog);
    }

    [Theory]
    [InlineData("localhost,1433", "MarketOpportunityEngine")]
    [InlineData("localhost,1433", "MarketOpportunityEngine_LiveValidationTests")]
    [InlineData("remote.example,1433", "MarketOpportunityEngine_IntegrationTests")]
    public void Sql_test_connection_rejects_non_test_targets(string server, string database)
    {
        var connectionString =
            $"Server={server};Database={database};User Id=sa;Password=not-a-real-password;Encrypt=True;TrustServerCertificate=True";

        Assert.Throws<InvalidOperationException>(() =>
            IntegrationTestConfiguration.ValidateSqlTestConnectionString(connectionString));
    }
}
