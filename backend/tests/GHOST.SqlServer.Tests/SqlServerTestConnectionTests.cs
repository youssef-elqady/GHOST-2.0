using Microsoft.Data.SqlClient;

namespace GHOST.SqlServer.Tests;

public sealed class SqlServerTestConnectionTests
{
    [Fact]
    public void ValidateAndNormalize_AcceptsOnlyTheDedicatedTestCatalog()
    {
        var result = SqlServerTestConnection.ValidateAndNormalize(
            "Data Source=localhost;Initial Catalog=GHOST_AUTH02_IT;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");

        var parsed = new SqlConnectionStringBuilder(result);
        Assert.Equal("localhost", parsed.DataSource);
        Assert.Equal(SqlServerTestConnection.RequiredCatalog, parsed.InitialCatalog);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndNormalize_RejectsMissingConnectionString(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConnection.ValidateAndNormalize(value));
    }

    [Theory]
    [InlineData("GHOST2")]
    [InlineData("master")]
    [InlineData("GHOST_AUTH02_IT ")]
    [InlineData("ghost_auth02_it")]
    public void ValidateAndNormalize_RejectsAnyCatalogOtherThanExactApprovedName(string catalog)
    {
        var connectionString = $"Data Source=localhost;Initial Catalog={catalog};Integrated Security=True";
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConnection.ValidateAndNormalize(connectionString));
    }

    [Fact]
    public void ValidateAndNormalize_RejectsMissingDataSource()
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConnection.ValidateAndNormalize(
            "Initial Catalog=GHOST_AUTH02_IT;Integrated Security=True"));
    }

    [Fact]
    public void ValidateAndNormalize_RejectsAttachDatabaseFile()
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConnection.ValidateAndNormalize(
            "Data Source=localhost;Initial Catalog=GHOST_AUTH02_IT;AttachDbFilename=C:\\temp\\ghost.mdf;Integrated Security=True"));
    }

    [Fact]
    public void ValidateAndNormalize_RejectsFailoverPartner()
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConnection.ValidateAndNormalize(
            "Data Source=localhost;Initial Catalog=GHOST_AUTH02_IT;Failover Partner=other-server;Integrated Security=True"));
    }
}
