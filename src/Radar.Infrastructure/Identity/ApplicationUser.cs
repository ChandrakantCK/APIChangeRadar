using Microsoft.AspNetCore.Identity;

namespace Radar.Infrastructure.Identity
{
    /// <summary>
    /// The login account. Lives in Infrastructure because it depends on ASP.NET Core Identity;
    /// the Domain only ever refers to users by their Guid id.
    /// </summary>
    public sealed class ApplicationUser : IdentityUser<Guid>
    {
        public ApplicationUser()
        {
            Id = Guid.CreateVersion7();
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public DateTimeOffset CreatedAt { get; private set; }
    }
}
