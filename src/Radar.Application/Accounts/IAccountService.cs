namespace Radar.Application.Accounts
{
    public interface IAccountService
    {
        /// <summary>
        /// Creates a user, their first organization and an Owner membership, all-or-nothing,
        /// then sends a verification email.
        /// </summary>
        Task<SignUpResult> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirms the email address if the token is valid. Returns false for an unknown user or a bad/expired token.
        /// </summary>
        Task<bool> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a new verification email if the address belongs to an unverified user; otherwise does nothing.
        /// </summary>
        Task ResendVerificationEmailAsync(ResendVerificationEmailRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks email + password (with lockout) and returns an access token for a verified user.
        /// </summary>
        Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);


        /// <summary>
        /// Exchanges a refresh token for a new access token AND a new refresh token (rotation).
        /// Presenting an already-used refresh token revokes the whole session.
        /// </summary>
        Task<RefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// The signed-in user's profile and organizations, or null if the user no longer exists.
        /// </summary>
        Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}