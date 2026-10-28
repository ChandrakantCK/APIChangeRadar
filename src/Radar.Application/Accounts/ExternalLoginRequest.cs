namespace Radar.Application.Accounts
{
    /// <summary>
    /// A sign-in confirmed by an outside provider such as GitHub.
    /// VerifiedEmail must be an address the provider itself has verified (or null if there is none).
    /// </summary>
    public sealed record ExternalLoginRequest(string Provider, string ProviderKey, string? VerifiedEmail);
}