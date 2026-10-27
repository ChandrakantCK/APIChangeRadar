namespace Radar.Application.Accounts
{
    public sealed record VerifyEmailRequest(Guid UserId, string Token);

    public sealed record ResendVerificationEmailRequest(string Email);
}