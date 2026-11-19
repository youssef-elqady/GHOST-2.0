using GHOST.API.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GHOST.Tests.Identity;

public sealed class JwtAuthenticationStartupTests
{
    [Fact]
    public async Task AddGhostJwtAuthentication_RejectsMissingSettingsAtStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostJwtAuthentication(new ConfigurationBuilder().Build());

        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());
    }

    [Fact]
    public async Task AddGhostJwtAuthentication_StartsWithValidExplicitSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = "GHOST",
                ["Authentication:Jwt:Audience"] = "GHOST.StaffAdmin",
                ["Authentication:Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Authentication:Jwt:AccessTokenLifetimeMinutes"] = "10"
            })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostJwtAuthentication(configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
