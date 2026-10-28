using Radar.Application.Abstractions;

namespace Radar.Application.Accounts
{
    public sealed record LoginRequest(string Email, string Password);

    public enum LoginStatus
    {
        Succeeded,
        InvalidCredentials,
        EmailNotConfirmed,
        LockedOut
    }

    public sealed record LoginResult(LoginStatus Status, AccessToken? AccessToken)
    {
        public static LoginResult Success(AccessToken token) => new(LoginStatus.Succeeded, token);

        public static LoginResult Failure(LoginStatus status) => new(status, null);
    }
}