using System.Text;
using System.Text.RegularExpressions;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Resolves `#include "other.wgsl"` before source reaches wgpu, so shared declarations (the frame and
    /// object uniform structs above all) live in one file instead of being copy-pasted into every shader.
    /// Paths resolve relative to the including file; each file is included at most once per module.
    /// <para>
    /// Also supports a single conditional symbol, <c>MANA_BROWSER</c>, via
    /// <c>#if MANA_BROWSER</c> / <c>#else</c> / <c>#endif</c> -- deliberately not a general
    /// expression language, just enough to express the handful of places where the browser target
    /// genuinely needs different shader code than desktop (see hdr_tonemap.wgsl's surface encode).
    /// Nesting is not supported.
    /// </para>
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
            return ApplyConditionals(builder.ToString());
        }

        /// <summary>
        /// Strips `#if MANA_BROWSER` / `#else` / `#endif` blocks, keeping whichever branch matches
        /// the build. Runs after includes so an included file can carry conditionals too.
        /// </summary>
        private static string ApplyConditionals(string source)
        {
#if MANA_BROWSER
            const bool browser = true;
#else
            const bool browser = false;
#endif
            if (!source.Contains("#if MANA_BROWSER", StringComparison.Ordinal))
            {
                return source;
            }

            var output = new StringBuilder(source.Length);
            var keeping = true;
            var inConditional = false;

            foreach (var line in source.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#if MANA_BROWSER", StringComparison.Ordinal))
                {
                    inConditional = true;
                    keeping = browser;
                    continue;
                }

                if (inConditional && trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    keeping = !browser;
                    continue;
                }

                if (inConditional && trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    inConditional = false;
                    keeping = true;
                    continue;
                }

                if (keeping)
                {
                    output.Append(line).Append('\n');
                }
            }

            return output.ToString();
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
