using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Api.Endpoints;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Identity;
using Radar.Infrastructure.Persistence;
using Radar.Integration.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;


namespace Radar.Integration.Tests.Auth
{
    [Collection(ApiCollection.Name)]
    public class SignUpTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";

        private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

        [Fact]
        public async Task Sign_up_creates_user_organization_and_owner_membership()
        {
            var client = factory.CreateClient();
            var email = UniqueEmail();

            var response = await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword, organizationName = "Acme" });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<SignUpResponse>();
            Assert.NotNull(body);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await db.Users.SingleAsync(u => u.Id == body.UserId);
            Assert.Equal(email, user.Email);
            Assert.False(user.EmailConfirmed);

            var organization = await db.Organizations.SingleAsync(o => o.Id == body.OrganizationId);
            Assert.Equal("Acme", organization.Name);

            var membership = await db.Memberships.SingleAsync(m => m.UserId == body.UserId);
            Assert.Equal(body.OrganizationId, membership.OrganizationId);
            Assert.Equal(MembershipRole.Owner, membership.Role);
        }

        [Fact]
        public async Task Password_is_stored_as_a_hash_never_as_plain_text()
        {
            var client = factory.CreateClient();
            var email = UniqueEmail();

            await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword });

            using var scope = factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);

            Assert.NotNull(user);
            Assert.NotEqual(ValidPassword, user.PasswordHash);
            Assert.True(await userManager.CheckPasswordAsync(user, ValidPassword));
        }

        [Fact]
        public async Task Short_password_is_rejected_and_nothing_is_saved()
        {
            var client = factory.CreateClient();
            var email = UniqueEmail();
            var organizationName = $"Org {Guid.NewGuid():N}";

            var response = await client.PostAsJsonAsync("/auth/signup", new { email, password = "too-short", organizationName });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
            Assert.False(await db.Organizations.AnyAsync(o => o.Name == organizationName));
        }

        [Fact]
        public async Task Same_email_cannot_sign_up_twice()
        {
            var client = factory.CreateClient();
            var email = UniqueEmail();

            var first = await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword });
            var second = await client.PostAsJsonAsync("/auth/signup", new { email = email.ToUpperInvariant(), password = ValidPassword });

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        }
    }
}
