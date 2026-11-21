using GHOST.API.Authentication;

namespace GHOST.Tests.Security;

public sealed class LocalOriginPolicyTests
{
    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("https://localhost:3443")]
    [InlineData("http://[::1]:3000")]
    public void Loopback_origins_are_accepted(string origin) =>
        Assert.Equal([origin], LocalOriginPolicy.Validate([origin]));

    [Theory]
    [InlineData("*")]
    [InlineData("http://192.168.1.20:3000")]
    [InlineData("https://ghost.example.com")]
    [InlineData("http://localhost:3000/")]
    [InlineData("http://localhost:3000/app")]
    [InlineData("localhost:3000")]
    [InlineData("ftp://localhost")]
    public void Wildcards_lan_public_and_malformed_origins_fail_startup(string origin) =>
        Assert.Throws<InvalidOperationException>(() => LocalOriginPolicy.Validate([origin]));

    [Fact]
    public void Empty_list_fails_and_duplicates_collapse()
    {
        Assert.Throws<InvalidOperationException>(() => LocalOriginPolicy.Validate([]));
        Assert.Single(LocalOriginPolicy.Validate(["http://localhost:3000", " http://localhost:3000 "]));
    }

    [Fact]
    public void Defaults_pass_validation() =>
        Assert.NotEmpty(LocalOriginPolicy.Validate(new RefreshTokenOptions().AllowedOrigins));
}
