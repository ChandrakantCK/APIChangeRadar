using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radar.Api.Endpoints;
using Radar.Api.Tenancy;
using Radar.Application.Accounts;
using Radar.Application.Organizations;
using Radar.Domain.Auditing;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Identity;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Tenancy;
using Radar.Integration.Tests.Infrastructure;

namespace Radar.Integration.Tests.Tenancy
{
    [Collection(ApiCollection.Name)]
    public class TenancyTests(RadarApiFactory factory)
    {
        private const string ValidPassword = "correct horse battery staple";

        private sealed record TestUser(Guid UserId, Guid OrganizationId, HttpClient Client);

        // Signs up a verified user with their own organization and returns a client that carries their token.
        private async Task<TestUser> CreateUserAsync(string organizationName)
        {
            var client = factory.CreateClient();
            var email = $"user-{Guid.NewGuid():N}@example.com";
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword, organizationName })).EnsureSuccessStatusCode();

            using (var scope = factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                await userManager.ConfirmEmailAsync(user!, await userManager.GenerateEmailConfirmationTokenAsync(user!));
            }

            var login = await client.PostAsJsonAsync("/auth/login", new { email, password = ValidPassword });
            var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var me = (await client.GetFromJsonAsync<UserProfile>("/me"))!;
            return new TestUser(me.UserId, me.Organizations.Single().Id, client);
        }

        private static HttpRequestMessage ForOrganization(HttpMethod method, string path, Guid organizationId, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add(OrganizationScopeFilter.HeaderName, organizationId.ToString());
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return request;
        }

        [Fact]
        public async Task A_user_sees_their_own_organization_by_default()
        {
            var alice = await CreateUserAsync("Alice Inc");

            var organization = await alice.Client.GetFromJsonAsync<OrganizationDetails>("/organizations/current");

            Assert.Equal(alice.OrganizationId, organization!.Id);
            Assert.Equal("Alice Inc", organization.Name);
            Assert.Equal("Owner", organization.Role);
        }

        // IDOR: Bob changes the organization id to Alice's. He must get nothing.
        [Fact]
        public async Task A_user_cannot_read_or_change_another_organization()
        {
            var alice = await CreateUserAsync("Alice Inc");
            var bob = await CreateUserAsync("Bob LLC");

            var read = await bob.Client.SendAsync(ForOrganization(HttpMethod.Get, "/organizations/current", alice.OrganizationId));
            var auditLog = await bob.Client.SendAsync(ForOrganization(HttpMethod.Get, "/organizations/current/audit-log", alice.OrganizationId));
            var rename = await bob.Client.SendAsync(ForOrganization(HttpMethod.Patch, "/organizations/current", alice.OrganizationId, new { name = "Pwned" }));

            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, auditLog.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Alice Inc", (await db.Organizations.SingleAsync(o => o.Id == alice.OrganizationId)).Name);
        }

        [Fact]
        public async Task Audit_log_only_ever_shows_the_current_organizations_entries()
        {
            var alice = await CreateUserAsync("Alice Inc");
            var bob = await CreateUserAsync("Bob LLC");
            var renamed = await alice.Client.PatchAsJsonAsync("/organizations/current", new { name = "Alice Group" });
            renamed.EnsureSuccessStatusCode();

            var aliceLog = await alice.Client.GetFromJsonAsync<List<AuditEntryView>>("/organizations/current/audit-log");
            var bobLog = await bob.Client.GetFromJsonAsync<List<AuditEntryView>>("/organizations/current/audit-log");

            Assert.Contains(aliceLog!, e => e.Action == "organization.renamed" && e.Target == "Alice Group");
            Assert.Contains(aliceLog!, e => e.Action == "organization.created");
            Assert.All(aliceLog!, e => Assert.Equal(alice.UserId, e.ActorUserId));
            Assert.All(bobLog!, e => Assert.Equal(bob.UserId, e.ActorUserId));
            Assert.DoesNotContain(bobLog!, e => e.Target == "Alice Group");
        }

        [Fact]
        public async Task A_member_can_read_but_not_rename()
        {
            var alice = await CreateUserAsync("Alice Inc");
            var bob = await CreateUserAsync("Bob LLC");

            // Make Bob a plain Member of Alice's organization (invites come later).
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Memberships.Add(new Membership(alice.OrganizationId, bob.UserId, MembershipRole.Member));
                await db.SaveChangesAsync();
            }

            var read = await bob.Client.SendAsync(ForOrganization(HttpMethod.Get, "/organizations/current", alice.OrganizationId));
            var rename = await bob.Client.SendAsync(ForOrganization(HttpMethod.Patch, "/organizations/current", alice.OrganizationId, new { name = "Bob's now" }));

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal("Member", (await read.Content.ReadFromJsonAsync<OrganizationDetails>())!.Role);
            Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        }

        [Fact]
        public async Task Query_filter_hides_tenant_data_when_no_organization_is_selected()
        {
            var alice = await CreateUserAsync("Alice Inc");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // No current organization: fail closed, nothing is visible.
            Assert.Empty(await db.AuditEntries.ToListAsync());

            // With Alice's organization selected, only Alice's entries are visible.
            scope.ServiceProvider.GetRequiredService<CurrentOrganizationContext>().Set(alice.OrganizationId, MembershipRole.Owner);
            var visible = await db.AuditEntries.ToListAsync();
            Assert.NotEmpty(visible);
            Assert.All(visible, e => Assert.Equal(alice.OrganizationId, e.OrganizationId));

            // IgnoreQueryFilters shows everything - only for deliberate admin/maintenance code.
            Assert.True(await db.AuditEntries.IgnoreQueryFilters().CountAsync() >= visible.Count);
        }

        [Fact]
        public async Task Writing_another_organizations_data_is_refused()
        {
            var alice = await CreateUserAsync("Alice Inc");
            var bob = await CreateUserAsync("Bob LLC");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope.ServiceProvider.GetRequiredService<CurrentOrganizationContext>().Set(bob.OrganizationId, MembershipRole.Owner);

            db.AuditEntries.Add(new AuditEntry(alice.OrganizationId, bob.UserId, "sneaky", null, null));

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }
}