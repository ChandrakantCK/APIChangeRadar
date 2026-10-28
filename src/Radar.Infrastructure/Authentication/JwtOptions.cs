using Microsoft.IdentityModel.Tokens;

namespace Radar.Infrastructure.Authentication
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        /// <summary>Who issued the token (this API).</summary>
        public string Issuer { get; set; } = string.Empty;

        /// <summary>Who the token is for (our web app).</summary>
        public string Audience { get; set; } = string.Empty;

        /// <summary>Base64 secret of at least 32 bytes. Never commit it: user-secrets locally, env var in production.</summary>
        public string SigningKey { get; set; } = string.Empty;

        public int AccessTokenMinutes { get; set; } = 15;

        public bool HasValidSigningKey()
        {
            try
            {
                return Convert.FromBase64String(SigningKey).Length >= 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public SymmetricSecurityKey CreateSigningKey() => new(Convert.FromBase64String(SigningKey));
    }
}