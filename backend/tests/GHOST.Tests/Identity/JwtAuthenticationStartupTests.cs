using GHOST.API.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
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
        var configuration = CreateValidConfiguration();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostJwtAuthentication(configuration);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddGhostJwtAuthentication_RequiresAuthenticationByDefault()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostJwtAuthentication(CreateValidConfiguration());

        using var provider = builder.Services.BuildServiceProvider();
        var authorizationOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var fallbackPolicy = authorizationOptions.FallbackPolicy;

        Assert.NotNull(fallbackPolicy);
        Assert.Contains(
            fallbackPolicy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(
            fallbackPolicy.Requirements,
            requirement => requirement is ClaimsAuthorizationRequirement claimRequirement &&
                claimRequirement.ClaimType == GhostClaimTypes.BusinessAccess);

        var passwordChangePolicy = authorizationOptions.GetPolicy("PasswordChangeOnly");
        Assert.NotNull(passwordChangePolicy);
        Assert.Contains(
            passwordChangePolicy.Requirements,
            requirement => requirement is ClaimsAuthorizationRequirement claimRequirement &&
                claimRequirement.ClaimType == GhostClaimTypes.MustChangePassword);
    }

    private static IConfiguration CreateValidConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = "GHOST",
                ["Authentication:Jwt:Audience"] = "GHOST.StaffAdmin",
                ["Authentication:Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Authentication:Jwt:AccessTokenLifetimeMinutes"] = "10"
            })
            .Build();
}
