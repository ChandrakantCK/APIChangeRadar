using Radar.Domain.Changes;

namespace Radar.Diffing.Changes
{
    /// <summary>
    /// One difference between two versions of an API description.
    /// </summary>
    /// <param name="RuleId">Stable id of the rule that found it, for example "response-field-removed".</param>
    /// <param name="Method">HTTP method, upper case.</param>
    /// <param name="Path">The endpoint's path as written in the newer version (older, if removed).</param>
    /// <param name="Location">Where inside the endpoint, for example "response 200 `data[].id`"; empty for the endpoint itself.</param>
    /// <param name="Message">One sentence for people.</param>
    public sealed record SpecChange(
        string RuleId,
        ChangeSeverity Severity,
        string Method,
        string Path,
        string Location,
        string Message);
}
