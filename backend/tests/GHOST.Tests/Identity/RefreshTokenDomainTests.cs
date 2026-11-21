using GHOST.Domain.Entities;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests.Identity;

public sealed class RefreshTokenDomainTests
{
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void Issue_CapsExpiryAtFamilyExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var token = RefreshToken.Issue(
            Guid.NewGuid(), Guid.NewGuid(), Hash, now, TimeSpan.FromDays(7), now.AddDays(2));

        Assert.Equal(now.AddDays(2), token.ExpiresAt);
    }

    [Fact]
    public void Issue_RejectsBadHashOrExpiredFamily()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<BusinessRuleValidationException>(() => RefreshToken.Issue(
            Guid.NewGuid(), Guid.NewGuid(), "short", now, TimeSpan.FromDays(1), now.AddDays(1)));
        Assert.Throws<BusinessRuleValidationException>(() => RefreshToken.Issue(
            Guid.NewGuid(), Guid.NewGuid(), Hash, now, TimeSpan.FromDays(1), now.AddSeconds(-1)));
    }

    [Fact]
    public void Consumed_Or_Revoked_TokensAreNotActive_AndCannotBeConsumedAgain()
    {
        var now = DateTimeOffset.UtcNow;
        var token = RefreshToken.Issue(
            Guid.NewGuid(), Guid.NewGuid(), Hash, now, TimeSpan.FromDays(1), now.AddDays(5));

        token.MarkConsumed(Guid.NewGuid(), now);
        Assert.False(token.IsActive(now));
        Assert.Throws<BusinessRuleValidationException>(() => token.MarkConsumed(Guid.NewGuid(), now));

        token.Revoke("logout", now);
        token.Revoke("other", now.AddMinutes(1));
        Assert.Equal("logout", token.RevocationReason);
    }
}
