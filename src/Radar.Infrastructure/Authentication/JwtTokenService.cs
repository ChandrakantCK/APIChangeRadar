using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Radar.Application.Abstractions;

namespace Radar.Infrastructure.Authentication
{
    internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
    {
        private readonly JsonWebTokenHandler _handler = new();

        public AccessToken CreateAccessToken(Guid userId, string email)
        {
            var settings = options.Value;
            var now = timeProvider.GetUtcNow();
            var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = settings.Issuer,
                Audience = settings.Audience,
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = expiresAt.UtcDateTime,
                Claims = new Dictionary<string, object>
                {
                    [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                    [JwtRegisteredClaimNames.Email] = email,
                    [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                },
                SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            };

            return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
        }
    }
}