using Radar.Application.Abstractions;

namespace Radar.Application.Accounts
{
    public sealed record LoginRequest(string Email, string Password);

    /// <summary>The raw refresh token goes to the client exactly once; only its hash is stored.</summary>
    public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

    public enum LoginStatus
    {
        Succeeded,
        InvalidCredentials,
        EmailNotConfirmed,
        LockedOut
    }

    public sealed record LoginResult(LoginStatus Status, AccessToken? AccessToken, IssuedRefreshToken? RefreshToken)
    {
        public static LoginResult Success(AccessToken accessToken, IssuedRefreshToken refreshToken) =>
            new(LoginStatus.Succeeded, accessToken, refreshToken);

        public static LoginResult Failure(LoginStatus status) => new(status, null, null);
    }

    public enum RefreshStatus
    {
        Succeeded,
        Invalid,
        ReuseDetected
    }

    public sealed record RefreshResult(RefreshStatus Status, AccessToken? AccessToken, IssuedRefreshToken? RefreshToken)
    {
        public static RefreshResult Success(AccessToken accessToken, IssuedRefreshToken refreshToken) =>
            new(RefreshStatus.Succeeded, accessToken, refreshToken);

        public static RefreshResult Failure(RefreshStatus status) => new(status, null, null);
    }
}