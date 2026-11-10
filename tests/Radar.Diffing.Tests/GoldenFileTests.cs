using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Radar.Diffing.Changes;

namespace Radar.Diffing.Tests
{
    /// <summary>
    /// Each folder under Golden/ is one case: before.json, after.json and expected.json, the list of changes
    /// a person reviewed and agreed with. Adding a case = adding a folder; no code changes.
    /// When a test fails, the message shows the actual JSON: review it, and if it is right, paste it into expected.json.
    /// </summary>
    public class GoldenFileTests
    {
        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Golden");

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static TheoryData<string> Cases()
        {
            var cases = new TheoryData<string>();
            foreach (var directory in Directory.GetDirectories(Root).Order(StringComparer.Ordinal))
            {
                cases.Add(Path.GetFileName(directory));
            }

            return cases;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Differ_finds_exactly_the_expected_changes(string name)
        {
            var directory = Path.Combine(Root, name);

            var actual = SpecDiffer.Diff(
                File.ReadAllBytes(Path.Combine(directory, "before.json")),
                File.ReadAllBytes(Path.Combine(directory, "after.json")));

            var expected = JsonSerializer.Deserialize<List<SpecChange>>(File.ReadAllText(Path.Combine(directory, "expected.json")), Json)!;

            // Compare as text so a failure shows the whole actual list, ready to review.
            Assert.Equal(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(actual, Json));
        }

        [Fact]
        public void A_spec_compared_with_itself_has_no_changes()
        {
            foreach (var directory in Directory.GetDirectories(Root))
            {
                var after = File.ReadAllBytes(Path.Combine(directory, "after.json"));
                Assert.Empty(SpecDiffer.Diff(after, after));
            }
        }
    }
}
