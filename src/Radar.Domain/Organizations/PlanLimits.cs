namespace Radar.Domain.Organizations
{
    /// <summary>
    /// What each plan allows. Enforced on the server: the web app only displays these numbers.
    /// </summary>
    public static class PlanLimits
    {
        public static int MaxWatches(OrganizationPlan plan) => plan switch
        {
            OrganizationPlan.Free => 3,
            OrganizationPlan.Pro => 50,
            OrganizationPlan.Team => 200,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
        };
    }
}
