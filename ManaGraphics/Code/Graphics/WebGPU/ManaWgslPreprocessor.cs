using System.Text;
using System.Text.RegularExpressions;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Resolves `#include "other.wgsl"` before source reaches wgpu, so shared declarations (the frame and
    /// object uniform structs above all) live in one file instead of being copy-pasted into every shader.
    /// Paths resolve relative to the including file; each file is included at most once per module.
    /// </summary>
    internal static partial class ManaWgslPreprocessor
    {
        [GeneratedRegex("^[ \\t]*#include[ \\t]+\"([^\"]+)\"[ \\t]*\\r?$", RegexOptions.Multiline)]
        private static partial Regex IncludeDirective();

        public static string LoadResolved(string absolutePath)
        {
            var builder = new StringBuilder();
            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Append(builder, absolutePath, included);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string absolutePath, HashSet<string> included)
        {
            var fullPath = Path.GetFullPath(absolutePath);
            if (!included.Add(fullPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var source = File.ReadAllText(fullPath);
            var lastIndex = 0;

            foreach (Match match in IncludeDirective().Matches(source))
            {
                builder.Append(source, lastIndex, match.Index - lastIndex);

                var includePath = Path.Combine(directory, match.Groups[1].Value);
                if (!File.Exists(includePath))
                {
                    throw new FileNotFoundException(
                        $"WGSL include '{match.Groups[1].Value}' referenced by '{fullPath}' was not found at '{includePath}'.");
                }

                Append(builder, includePath, included);
                lastIndex = match.Index + match.Length;
            }

            builder.Append(source, lastIndex, source.Length - lastIndex);
            builder.AppendLine();
        }
    }
}
