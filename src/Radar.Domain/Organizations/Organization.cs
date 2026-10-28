namespace Radar.Domain.Organizations
{
    public sealed class Organization
    {
        public const int NameMaxLength = 100;

        // Used by EF Core when loading rows from the database.
        private Organization()
        {
        }

        public Organization(string name)
        {
            Id = Guid.CreateVersion7();
            Name = ValidateName(name);
            Plan = OrganizationPlan.Free;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; private set; }

        public string Name { get; private set; } = null!;

        public OrganizationPlan Plan { get; private set; }

        public DateTimeOffset? TrialEndsAt { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public void Rename(string name)
        {
            Name = ValidateName(name);
        }

        private static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Organization name is required.", nameof(name));
            }

            if (name.Trim().Length > NameMaxLength)
            {
                throw new ArgumentException($"Organization name must be at most {NameMaxLength} characters.", nameof(name));
            }

            return name.Trim();
        }
    }
}