namespace Radar.Application.Accounts
{
    public sealed record OrganizationSummary(Guid Id, string Name, string Role);

    public sealed record UserProfile(Guid UserId, string Email, bool EmailConfirmed, IReadOnlyList<OrganizationSummary> Organizations);
}