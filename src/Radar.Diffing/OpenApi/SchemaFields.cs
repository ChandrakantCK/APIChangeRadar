using System.Text.Json;

namespace Radar.Diffing.OpenApi
{
    /// <param name="Type">"string", "integer", "object"... ; "string|integer" for OpenAPI 3.1 type lists; null when unknown (anyOf/oneOf).</param>
    /// <param name="Enum">Allowed values as JSON text; empty when any value is allowed.</param>
    internal sealed record FieldInfo(string? Type, bool Required, IReadOnlySet<string> Enum);

    /// <summary>
    /// Flattens a schema into dotted field paths: "customer.email", "data[].id" ("[]" = each array item).
    /// Comparing two such dictionaries is much simpler than comparing two schema trees.
    /// </summary>
    internal static class SchemaFields
    {
        // Deep enough for real APIs, small enough that huge specs stay fast.
        private const int MaxDepth = 8;
        private const int MaxAllOfDepth = 5;

        private static readonly IReadOnlySet<string> NoEnum = new HashSet<string>();

        public static Dictionary<string, FieldInfo> Collect(OpenApiDocumentView document, JsonElement? schema)
        {
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            if (schema is { } root)
            {
                Walk(document, root, string.Empty, fields, new HashSet<string>(StringComparer.Ordinal), depth: 0);
            }

            return fields;
        }

        public static FieldInfo Describe(OpenApiDocumentView document, JsonElement schema, bool required)
        {
            var resolved = document.Resolve(schema);
            if (resolved.ValueKind != JsonValueKind.Object)
            {
                return new FieldInfo(null, required, NoEnum);
            }

            var values = resolved.TryGetProperty("enum", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(v => v.GetRawText()).ToHashSet(StringComparer.Ordinal)
                : NoEnum;

            return new FieldInfo(TypeOf(resolved), required, values);
        }

        /// <summary>"a.b" → "a", "a[]" → "a", "a[].b" → "a[]", "a" → "".</summary>
        public static string ParentOf(string path)
        {
            if (path.EndsWith("[]", StringComparison.Ordinal))
            {
                return path[..^2];
            }

            var dot = path.LastIndexOf('.');
            return dot < 0 ? string.Empty : path[..dot];
        }

        private static void Walk(
            OpenApiDocumentView document, JsonElement schema, string prefix,
            Dictionary<string, FieldInfo> fields, HashSet<string> refsOnPath, int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            schema = document.Resolve(schema, out var reference);
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // A schema that contains itself (a comment with replies, a repository with a parent repository)
            // would recurse forever: stop when a reference repeats on the current path.
            if (reference is not null && !refsOnPath.Add(reference))
            {
                return;
            }

            try
            {
                var (properties, required) = PropertiesOf(document, schema);
                foreach (var (name, propertySchema) in properties)
                {
                    var path = prefix.Length == 0 ? name : $"{prefix}.{name}";
                    fields[path] = Describe(document, propertySchema, required.Contains(name));
                    Walk(document, propertySchema, path, fields, refsOnPath, depth + 1);
                }

                if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                {
                    var path = prefix + "[]";
                    fields[path] = Describe(document, items, required: true);
                    Walk(document, items, path, fields, refsOnPath, depth + 1);
                }
            }
            finally
            {
                if (reference is not null)
                {
                    refsOnPath.Remove(reference);
                }
            }
        }

        // Own properties plus those of every allOf member ("Customer = BaseObject + these fields").
        // anyOf / oneOf are alternatives, not a merge, so v1 does not look inside them.
        private static (Dictionary<string, JsonElement> Properties, HashSet<string> Required) PropertiesOf(
            OpenApiDocumentView document, JsonElement schema)
        {
            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);
            Merge(schema, 0);
            return (properties, required);

            void Merge(JsonElement current, int depth)
            {
                if (depth > MaxAllOfDepth || current.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                if (current.TryGetProperty("properties", out var own) && own.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in own.EnumerateObject())
                    {
                        properties[property.Name] = property.Value;
                    }
                }

                if (current.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array)
                {
                    foreach (var name in names.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String))
                    {
                        required.Add(name.GetString()!);
                    }
                }

                if (current.TryGetProperty("allOf", out var members) && members.ValueKind == JsonValueKind.Array)
                {
                    foreach (var member in members.EnumerateArray())
                    {
                        Merge(document.Resolve(member), depth + 1);
                    }
                }
            }
        }

        private static string? TypeOf(JsonElement schema)
        {
            if (schema.TryGetProperty("type", out var type))
            {
                if (type.ValueKind == JsonValueKind.String)
                {
                    return type.GetString();
                }

                // OpenAPI 3.1: "type": ["string", "null"]. Nullability is not a type change in v1.
                if (type.ValueKind == JsonValueKind.Array)
                {
                    var names = type.EnumerateArray()
                        .Where(t => t.ValueKind == JsonValueKind.String && t.GetString() != "null")
                        .Select(t => t.GetString()!)
                        .Order(StringComparer.Ordinal)
                        .ToList();
                    return names.Count == 0 ? null : string.Join('|', names);
                }
            }

            if (schema.TryGetProperty("properties", out _) || schema.TryGetProperty("allOf", out _))
            {
                return "object";
            }

            return schema.TryGetProperty("items", out _) ? "array" : null;
        }
    }
}
