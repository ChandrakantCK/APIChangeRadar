using System.Text.Json;
using Radar.Diffing.Changes;
using Radar.Diffing.OpenApi;
using Radar.Domain.Changes;

namespace Radar.Diffing
{
    /// <summary>
    /// Compares two versions of an OpenAPI 3.x document and lists what changed, endpoint by endpoint.
    /// v1 looks at: endpoints, deprecation, parameters, request bodies and success (2xx) responses,
    /// following $ref and merging allOf. It does not look inside anyOf/oneOf, headers, security or examples.
    /// </summary>
    public static class SpecDiffer
    {
        private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

        public static IReadOnlyList<SpecChange> Diff(ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after)
        {
            using var beforeJson = JsonDocument.Parse(before, ReadOptions);
            using var afterJson = JsonDocument.Parse(after, ReadOptions);
            var old = new OpenApiDocumentView(beforeJson.RootElement);
            var current = new OpenApiDocumentView(afterJson.RootElement);

            var changes = new List<SpecChange>();
            var referenceChanged = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (var (key, operation) in old.Operations)
            {
                if (!current.Operations.ContainsKey(key))
                {
                    changes.Add(new SpecChange("endpoint-removed", ChangeSeverity.Breaking, operation.Method, operation.Path, string.Empty,
                        "Endpoint removed."));
                }
            }

            foreach (var (key, operation) in current.Operations)
            {
                if (!old.Operations.TryGetValue(key, out var previous))
                {
                    changes.Add(new SpecChange("endpoint-added", ChangeSeverity.Info, operation.Method, operation.Path, string.Empty,
                        "Endpoint added."));
                    continue;
                }

                // Shortcut for big specs: an operation whose own text and every schema it uses are identical can't have changed.
                if (IsUntouched(old, previous, current, operation, referenceChanged))
                {
                    continue;
                }

                new OperationComparer(old, previous, current, operation, changes).Compare();
            }

            return changes
                .OrderBy(c => c.Path, StringComparer.Ordinal)
                .ThenBy(c => c.Method, StringComparer.Ordinal)
                .ThenBy(c => c.Severity)
                .ThenBy(c => c.RuleId, StringComparer.Ordinal)
                .ThenBy(c => c.Location, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsUntouched(
            OpenApiDocumentView old, OperationView previous,
            OpenApiDocumentView current, OperationView operation,
            Dictionary<string, bool> referenceChanged)
        {
            if (previous.Operation.GetRawText() != operation.Operation.GetRawText()
                || PathParameters(previous) != PathParameters(operation))
            {
                return false;
            }

            var references = current.ReachableRefs(operation.Operation);
            references.UnionWith(old.ReachableRefs(previous.Operation));
            if (operation.PathItem.TryGetProperty("parameters", out var shared))
            {
                references.UnionWith(current.ReachableRefs(shared));
            }

            foreach (var reference in references)
            {
                if (!referenceChanged.TryGetValue(reference, out var changed))
                {
                    changed = old.RawTextAt(reference) != current.RawTextAt(reference);
                    referenceChanged[reference] = changed;
                }

                if (changed)
                {
                    return false;
                }
            }

            return true;

            static string? PathParameters(OperationView view) =>
                view.PathItem.TryGetProperty("parameters", out var parameters) ? parameters.GetRawText() : null;
        }

        private sealed record ParameterInfo(string Name, string In, bool Required, FieldInfo Field);

        private sealed class OperationComparer(
            OpenApiDocumentView oldDocument, OperationView previous,
            OpenApiDocumentView newDocument, OperationView operation,
            List<SpecChange> changes)
        {
            public void Compare()
            {
                CompareDeprecation();
                CompareParameters();
                CompareRequestBody();
                CompareResponses();
            }

            private void Add(string ruleId, ChangeSeverity severity, string location, string message) =>
                changes.Add(new SpecChange(ruleId, severity, operation.Method, operation.Path, location, message));

            private void CompareDeprecation()
            {
                if (IsDeprecated(operation.Operation) && !IsDeprecated(previous.Operation))
                {
                    Add("endpoint-deprecated", ChangeSeverity.Warning, string.Empty, "Endpoint deprecated.");
                }

                static bool IsDeprecated(JsonElement op) =>
                    op.TryGetProperty("deprecated", out var flag) && flag.ValueKind == JsonValueKind.True;
            }

            private void CompareParameters()
            {
                var before = Parameters(oldDocument, previous);
                var after = Parameters(newDocument, operation);

                foreach (var (key, parameter) in after)
                {
                    var location = $"{parameter.In} parameter `{parameter.Name}`";
                    if (!before.TryGetValue(key, out var old))
                    {
                        if (parameter.Required)
                        {
                            Add("parameter-required-added", ChangeSeverity.Breaking, location, $"New required {location}: existing requests don't send it.");
                        }
                        else
                        {
                            Add("parameter-optional-added", ChangeSeverity.Info, location, $"New optional {location}.");
                        }

                        continue;
                    }

                    if (parameter.Required && !old.Required)
                    {
                        Add("parameter-became-required", ChangeSeverity.Breaking, location, $"The {location} is now required.");
                    }

                    CompareType("parameter-type-changed", location, old.Field, parameter.Field);
                    CompareRequestEnum("parameter", location, old.Field, parameter.Field);
                }

                foreach (var (key, parameter) in before)
                {
                    if (!after.ContainsKey(key))
                    {
                        var location = $"{parameter.In} parameter `{parameter.Name}`";
                        Add("parameter-removed", ChangeSeverity.Warning, location, $"The {location} was removed: requests that send it may be rejected or it is ignored.");
                    }
                }
            }

            private void CompareRequestBody()
            {
                var before = SchemaFields.Collect(oldDocument, RequestSchema(oldDocument, previous));
                var after = SchemaFields.Collect(newDocument, RequestSchema(newDocument, operation));

                foreach (var (path, field) in after)
                {
                    var location = $"request body `{path}`";
                    if (!before.TryGetValue(path, out var old))
                    {
                        if (!IsTopMost(path, before))
                        {
                            continue;
                        }

                        if (field.Required)
                        {
                            Add("request-field-required-added", ChangeSeverity.Breaking, location, $"New required field `{path}` in the request body.");
                        }
                        else
                        {
                            Add("request-field-optional-added", ChangeSeverity.Info, location, $"New optional field `{path}` in the request body.");
                        }

                        continue;
                    }

                    if (field.Required && !old.Required)
                    {
                        Add("request-field-became-required", ChangeSeverity.Breaking, location, $"Request field `{path}` is now required.");
                    }

                    CompareType("request-field-type-changed", location, old, field);
                    CompareRequestEnum("request-field", location, old, field);
                }

                foreach (var path in before.Keys.Where(p => !after.ContainsKey(p) && IsTopMost(p, after)))
                {
                    Add("request-field-removed", ChangeSeverity.Warning, $"request body `{path}`",
                        $"Request field `{path}` was removed: requests that send it may be rejected or it is ignored.");
                }
            }

            private void CompareResponses()
            {
                var before = SuccessResponses(oldDocument, previous);
                var after = SuccessResponses(newDocument, operation);

                foreach (var status in before.Keys.Where(s => !after.ContainsKey(s)))
                {
                    Add("response-status-removed", ChangeSeverity.Breaking, $"response {status}", $"Success response {status} removed.");
                }

                foreach (var (status, schema) in after)
                {
                    if (!before.TryGetValue(status, out var oldSchema))
                    {
                        Add("response-status-added", ChangeSeverity.Info, $"response {status}", $"New success response {status}.");
                        continue;
                    }

                    CompareResponseFields(status, SchemaFields.Collect(oldDocument, oldSchema), SchemaFields.Collect(newDocument, schema));
                }
            }

            private void CompareResponseFields(string status, Dictionary<string, FieldInfo> before, Dictionary<string, FieldInfo> after)
            {
                foreach (var (path, field) in after)
                {
                    var location = $"response {status} `{path}`";
                    if (!before.TryGetValue(path, out var old))
                    {
                        if (IsTopMost(path, before))
                        {
                            Add("response-field-added", ChangeSeverity.Info, location, $"New field `{path}` in the {status} response.");
                        }

                        continue;
                    }

                    // Clients that read the field without checking for it break when it's missing.
                    if (old.Required && !field.Required)
                    {
                        Add("response-field-became-optional", ChangeSeverity.Breaking, location,
                            $"Field `{path}` in the {status} response is no longer always present.");
                    }

                    CompareType("response-field-type-changed", location, old, field);

                    // Clients that switch over the values may not handle a new one.
                    var added = field.Enum.Except(old.Enum).Order(StringComparer.Ordinal).ToList();
                    if (old.Enum.Count > 0 && field.Enum.Count > 0 && added.Count > 0)
                    {
                        Add("response-field-enum-value-added", ChangeSeverity.Warning, location,
                            $"Field `{path}` in the {status} response can now also be {string.Join(", ", added)}.");
                    }
                }

                foreach (var path in before.Keys.Where(p => !after.ContainsKey(p) && IsTopMost(p, after)))
                {
                    Add("response-field-removed", ChangeSeverity.Breaking, $"response {status} `{path}`",
                        $"Field `{path}` was removed from the {status} response.");
                }
            }

            private void CompareType(string ruleId, string location, FieldInfo before, FieldInfo after)
            {
                if (before.Type is not null && after.Type is not null && before.Type != after.Type)
                {
                    Add(ruleId, ChangeSeverity.Breaking, location, $"Type of {location} changed from {before.Type} to {after.Type}.");
                }
            }

            // Values the server used to accept and now rejects: some removed from the list, or a free value restricted to a list.
            private void CompareRequestEnum(string rulePrefix, string location, FieldInfo before, FieldInfo after)
            {
                if (after.Enum.Count == 0)
                {
                    return;
                }

                if (before.Enum.Count == 0)
                {
                    var allowed = after.Enum.Order(StringComparer.Ordinal).ToList();
                    var shown = string.Join(", ", allowed.Take(MaxValuesShown)) + (allowed.Count > MaxValuesShown ? $" and {allowed.Count - MaxValuesShown} more" : string.Empty);
                    Add($"{rulePrefix}-became-enum", ChangeSeverity.Breaking, location, $"The {location} now only accepts {shown}.");
                    return;
                }

                var removed = before.Enum.Except(after.Enum).Order(StringComparer.Ordinal).ToList();
                if (removed.Count > 0)
                {
                    Add($"{rulePrefix}-enum-value-removed", ChangeSeverity.Breaking, location, $"The {location} no longer accepts {string.Join(", ", removed)}.");
                }
            }

            private const int MaxValuesShown = 10;

            // Report only the outermost new/removed field: a removed "customer" object is one change, not one per sub-field.
            private static bool IsTopMost(string path, Dictionary<string, FieldInfo> other)
            {
                var parent = SchemaFields.ParentOf(path);
                return parent.Length == 0 || other.ContainsKey(parent);
            }
        }

        // Path-level parameters apply to every method; operation-level ones override them.
        // Path parameters are matched by position, so /users/{id} → /users/{user_id} is not "removed + added".
        private static Dictionary<string, ParameterInfo> Parameters(OpenApiDocumentView document, OperationView view)
        {
            var result = new Dictionary<string, ParameterInfo>(StringComparer.Ordinal);
            var pathNames = OpenApiDocumentView.PathParameterNames(view.Path);

            foreach (var source in new[] { view.PathItem, view.Operation })
            {
                if (!source.TryGetProperty("parameters", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in list.EnumerateArray())
                {
                    var parameter = document.Resolve(item);
                    if (parameter.ValueKind != JsonValueKind.Object
                        || !parameter.TryGetProperty("name", out var nameElement)
                        || !parameter.TryGetProperty("in", out var inElement))
                    {
                        continue;
                    }

                    var name = nameElement.GetString() ?? string.Empty;
                    var location = inElement.GetString() ?? string.Empty;
                    if (location == "body")
                    {
                        continue; // Swagger 2 request body: handled as the request body.
                    }

                    var position = location == "path" ? pathNames.IndexOf(name) : -1;
                    var key = position >= 0 ? $"path:#{position}"
                        : location == "header" ? $"header:{name.ToLowerInvariant()}"
                        : $"{location}:{name}";

                    var required = location == "path"
                        || (parameter.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True);

                    // OpenAPI 3: the type is in "schema"; Swagger 2: on the parameter itself.
                    var schema = parameter.TryGetProperty("schema", out var s) ? s : parameter;
                    result[key] = new ParameterInfo(name, location, required, SchemaFields.Describe(document, schema, required));
                }
            }

            return result;
        }

        private static JsonElement? RequestSchema(OpenApiDocumentView document, OperationView view)
        {
            if (view.Operation.TryGetProperty("requestBody", out var body))
            {
                return MediaSchema(document.Resolve(body));
            }

            // Swagger 2: a parameter "in": "body".
            if (view.Operation.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in parameters.EnumerateArray())
                {
                    var parameter = document.Resolve(item);
                    if (parameter.ValueKind == JsonValueKind.Object
                        && parameter.TryGetProperty("in", out var location) && location.GetString() == "body"
                        && parameter.TryGetProperty("schema", out var schema))
                    {
                        return schema;
                    }
                }
            }

            return null;
        }

        private static Dictionary<string, JsonElement?> SuccessResponses(OpenApiDocumentView document, OperationView view)
        {
            var result = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
            if (!view.Operation.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var response in responses.EnumerateObject().Where(r => r.Name.StartsWith('2')))
            {
                var resolved = document.Resolve(response.Value);
                result[response.Name.ToUpperInvariant()] = resolved.ValueKind != JsonValueKind.Object ? null
                    : resolved.TryGetProperty("schema", out var swagger2Schema) ? swagger2Schema
                    : MediaSchema(resolved);
            }

            return result;
        }

        // Prefer JSON; otherwise the first media type (Stripe's requests are form-encoded).
        private static JsonElement? MediaSchema(JsonElement holder)
        {
            if (holder.ValueKind != JsonValueKind.Object
                || !holder.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var media = content.TryGetProperty("application/json", out var json)
                ? json
                : content.EnumerateObject().Select(m => m.Value).FirstOrDefault();

            return media.ValueKind == JsonValueKind.Object && media.TryGetProperty("schema", out var schema) ? schema : null;
        }
    }
}
