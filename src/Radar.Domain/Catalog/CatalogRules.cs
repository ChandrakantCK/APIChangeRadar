using System.Text.RegularExpressions;

namespace Radar.Domain.Catalog
{
    internal static partial class CatalogRules
    {
        // Lowercase letters, digits and single dashes: "stripe", "api-v2010".
        [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
        private static partial Regex SlugPattern();

        public static string ValidateSlug(string slug, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > maxLength || !SlugPattern().IsMatch(slug))
            {
                throw new ArgumentException(
                    $"Slug must be 1-{maxLength} lowercase letters, digits or dashes.", nameof(slug));
            }

            return slug;
        }

        public static string ValidateRequired(string value, int maxLength, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A value is required.", paramName);
            }

            if (value.Trim().Length > maxLength)
            {
                throw new ArgumentException($"Must be at most {maxLength} characters.", paramName);
            }

            return value.Trim();
        }

        // Only https: the crawler will fetch these URLs, so no http://, file:// or other schemes.
        public static string ValidateHttpsUrl(string url, int maxLength, string paramName)
        {
            var value = ValidateRequired(url, maxLength, paramName);
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Must be an absolute https:// URL.", paramName);
            }

            return value;
        }
    }
}
