using System.Text.Json.Nodes;
using ManaCore.Serialization;
using ManaUI;

namespace ManaTests;

/// <summary>
/// <see cref="ManaJsonFormat"/>: saved files a person can read -- a position on one line, not five -- without
/// a single value changing on the way.
/// </summary>
public static class JsonFormatChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        const string source = """
            {"name":"Coin","position":[0,1.5708,-2],"tags":["room","corridor"],"pairs":[["free","wall"],[">seat","table"]],
             "empty":[],"nothing":{},"children":[{"name":"A","scale":[1,1,1]}],"tiny":1e-05,
             "long":[1000001,1000002,1000003,1000004,1000005,1000006,1000007,1000008,1000009,1000010,1000011,1000012]}
            """;
        var formatted = ManaJsonFormat.Indent(source);
        var lines = formatted.Split('\n');

        check("reformatting changes no value", JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(formatted)), "");
        check("a short array of plain values stays on one line", lines.Any(line => line.Trim() == "\"position\": [0, 1.5708, -2],"), "");
        check("and so does a list of pairs", lines.Any(line => line.Trim() == "\"pairs\": [[\"free\", \"wall\"], [\">seat\", \"table\"]],"), "");
        check("numbers are written as they came, not re-rounded", formatted.Contains("1e-05", StringComparison.Ordinal), "");
        check("and a string's > is written as >, not \u003E", ManaJsonFormat.Indent("{\"a\":\"\u003Eseat\"}").Contains("\">seat\"", StringComparison.Ordinal), "");
        check("objects are still indented a field a line", lines.Any(line => line == "      \"name\": \"A\","), formatted);
        check("and an array too long for a line is laid out an element a line",
            lines.Count(line => line.Trim().StartsWith("1000", StringComparison.Ordinal)) == 12, "");
    }
}
