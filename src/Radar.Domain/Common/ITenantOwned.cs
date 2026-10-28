namespace Radar.Domain.Common
{
    /// <summary>
    /// Marks data that belongs to exactly one organization (tenant).
    /// Every query on such an entity is automatically limited to the current organization.
    /// </summary>
    public interface ITenantOwned
    {
        Guid OrganizationId { get; }
    }
}