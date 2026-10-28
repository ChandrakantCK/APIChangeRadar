namespace Radar.Application.Abstractions
{
    public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Issues short-lived access tokens (JWTs) that prove who the caller is.
    /// </summary>
    public interface ITokenService
    {
        AccessToken CreateAccessToken(Guid userId, string email);
    }
}