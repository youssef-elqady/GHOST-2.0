using Microsoft.Data.SqlClient;

namespace GHOST.SqlServer.Tests;

/// <summary>
/// Safety boundary for AUTH-02 SQL Server tests.
/// Never reads application configuration or GHOST_CONNECTION_STRING.
/// </summary>
internal static class SqlServerTestConnection
{
    internal const string EnvironmentVariableName = "GHOST_AUTH02_TEST_CONNECTION_STRING";
    internal const string RequiredCatalog = "GHOST_AUTH02_IT";

    internal static string ValidateAndNormalize(string? rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
            throw new InvalidOperationException($"Set {EnvironmentVariableName} to a dedicated SQL Server test connection string.");

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(rawConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The AUTH-02 test connection string is invalid.");
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("The AUTH-02 test connection string must specify a SQL Server data source.");

        if (!string.Equals(builder.InitialCatalog, RequiredCatalog, StringComparison.Ordinal))
            throw new InvalidOperationException($"AUTH-02 SQL tests are restricted to the exact database catalog '{RequiredCatalog}'.");

        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename) ||
            !string.IsNullOrWhiteSpace(builder.FailoverPartner))
            throw new InvalidOperationException("AttachDBFilename and Failover Partner are not allowed for AUTH-02 SQL tests.");

        // Never print this value: it may contain credentials.
        return builder.ConnectionString;
    }

    internal static string GetRequiredConnectionString() =>
        ValidateAndNormalize(Environment.GetEnvironmentVariable(EnvironmentVariableName));
}
