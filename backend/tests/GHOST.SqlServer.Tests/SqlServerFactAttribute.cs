namespace GHOST.SqlServer.Tests;

/// <summary>
/// SQL integration tests are skipped when no explicit AUTH-02 test connection is configured.
/// If a value is supplied but unsafe, the test runs and fails closed during validation.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            SqlServerTestConnection.EnvironmentVariableName)))
        {
            Skip = $"Set {SqlServerTestConnection.EnvironmentVariableName} to run isolated SQL Server tests.";
        }
    }
}
