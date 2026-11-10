using System.Text;
using Radar.Diffing.Normalization;

namespace Radar.Diffing.Tests.Normalization
{
    public class SpecNormalizerTests
    {
        private static NormalizedSpec Normalize(string json) => SpecNormalizer.Normalize(Encoding.UTF8.GetBytes(json));

        private static string Canonical(string json) => Encoding.UTF8.GetString(Normalize(json).CanonicalJson);

        [Fact]
        public void Keys_are_sorted_and_whitespace_removed()
        {
            const string json = """
                {
                  "paths": { "/b": {}, "/a": {} },
                  "openapi": "3.1.0",
                  "info": { "version": "1.0", "title": "Demo" }
                }
                """;

            Assert.Equal("""{"info":{"title":"Demo","version":"1.0"},"openapi":"3.1.0","paths":{"/a":{},"/b":{}}}""", Canonical(json));
        }

        [Fact]
        public void Formatting_and_key_order_do_not_change_the_hash()
        {
            var compact = Normalize("""{"openapi":"3.0.3","info":{"title":"A","version":"1"},"paths":{}}""");
            var pretty = Normalize("""
                {
                    "paths" : { },
                    "info" : { "version" : "1", "title" : "A" },
                    "openapi" : "3.0.3"
                }
                """);

            Assert.Equal(compact.Hash, pretty.Hash);
        }

        [Fact]
        public void A_real_change_changes_the_hash()
        {
            var before = Normalize("""{"openapi":"3.0.3","paths":{"/a":{}}}""");
            var after = Normalize("""{"openapi":"3.0.3","paths":{"/a":{},"/b":{}}}""");

            Assert.NotEqual(before.Hash, after.Hash);
        }

        [Fact]
        public void Array_order_is_kept()
        {
            Assert.Equal("""{"enum":["b","a"],"openapi":"3.0.0"}""", Canonical("""{"openapi":"3.0.0","enum":["b","a"]}"""));
        }

        [Fact]
        public void Numbers_are_kept_exactly_as_written()
        {
            Assert.Equal(
                """{"big":12345678901234567890,"decimal":1.10,"openapi":"3.0.0"}""",
                Canonical("""{"openapi":"3.0.0","decimal":1.10,"big":12345678901234567890}"""));
        }

        [Fact]
        public void Escaped_and_plain_text_normalize_to_the_same_bytes()
        {
            Assert.Equal(
                Normalize("""{"openapi":"3.0.0","info":{"title":"Café"}}""").Hash,
                Normalize("""{"openapi":"3.0.0","info":{"title":"Café"}}""").Hash);
        }

        [Theory]
        [InlineData("""{"openapi":"3.1.0","info":{"version":"2024-06-20"}}""", "OpenAPI 3.1.0", "2024-06-20")]
        [InlineData("""{"swagger":"2.0","info":{"version":"v1"}}""", "Swagger 2.0", "v1")]
        [InlineData("""{"openapi":"3.0.0"}""", "OpenAPI 3.0.0", null)]
        public void Spec_and_api_versions_are_read(string json, string specVersion, string? apiVersion)
        {
            var spec = Normalize(json);

            Assert.Equal(specVersion, spec.SpecVersion);
            Assert.Equal(apiVersion, spec.ApiVersion);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1, 2, 3]")]
        [InlineData("""{"title":"no version field"}""")]
        public void Non_openapi_documents_are_rejected(string json)
        {
            Assert.Throws<SpecNormalizationException>(() => Normalize(json));
        }

        [Fact]
        public void Hash_is_lowercase_sha256_hex()
        {
            Assert.Matches("^[0-9a-f]{64}$", Normalize("""{"openapi":"3.0.0"}""").Hash);
        }
    }
}
