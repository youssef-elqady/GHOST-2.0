using Microsoft.AspNetCore.WebUtilities;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using GHOST.Domain.Entities;
using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GHOST.API.Authentication;

public enum RefreshOutcome
{
    Rotated,
    Rejected,
    ReplayDetected
}

public sealed record IssuedRefreshToken(
    string RawValue,
    Guid FamilyId,
    DateTimeOffset ExpiresAtUtc);

public sealed record RefreshResult(
    RefreshOutcome Outcome,
    Guid? UserId,
    Guid? FamilyId,
    IssuedRefreshToken? Replacement);

public sealed record RevokedSession(Guid UserId, Guid FamilyId);

/// <summary>
/// Issues, rotates and revokes refresh tokens. Raw tokens are never persisted; only their
/// SHA-256 hash is stored. Reuse of a consumed or revoked token revokes the entire family.
/// </summary>
public sealed class RefreshTokenService
{
    public const string ReplayReason = "replay_detected";
    public const string LogoutReason = "logout";
    public const string SessionsRevokedReason = "sessions_revoked";

    private readonly GHOSTDbContext _dbContext;
    private readonly RefreshTokenOptions _options;
    private readonly TimeProvider _timeProvider;

    public RefreshTokenService(
        GHOSTDbContext dbContext,
        IOptions<RefreshTokenOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public static string GenerateRawToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawToken);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    public async Task<IssuedRefreshToken> StartFamilyAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var familyId = Guid.NewGuid();
        var familyExpiresAt = now.AddDays(_options.AbsoluteLifetimeDays);

        var (entity, raw) = CreateToken(userId, familyId, now, familyExpiresAt);
        _dbContext.RefreshTokens.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(raw, familyId, entity.ExpiresAt);
    }

    public async Task<RefreshResult> RotateAsync(
        string? rawToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 128)
            return new RefreshResult(RefreshOutcome.Rejected, null, null, null);

        var hash = Hash(rawToken);
        var now = _timeProvider.GetUtcNow();

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

        var token = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);

        if (token is null)
            return new RefreshResult(RefreshOutcome.Rejected, null, null, null);

        if (token.IsConsumed || token.IsRevoked)
        {
            await RevokeFamilyInternalAsync(token.FamilyId, ReplayReason, now, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new RefreshResult(
                RefreshOutcome.ReplayDetected, token.UserId, token.FamilyId, null);
        }

        if (token.IsExpired(now) || now >= token.FamilyExpiresAt)
            return new RefreshResult(RefreshOutcome.Rejected, token.UserId, token.FamilyId, null);

        var (replacement, raw) = CreateToken(
            token.UserId, token.FamilyId, now, token.FamilyExpiresAt);

        token.MarkConsumed(replacement.Id, now);
        _dbContext.RefreshTokens.Add(replacement);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request consumed or revoked this token first. Two descendants must
            // never be valid, so treat the loser as a replay and revoke the whole family.
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);

            _dbContext.ChangeTracker.Clear();
            await RevokeFamilyInternalAsync(token.FamilyId, ReplayReason, now, cancellationToken);

            return new RefreshResult(
                RefreshOutcome.ReplayDetected, token.UserId, token.FamilyId, null);
        }

        return new RefreshResult(
            RefreshOutcome.Rotated,
            token.UserId,
            token.FamilyId,
            new IssuedRefreshToken(raw, token.FamilyId, replacement.ExpiresAt));
    }

    /// <summary>Revokes the family of the presented token. Idempotent; null when unknown.</summary>
    public async Task<RevokedSession?> RevokeFamilyByTokenAsync(
        string? rawToken,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 128)
            return null;

        var hash = Hash(rawToken);
        var token = await _dbContext.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);

        if (token is null)
            return null;

        await RevokeFamilyInternalAsync(
            token.FamilyId, reason, _timeProvider.GetUtcNow(), cancellationToken);

        return new RevokedSession(token.UserId, token.FamilyId);
    }

    /// <summary>Revokes every session of a user (password change, reset, account disable).</summary>
    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var tokens = await _dbContext.RefreshTokens
            .Where(item => item.UserId == userId && item.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
            token.Revoke(reason, now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }

    /// <summary>
    /// True while the family has a live, unrevoked, unconsumed token inside its absolute lifetime.
    /// Used to reject access tokens whose session was logged out or revoked.
    /// </summary>
    public Task<bool> IsFamilyActiveAsync(
        Guid userId,
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return _dbContext.RefreshTokens.AnyAsync(
            item => item.FamilyId == familyId
                && item.UserId == userId
                && item.RevokedAt == null
                && item.ConsumedAt == null
                && item.ExpiresAt > now
                && item.FamilyExpiresAt > now,
            cancellationToken);
    }

    private async Task RevokeFamilyInternalAsync(
        Guid familyId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(item => item.FamilyId == familyId && item.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
            token.Revoke(reason, now);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private (RefreshToken Entity, string Raw) CreateToken(
        Guid userId,
        Guid familyId,
        DateTimeOffset now,
        DateTimeOffset familyExpiresAt)
    {
        var raw = GenerateRawToken();
        var entity = RefreshToken.Issue(
            userId,
            familyId,
            Hash(raw),
            now,
            TimeSpan.FromDays(_options.SlidingLifetimeDays),
            familyExpiresAt);

        return (entity, raw);
    }
}
