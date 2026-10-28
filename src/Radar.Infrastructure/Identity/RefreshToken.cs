namespace Radar.Infrastructure.Identity
{
    /// <summary>
    /// One refresh token. All tokens created from the same login share a FamilyId:
    /// each refresh revokes the current token and issues its replacement in the same family.
    /// </summary>
    public sealed class RefreshToken
    {
        // Used by EF Core when loading rows from the database.
        private RefreshToken()
        {
        }

        public RefreshToken(Guid userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        {
            Id = Guid.CreateVersion7();
            UserId = userId;
            FamilyId = familyId;
            TokenHash = tokenHash;
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
        }

        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        public Guid FamilyId { get; private set; }

        /// <summary>SHA-256 of the raw token (hex). The raw token is never stored.</summary>
        public string TokenHash { get; private set; } = null!;

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        public DateTimeOffset? RevokedAt { get; private set; }

        public string? RevokedReason { get; private set; }

        /// <summary>The token that replaced this one when it was rotated.</summary>
        public Guid? ReplacedById { get; private set; }
    }
}