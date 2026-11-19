using GHOST.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GHOST.Tests.Identity;

public sealed class GhostIdentityServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGhostIdentity_ConfiguresSharedPasswordAndLockoutPolicy()
    {
        var services = new ServiceCollection();
        services.AddGhostIdentity();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(128, options.Stores.MaxLengthForKeys);
        Assert.False(options.User.RequireUniqueEmail);

        Assert.Equal(12, options.Password.RequiredLength);
        Assert.Equal(4, options.Password.RequiredUniqueChars);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireNonAlphanumeric);

        Assert.True(options.Lockout.AllowedForNewUsers);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
    }
}
