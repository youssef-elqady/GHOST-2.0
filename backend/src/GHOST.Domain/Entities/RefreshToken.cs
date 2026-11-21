using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// One link in a refresh-token chain. Only the SHA-256 hash of the raw token is stored.
/// All tokens issued from one login share a <see cref="FamilyId"/>; replaying a consumed
/// token revokes the whole family.
/// </summary>
public sealed class RefreshToken
{
    public const int TokenHashLength = 64;

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset FamilyExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? RevocationReason { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private RefreshToken()
    {
    }

    public static RefreshToken Issue(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan slidingLifetime,
        DateTimeOffset familyExpiresAt)
    {
        if (userId == Guid.Empty)
            throw new BusinessRuleValidationException("UserId cannot be empty.");

        if (familyId == Guid.Empty)
            throw new BusinessRuleValidationException("FamilyId cannot be empty.");

        if (tokenHash is null || tokenHash.Length != TokenHashLength)
            throw new BusinessRuleValidationException("Token hash must be a 64-character SHA-256 hex string.");

        if (slidingLifetime <= TimeSpan.Zero)
            throw new BusinessRuleValidationException("Refresh-token lifetime must be positive.");

        var slidingExpiry = now.Add(slidingLifetime);
        var expiresAt = slidingExpiry < familyExpiresAt ? slidingExpiry : familyExpiresAt;

        if (expiresAt <= now)
            throw new BusinessRuleValidationException("Refresh-token family has already expired.");

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            FamilyExpiresAt = familyExpiresAt
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsConsumed => ConsumedAt is not null;

    public bool IsActive(DateTimeOffset now) =>
        !IsRevoked && !IsConsumed && !IsExpired(now);

    public void MarkConsumed(Guid replacementId, DateTimeOffset now)
    {
        if (!IsActive(now))
            throw new BusinessRuleValidationException("Only an active refresh token can be consumed.");

        ConsumedAt = now;
        ReplacedByTokenId = replacementId;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleValidationException("Revocation reason is required.");

        if (RevokedAt is not null)
            return;

        RevokedAt = now;
        RevocationReason = reason.Trim();
    }
}
