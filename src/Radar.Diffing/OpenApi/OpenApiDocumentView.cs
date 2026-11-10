using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radar.Diffing.OpenApi
{
    internal sealed record OperationView(string Method, string Path, JsonElement Operation, JsonElement PathItem);

    /// <summary>
    /// Read-only helpers over a parsed OpenAPI document: its operations, and following "$ref" pointers.
    /// </summary>
    internal sealed partial class OpenApiDocumentView
    {
        private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

        private readonly JsonElement _root;
        private readonly Dictionary<string, HashSet<string>> _directRefsByTarget = new(StringComparer.Ordinal);

        public OpenApiDocumentView(JsonElement root)
        {
            _root = root;
            Operations = ReadOperations(root);
        }

        /// <summary>Keyed by "METHOD /path/{}" so that renaming a path parameter is not a new endpoint.</summary>
        public IReadOnlyDictionary<string, OperationView> Operations { get; }

        [GeneratedRegex("{[^}]*}")]
        private static partial Regex PathParameter();

        public static string NormalizePath(string path) => PathParameter().Replace(path, "{}");

        /// <summary>The path parameter names in order: "/a/{x}/b/{y}" gives ["x", "y"].</summary>
        public static List<string> PathParameterNames(string path) =>
            PathParameter().Matches(path).Select(m => m.Value[1..^1]).ToList();

        /// <summary>Follows "$ref" (up to 10 hops). An unresolvable reference gives an undefined element.</summary>
        public JsonElement Resolve(JsonElement element) => Resolve(element, out _);

        public JsonElement Resolve(JsonElement element, out string? reference)
        {
            reference = null;
            for (var hops = 0; hops < 10; hops++)
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("$ref", out var pointer)
                    || pointer.ValueKind != JsonValueKind.String)
                {
                    return element;
                }

                reference = pointer.GetString()!;
                if (Find(reference) is not { } target)
                {
                    return default;
                }

                element = target;
            }

            return default;
        }

        /// <summary>The JSON text a reference points to, or null.</summary>
        public string? RawTextAt(string reference) => Find(reference)?.GetRawText();

        /// <summary>Every reference reachable from the element, following references through the document.</summary>
        public HashSet<string> ReachableRefs(JsonElement element)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(DirectRefs(element));
            while (pending.TryPop(out var reference))
            {
                if (!reachable.Add(reference))
                {
                    continue;
                }

                if (!_directRefsByTarget.TryGetValue(reference, out var next))
                {
                    next = Find(reference) is { } target ? DirectRefs(target) : [];
                    _directRefsByTarget[reference] = next;
                }

                foreach (var r in next)
                {
                    pending.Push(r);
                }
            }

            return reachable;
        }

        private static HashSet<string> DirectRefs(JsonElement element)
        {
            var refs = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<JsonElement>();
            pending.Push(element);
            while (pending.TryPop(out var current))
            {
                if (current.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in current.EnumerateObject())
                    {
                        if (property.Name == "$ref" && property.Value.ValueKind == JsonValueKind.String)
                        {
                            refs.Add(property.Value.GetString()!);
                        }
                        else
                        {
                            pending.Push(property.Value);
                        }
                    }
                }
                else if (current.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in current.EnumerateArray())
                    {
                        pending.Push(item);
                    }
                }
            }

            return refs;
        }

        // Only local references ("#/components/schemas/Customer"); "~1" and "~0" are JSON Pointer escapes for "/" and "~".
        private JsonElement? Find(string reference)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
            {
                return null;
            }

            var current = _root;
            foreach (var raw in reference[2..].Split('/'))
            {
                var segment = raw.Replace("~1", "/").Replace("~0", "~");
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }

            return current;
        }

        private static Dictionary<string, OperationView> ReadOperations(JsonElement root)
        {
            var operations = new Dictionary<string, OperationView>(StringComparer.Ordinal);
            if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            {
                return operations;
            }

            foreach (var path in paths.EnumerateObject())
            {
                if (path.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var method in Methods)
                {
                    if (path.Value.TryGetProperty(method, out var operation) && operation.ValueKind == JsonValueKind.Object)
                    {
                        var upper = method.ToUpperInvariant();
                        operations[$"{upper} {NormalizePath(path.Name)}"] = new OperationView(upper, path.Name, operation, path.Value);
                    }
                }
            }

            return operations;
        }
    }
}
