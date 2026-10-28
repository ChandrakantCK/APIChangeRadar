using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Radar.Infrastructure.Authentication;

namespace Radar.Api.Authentication
{
    public static class JwtAuthenticationExtensions
    {
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

            // Configured from JwtOptions lazily, so tools that only build the app (dotnet ef) don't need the key.
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
                {
                    var jwt = jwtOptions.Value;

                    // Keep claim names as they are in the token ("sub", "email").
                    bearer.MapInboundClaims = false;

                    bearer.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = jwt.Issuer,
                        ValidAudience = jwt.Audience,
                        IssuerSigningKey = jwt.CreateSigningKey(),

                        // Accept only the algorithm we sign with ("alg": "none" or RS/HS swaps are rejected).
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                        // Small tolerance for clock differences between machines.
                        ClockSkew = TimeSpan.FromSeconds(30),

                        NameClaimType = JwtRegisteredClaimNames.Sub,
                    };
                });

            services.AddAuthorization();

            return services;
        }
    }
}