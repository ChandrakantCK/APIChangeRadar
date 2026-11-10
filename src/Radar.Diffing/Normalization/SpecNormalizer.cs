using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Radar.Diffing.Normalization
{
    /// <summary>A spec rewritten in canonical form, plus what it says about itself.</summary>
    public sealed record NormalizedSpec(
        byte[] CanonicalJson,
        string Hash,
        string SpecVersion,
        string? ApiVersion);

    public sealed class SpecNormalizationException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>
    /// Rewrites an OpenAPI/Swagger JSON document in one canonical form: object keys sorted, no whitespace,
    /// strings re-escaped the same way, numbers kept exactly as written. Two files that differ only in
    /// formatting or key order produce identical bytes, so the same hash: "nothing really changed".
    /// Array order is kept, because in JSON it can carry meaning.
    /// </summary>
    public static class SpecNormalizer
    {
        private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

        private static readonly JsonWriterOptions WriteOptions = new()
        {
            Indented = false,
            // Keep non-ASCII text readable and compact; any escaping is fine as long as it's always the same.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = 256,
        };

        public static NormalizedSpec Normalize(ReadOnlyMemory<byte> json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, ReadOptions);
            }
            catch (JsonException ex)
            {
                throw new SpecNormalizationException($"Not valid JSON: {ex.Message}", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new SpecNormalizationException("The document is not a JSON object.");
                }

                // OpenAPI 3.x says "openapi": "3.x.y"; the older Swagger 2.0 says "swagger": "2.0".
                var specVersion = ReadString(root, "openapi") is { } openApi ? $"OpenAPI {openApi}"
                    : ReadString(root, "swagger") is { } swagger ? $"Swagger {swagger}"
                    : throw new SpecNormalizationException("Not an OpenAPI document: no \"openapi\" or \"swagger\" field.");

                var apiVersion = root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
                    ? ReadString(info, "version")
                    : null;

                using var output = new MemoryStream(json.Length);
                using (var writer = new Utf8JsonWriter(output, WriteOptions))
                {
                    WriteCanonical(writer, root);
                }

                var canonical = output.ToArray();
                return new NormalizedSpec(canonical, Convert.ToHexStringLower(SHA256.HashData(canonical)), specVersion, apiVersion);
            }
        }

        private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    // Ordinal: the same order on every machine and culture.
                    foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonical(writer, property.Value);
                    }

                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteCanonical(writer, item);
                    }

                    writer.WriteEndArray();
                    break;

                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;

                case JsonValueKind.Number:
                    // As written: parsing to double could turn 1.10 into 1.1 or lose digits of big integers.
                    writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    writer.WriteBooleanValue(element.GetBoolean());
                    break;

                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        private static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
