using System.Buffers.Text;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radar.Application.Abstractions;
using Radar.Application.Accounts;
using Radar.Domain.Organizations;
using Radar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Radar.Infrastructure.Identity
{
    internal sealed class AccountService(AppDbContext db, UserManager<ApplicationUser> userManager, IEmailSender emailSender,
        IOptions<AppOptions> appOptions, ILogger<AccountService> logger, ITokenService tokenService) : IAccountService
    {
        private const string DefaultOrganizationName = "My workspace";

        public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return LoginResult.Failure(LoginStatus.InvalidCredentials);
            }

            var user = await userManager.FindByEmailAsync(request.Email.Trim());
            if (user is null)
            {
                // Same answer as a wrong password: don't reveal which emails have accounts.
                return LoginResult.Failure(LoginStatus.InvalidCredentials);
            }

            if (await userManager.IsLockedOutAsync(user))
            {
                return LoginResult.Failure(LoginStatus.LockedOut);
            }

            if (!await userManager.CheckPasswordAsync(user, request.Password))
            {
                // Counts the failure; the 5th one in a row locks the account for 15 minutes.
                await userManager.AccessFailedAsync(user);

                return await userManager.IsLockedOutAsync(user)
                    ? LoginResult.Failure(LoginStatus.LockedOut)
                    : LoginResult.Failure(LoginStatus.InvalidCredentials);
            }

            await userManager.ResetAccessFailedCountAsync(user);

            // Checked only AFTER the password, so this answer can't be used to probe unknown accounts.
            if (!user.EmailConfirmed)
            {
                return LoginResult.Failure(LoginStatus.EmailNotConfirmed);
            }

            return LoginResult.Success(tokenService.CreateAccessToken(user.Id, user.Email!));
        }

        public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return null;
            }

            var organizations = await (
                    from membership in db.Memberships
                    join organization in db.Organizations on membership.OrganizationId equals organization.Id
                    where membership.UserId == userId
                    orderby organization.Name
                    select new { organization.Id, organization.Name, membership.Role })
                .ToListAsync(cancellationToken);

            return new UserProfile(
                user.Id,
                user.Email!,
                user.EmailConfirmed,
                organizations.Select(o => new OrganizationSummary(o.Id, o.Name, o.Role.ToString())).ToList());
        }

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
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
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
            }

            // The account exists now. If the email fails, sign-up still succeeded:
            // the user can ask for a new link. (Phase 3's outbox makes this fully reliable.)
            try
            {
                await SendVerificationEmailAsync(user, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not send the verification email for user {UserId}.", user.Id);
            }

            return SignUpResult.Success(user.Id, organization.Id);
        }

        public async Task<bool> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
        {
            var user = await userManager.FindByIdAsync(request.UserId.ToString());
            if (user is null || string.IsNullOrEmpty(request.Token))
            {
                return false;
            }

            string token;
            try
            {
                token = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(request.Token));
            }
            catch (FormatException)
            {
                return false;
            }

            var result = await userManager.ConfirmEmailAsync(user, token);
            return result.Succeeded;
        }

        public async Task ResendVerificationEmailAsync(ResendVerificationEmailRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return;
            }

            var user = await userManager.FindByEmailAsync(request.Email.Trim());

            // Unknown or already verified: do nothing, and let the caller answer the same way
            // either way, so the endpoint can't be used to find out which emails have accounts.
            if (user is null || user.EmailConfirmed)
            {
                return;
            }

            await SendVerificationEmailAsync(user, cancellationToken);
        }

        private async Task SendVerificationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            var rawToken = await userManager.GenerateEmailConfirmationTokenAsync(user);

            // Identity tokens contain '+', '/' and '='; Base64Url makes them safe inside a URL.
            var token = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(rawToken));
            var link = $"{appOptions.Value.WebBaseUrl.TrimEnd('/')}/verify-email?userId={user.Id}&token={token}";

            var text =
                "Welcome to API Change Radar!\n\n" +
                "Confirm your email address by opening this link:\n" +
                $"{link}\n\n" +
                "The link expires in 24 hours. If you did not sign up, you can ignore this email.";

            var html =
                "<p>Welcome to <strong>API Change Radar</strong>!</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Confirm your email address</a></p>" +
                "<p>The link expires in 24 hours. If you did not sign up, you can ignore this email.</p>";

            await emailSender.SendAsync(
                new EmailMessage(user.Email!, "Confirm your email for API Change Radar", text, html),
                cancellationToken);
        }
    }
}