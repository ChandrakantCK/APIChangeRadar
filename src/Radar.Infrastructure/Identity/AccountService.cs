using Microsoft.AspNetCore.Identity;
using Radar.Application.Accounts;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Persistence;


namespace Radar.Infrastructure.Identity
{
    internal sealed class AccountService(AppDbContext db, UserManager<ApplicationUser> userManager) : IAccountService
    {
        private const string DefaultOrganizationName = "My workspace";

        public async Task<SignUpResult> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return SignUpResult.Failure(new SignUpError("Required", "Email and password are required."));
            }

            // Validate everything we can BEFORE touching the database.
            Organization organization;
            try
            {
                organization = new Organization(
                    string.IsNullOrWhiteSpace(request.OrganizationName) ? DefaultOrganizationName : request.OrganizationName);
            }
            catch (ArgumentException ex)
            {
                return SignUpResult.Failure(new SignUpError("InvalidOrganizationName", ex.Message));
            }

            var email = request.Email.Trim();
            var user = new ApplicationUser { Email = email, UserName = email };

            // One transaction: user + organization + membership are saved together or not at all.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                // Returning without CommitAsync: disposing the transaction rolls it back.
                return SignUpResult.Failure(created.Errors.Select(e => new SignUpError(e.Code, e.Description)).ToArray());
            }

            db.Organizations.Add(organization);
            db.Memberships.Add(new Membership(organization.Id, user.Id, MembershipRole.Owner));
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return SignUpResult.Success(user.Id, organization.Id);
        }
    }
}
