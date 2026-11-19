using GHOST.API.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GHOST.Tests.Configuration;

public sealed class DatabaseConnectionOptionsValidatorTests
{
    private readonly DatabaseConnectionOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsExplicitNonReservedDevelopmentDatabase()
    {
        var options = new DatabaseConnectionOptions
        {
            DefaultConnection = "Server=localhost;Database=GHOST_DEV;Trusted_Connection=True;TrustServerCertificate=True"
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsMissingConnectionString()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new DatabaseConnectionOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("is required"));
    }

    [Theory]
    [InlineData("GHOST2")]
    [InlineData("GHOST_AUTH02_IT")]
    [InlineData("master")]
    [InlineData("model")]
    [InlineData("msdb")]
    [InlineData("tempdb")]
    public void Validate_RejectsReservedDatabaseCatalogs(string catalog)
    {
        var options = new DatabaseConnectionOptions
        {
            DefaultConnection = $"Server=localhost;Database={catalog};Trusted_Connection=True"
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("not permitted"));
    }

    [Fact]
    public void Validate_RejectsMalformedConnectionString()
    {
        var options = new DatabaseConnectionOptions
        {
            DefaultConnection = "not-a-valid-connection-string"
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("not a valid SQL Server"));
    }

    [Fact]
    public void Validate_RequiresExplicitServerAndCatalog()
    {
        var noServer = _validator.Validate(
            Options.DefaultName,
            new DatabaseConnectionOptions
            {
                DefaultConnection = "Database=GHOST_DEV;Trusted_Connection=True"
            });
        var noCatalog = _validator.Validate(
            Options.DefaultName,
            new DatabaseConnectionOptions
            {
                DefaultConnection = "Server=localhost;Trusted_Connection=True"
            });

        Assert.True(noServer.Failed);
        Assert.Contains(noServer.Failures, failure => failure.Contains("data source"));
        Assert.True(noCatalog.Failed);
        Assert.Contains(noCatalog.Failures, failure => failure.Contains("database catalog"));
    }

    [Fact]
    public void Validate_RejectsAttachedDatabaseFiles()
    {
        var options = new DatabaseConnectionOptions
        {
            DefaultConnection = "Server=localhost;Database=GHOST_DEV;AttachDbFilename=C:\\data\\ghost.mdf;Trusted_Connection=True"
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("must not attach"));
    }
}

public sealed class DatabaseConnectionStartupTests
{
    [Fact]
    public async Task AddGhostDatabaseConnection_RejectsMissingConnectionAtStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostDatabaseConnection(new ConfigurationBuilder().Build());

        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());
    }

    [Fact]
    public async Task AddGhostDatabaseConnection_StartsWithExplicitSafeCatalog()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Database=GHOST_DEV;Trusted_Connection=True;TrustServerCertificate=True"
            })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostDatabaseConnection(configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
