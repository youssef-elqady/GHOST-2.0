using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace GHOST.API.Configuration;

public sealed class DatabaseConnectionOptionsValidator
    : IValidateOptions<DatabaseConnectionOptions>
{
    private static readonly HashSet<string> ForbiddenCatalogs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "GHOST2",
            "GHOST_AUTH02_IT",
            "master",
            "model",
            "msdb",
            "tempdb"
        };

    public ValidateOptionsResult Validate(
        string? name,
        DatabaseConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DefaultConnection))
        {
            return ValidateOptionsResult.Fail(
                $"{DatabaseConnectionOptions.SectionName}:DefaultConnection is required. Configure it explicitly for this environment.");
        }

        SqlConnectionStringBuilder connection;
        try
        {
            connection = new SqlConnectionStringBuilder(options.DefaultConnection);
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail(
                $"{DatabaseConnectionOptions.SectionName}:DefaultConnection is not a valid SQL Server connection string.");
        }

        if (string.IsNullOrWhiteSpace(connection.DataSource))
        {
            return ValidateOptionsResult.Fail(
                $"{DatabaseConnectionOptions.SectionName}:DefaultConnection must explicitly specify a SQL Server data source.");
        }

        if (string.IsNullOrWhiteSpace(connection.InitialCatalog))
        {
            return ValidateOptionsResult.Fail(
                $"{DatabaseConnectionOptions.SectionName}:DefaultConnection must explicitly specify a database catalog.");
        }

        if (ForbiddenCatalogs.Contains(connection.InitialCatalog))
        {
            return ValidateOptionsResult.Fail(
                $"The database catalog '{connection.InitialCatalog}' is not permitted for GHOST API runtime.");
        }

        if (!string.IsNullOrWhiteSpace(connection.AttachDBFilename))
        {
            return ValidateOptionsResult.Fail(
                $"{DatabaseConnectionOptions.SectionName}:DefaultConnection must not attach a database file.");
        }

        return ValidateOptionsResult.Success;
    }
}
