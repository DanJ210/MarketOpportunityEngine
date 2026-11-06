using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MarketOpportunity.IntegrationTests;

internal static class IntegrationTestConfiguration
{
    private const string EnvironmentVariableName = "MARKETOPPORTUNITY_SQL_TEST_CONNECTION";
    private const string UserSecretKey = "MarketOpportunity:SQL:Test:Connection";
    private const string RequiredServer = "localhost,1433";
    private const string RequiredDatabase = "MarketOpportunityEngine_IntegrationTests";

    public static string GetSqlTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = new ConfigurationBuilder()
                .AddUserSecrets(typeof(IntegrationTestConfiguration).Assembly, optional: true)
                .Build()[UserSecretKey];
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"SQL Server integration tests require a dedicated connection. Configure User Secret '{UserSecretKey}' for the integration-test project or set {EnvironmentVariableName}.");
        }

        return ValidateSqlTestConnectionString(connectionString);
    }

    internal static string ValidateSqlTestConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.DataSource, RequiredServer, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(builder.InitialCatalog, RequiredDatabase, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(builder.FailoverPartner)
            || !string.IsNullOrEmpty(builder.AttachDBFilename))
        {
            throw new InvalidOperationException(
                $"SQL Server integration tests require server '{RequiredServer}' and database '{RequiredDatabase}'.");
        }

        return builder.ConnectionString;
    }
}
