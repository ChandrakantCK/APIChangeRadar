using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Radar.Api.Endpoints;
using Radar.Infrastructure.Identity;

namespace Radar.Integration.Tests.Infrastructure
{
    public static class TestAccounts
    {
        public const string ValidPassword = "correct horse battery staple";

        /// <summary>Signs up a verified user with their own organization and returns a client that carries their token.</summary>
        public static async Task<HttpClient> CreateSignedInClientAsync(WebApplicationFactory<Program> app, string organizationName = "Test org")
        {
            var client = app.CreateClient();
            var email = $"user-{Guid.NewGuid():N}@example.com";
            (await client.PostAsJsonAsync("/auth/signup", new { email, password = ValidPassword, organizationName })).EnsureSuccessStatusCode();

            using (var scope = app.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                await userManager.ConfirmEmailAsync(user!, await userManager.GenerateEmailConfirmationTokenAsync(user!));
            }

            var login = await client.PostAsJsonAsync("/auth/login", new { email, password = ValidPassword });
            var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}
