using GHOST.API.Authentication;
using Microsoft.Extensions.Options;

namespace GHOST.Tests.Identity;

public sealed class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsCompleteStrongConfiguration()
    {
        var options = new JwtOptions
        {
            Issuer = "GHOST",
            Audience = "GHOST.StaffAdmin",
            SigningKey = Convert.ToBase64String(new byte[32]),
            AccessTokenLifetimeMinutes = 10
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredSettings()
    {
        var result = _validator.Validate(Options.DefaultName, new JwtOptions());

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures.Count());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("1234567890123456789012345678901")]
    public void Validate_RejectsSigningKeysShorterThan32Utf8Bytes(string signingKey)
    {
        var options = CreateValidOptions() with { SigningKey = signingKey };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("SigningKey"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(60)]
    public void Validate_RejectsAccessTokenLifetimeOutsideAllowedRange(int lifetimeMinutes)
    {
        var options = CreateValidOptions() with
        {
            AccessTokenLifetimeMinutes = lifetimeMinutes
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("AccessTokenLifetimeMinutes"));
    }

    [Fact]
    public void Validate_RejectsWhitespaceIssuerAndAudience()
    {
        var options = CreateValidOptions() with
        {
            Issuer = " ",
            Audience = "\t"
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains(":Issuer"));
        Assert.Contains(result.Failures, failure => failure.Contains(":Audience"));
    }

    private static JwtOptions CreateValidOptions() => new()
    {
        Issuer = "GHOST",
        Audience = "GHOST.StaffAdmin",
        SigningKey = Convert.ToBase64String(new byte[32]),
        AccessTokenLifetimeMinutes = 10
    };
}
