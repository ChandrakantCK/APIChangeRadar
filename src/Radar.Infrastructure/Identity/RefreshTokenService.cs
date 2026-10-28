using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radar.Application.Accounts;
using Radar.Infrastructure.Authentication;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Identity
{
    internal enum RotationStatus
    {
        Rotated,
        Invalid,
        ReuseDetected
    }

    internal sealed record RotationResult(RotationStatus Status, Guid UserId, IssuedRefreshToken? NewToken);

    /// <summary>
    /// Creates, rotates and revokes refresh tokens. Only SHA-256 hashes are stored.
    /// </summary>
    internal sealed class RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider timeProvider, ILogger<RefreshTokenService> logger)
    {
        /// <summary>Starts a new family (a new login session) and returns its first token.</summary>
        public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken)
        {
            var (raw, token) = CreateToken(userId, familyId: Guid.CreateVersion7());
            db.Add(token);
            await db.SaveChangesAsync(cancellationToken);
            return new IssuedRefreshToken(raw, token.ExpiresAt);
        }

        public async Task<RotationResult> RotateAsync(string rawToken, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            var hash = Hash(rawToken);
            var current = await db.Set<RefreshToken>()
                .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

            if (current is null)
            {
                return new RotationResult(RotationStatus.Invalid, Guid.Empty, null);
            }

            if (current.RevokedAt is not null)
            {
                // This token was already used once. Either the user's client misbehaved, or someone
                // stole it. We can't tell which, so we end the whole session (the family).
                await RevokeFamilyAsync(current.FamilyId, "reuse-detected", cancellationToken);
                logger.LogWarning("Refresh token reuse detected for user {UserId}; family {FamilyId} revoked.", current.UserId, current.FamilyId);
                return new RotationResult(RotationStatus.ReuseDetected, current.UserId, null);
            }

            if (current.ExpiresAt <= now)
            {
                return new RotationResult(RotationStatus.Invalid, current.UserId, null);
            }

            var (raw, replacement) = CreateToken(current.UserId, current.FamilyId);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Compare-and-set: revoke the current token ONLY if nobody else revoked it in the meantime.
            // Two requests racing with the same token: exactly one gets rows == 1.
            var rows = await db.Set<RefreshToken>()
                .Where(t => t.Id == current.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, "rotated")
                    .SetProperty(t => t.ReplacedById, replacement.Id),
                    cancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                await RevokeFamilyAsync(current.FamilyId, "reuse-detected", cancellationToken);
                return new RotationResult(RotationStatus.ReuseDetected, current.UserId, null);
            }

            db.Add(replacement);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RotationResult(RotationStatus.Rotated, current.UserId, new IssuedRefreshToken(raw, replacement.ExpiresAt));
        }

        private Task<int> RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            return db.Set<RefreshToken>()
                .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, reason),
                    cancellationToken);
        }

        private (string Raw, RefreshToken Token) CreateToken(Guid userId, Guid familyId)
        {
            // 256 bits from a cryptographic random generator: impossible to guess.
            var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var now = timeProvider.GetUtcNow();
            var token = new RefreshToken(userId, familyId, Hash(raw), now, now.AddDays(options.Value.RefreshTokenDays));
            return (raw, token);
        }

        // A fast hash is fine here (unlike passwords): the token is already 256 random bits,
        // so there is nothing to brute-force, and we need to look tokens up by their hash.
        internal static string Hash(string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}