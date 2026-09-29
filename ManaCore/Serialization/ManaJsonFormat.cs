using System.Text;
using System.Text.Json;

namespace ManaCore.Serialization
{
    /// <summary>
    /// Indents JSON for people to read. The stock indented writer puts every array element on a line of its
    /// own, so a position takes five lines; here an array of plain values that fits on a line stays on one --
    /// "position": [0, 1.5, 0] -- and everything else is indented as usual. Values are written exactly as they
    /// came in, so reformatting a file never changes a number.
    /// </summary>
    public static class ManaJsonFormat
    {
        /// <summary>Arrays longer than this on one line are laid out an element a line instead.</summary>
        private const int InlineWidth = 100;

        // Strings escaped only as JSON needs. The default writes '>' and '+' as six-character unicode escapes,
        // which is safe in a web page and unreadable in a file.
        private static readonly JsonSerializerOptions Text = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static string Indent(string json)
        {
            using var document = JsonDocument.Parse(json);
            var output = new StringBuilder(json.Length);
            Write(document.RootElement, output, 0);
            return output.ToString();
        }

        private static void Write(JsonElement element, StringBuilder output, int depth)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    WriteObject(element, output, depth);
                    break;
                case JsonValueKind.Array:
                    WriteArray(element, output, depth);
                    break;
                default:
                    output.Append(Plain(element));
                    break;
            }
        }

        private static void WriteObject(JsonElement element, StringBuilder output, int depth)
        {
            var properties = element.EnumerateObject().ToList();
            if (properties.Count == 0)
            {
                output.Append("{}");
                return;
            }

            output.Append('{').Append('\n');
            for (var i = 0; i < properties.Count; i++)
            {
                Pad(output, depth + 1);
                output.Append(JsonSerializer.Serialize(properties[i].Name, Text)).Append(": ");
                Write(properties[i].Value, output, depth + 1);
                output.Append(i < properties.Count - 1 ? ",\n" : "\n");
            }

            Pad(output, depth);
            output.Append('}');
        }

        private static void WriteArray(JsonElement element, StringBuilder output, int depth)
        {
            var items = element.EnumerateArray().ToList();
            if (items.Count == 0)
            {
                output.Append("[]");
                return;
            }

            // Plain values -- and arrays of plain values, like a list of pairs -- on one line when they fit.
            if (items.All(IsInline) && Inline(element) is { } line && line.Length + depth * 2 <= InlineWidth)
            {
                output.Append(line);
                return;
            }

            output.Append('[').Append('\n');
            for (var i = 0; i < items.Count; i++)
            {
                Pad(output, depth + 1);
                Write(items[i], output, depth + 1);
                output.Append(i < items.Count - 1 ? ",\n" : "\n");
            }

            Pad(output, depth);
            output.Append(']');
        }

        private static bool IsInline(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => false,
            JsonValueKind.Array => element.EnumerateArray().All(item => item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)),
            _ => true
        };

        private static string Inline(JsonElement element) => element.ValueKind == JsonValueKind.Array
            ? "[" + string.Join(", ", element.EnumerateArray().Select(Inline)) + "]"
            : Plain(element);

        /// <summary>A value as written -- numbers untouched -- with strings re-escaped readably.</summary>
        private static string Plain(JsonElement element) =>
            element.ValueKind == JsonValueKind.String ? JsonSerializer.Serialize(element.GetString(), Text) : element.GetRawText();

        private static void Pad(StringBuilder output, int depth) => output.Append(' ', depth * 2);
    }
}
