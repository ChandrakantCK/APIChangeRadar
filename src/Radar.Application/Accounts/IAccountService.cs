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
    }
}