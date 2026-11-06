namespace MarketOpportunity.IntegrationTests;

public sealed class IntegrationTestConfigurationTests
{
    [Fact]
    public void Sql_test_connection_accepts_only_the_dedicated_local_integration_database()
    {
        const string connectionString =
            "Server=localhost,1433;Database=MarketOpportunityEngine_IntegrationTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

        var validated = IntegrationTestConfiguration.ValidateSqlTestConnectionString(connectionString);
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(validated);

        Assert.Equal("localhost,1433", builder.DataSource);
        Assert.Equal("MarketOpportunityEngine_IntegrationTests", builder.InitialCatalog);
    }

    [Theory]
    [InlineData("localhost,1433", "MarketOpportunityEngine")]
    [InlineData("localhost,1433", "MarketOpportunityEngine_LiveValidationTests")]
    [InlineData("localhost,1433", "SomeOtherTestDatabase")]
    [InlineData("remote.example,1433", "MarketOpportunityEngine_IntegrationTests")]
    public void Sql_test_connection_rejects_non_test_targets(string server, string database)
    {
        var connectionString =
            $"Server={server};Database={database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

        Assert.Throws<InvalidOperationException>(() =>
            IntegrationTestConfiguration.ValidateSqlTestConnectionString(connectionString));
    }

    [Theory]
    [InlineData("Failover Partner=unapproved.example,1433")]
    [InlineData("AttachDbFilename=unapproved.mdf")]
    public void Sql_test_connection_rejects_alternate_target_overrides(string targetOverride)
    {
        var connectionString =
            $"Server=localhost,1433;Database=MarketOpportunityEngine_IntegrationTests;Integrated Security=True;{targetOverride}";

        Assert.Throws<InvalidOperationException>(() =>
            IntegrationTestConfiguration.ValidateSqlTestConnectionString(connectionString));
    }
}
