using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Reads the doc comment written above a property, so a reflected inspector can explain a field the
    /// way Unreal's tooltips do.
    /// <para>
    /// The comments are not in the assembly -- the compiler puts them in a sibling XML file, which is
    /// emitted only when a project asks for it. A project that does not is not an error here; its
    /// properties simply get no tooltip.
    /// </para>
    /// <para>
    /// Loaded once per assembly and cached. The file for a large project is a few hundred kilobytes, and
    /// parsing it on the frame a node is selected would be felt.
    /// </para>
    /// </summary>
    public static class XmlDocSummaries
    {
        private static readonly Dictionary<Assembly, Dictionary<string, string>?> Loaded = new();

        /// <summary>The summary for a property, or null when there is none to be had.</summary>
        public static string? For(PropertyInfo property)
        {
            var declaring = property.DeclaringType;
            if (declaring == null)
            {
                return null;
            }

            var members = MembersFor(declaring.Assembly);
            if (members == null)
            {
                return null;
            }

            // Walk up the hierarchy: an inherited property is documented where it was declared, and the
            // key names that type rather than the one being inspected.
            for (var type = declaring; type != null; type = type.BaseType)
            {
                var key = $"P:{type.FullName}.{property.Name}";
                if (members.TryGetValue(key, out var summary))
                {
                    return summary;
                }
            }

            return null;
        }

        private static Dictionary<string, string>? MembersFor(Assembly assembly)
        {
            if (Loaded.TryGetValue(assembly, out var cached))
            {
                return cached;
            }

            var members = Load(assembly);
            Loaded[assembly] = members;
            return members;
        }

        private static Dictionary<string, string>? Load(Assembly assembly)
        {
            try
            {
                if (string.IsNullOrEmpty(assembly.Location))
                {
                    return null;
                }

                var path = Path.ChangeExtension(assembly.Location, ".xml");
                if (!File.Exists(path))
                {
                    return null;
                }

                var document = XDocument.Load(path);
                var members = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var member in document.Descendants("member"))
                {
                    var name = member.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name) || !name.StartsWith("P:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var summary = member.Element("summary");
                    if (summary == null)
                    {
                        continue;
                    }

                    var text = Flatten(summary);
                    if (text.Length > 0)
                    {
                        members[name] = text;
                    }
                }

                return members;
            }
            catch (Exception)
            {
                // A malformed or unreadable doc file costs tooltips, not the inspector.
                return null;
            }
        }

        /// <summary>
        /// Turns the summary's mixed content into plain text. A doc comment is XML: it carries
        /// &lt;para&gt; breaks and &lt;see cref="..."/&gt; references that have no text of their own, and
        /// reading Value alone would run the sentences together and drop the cross-references entirely.
        /// </summary>
        private static string Flatten(XElement summary)
        {
            var text = new StringBuilder();
            Append(summary, text);

            // The source wraps these at eighty columns, so the line breaks are an artefact of the file
            // rather than the author's intent. Paragraphs are kept; wrapping is not.
            var lines = text.ToString()
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(line => line.Trim());

            var joined = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    // One break between paragraphs, however many the markup produced. A <para> emits a
                    // break on each side, so two in a row would otherwise leave a gap of three.
                    if (joined.Length > 0 && joined[^1] != '\n')
                    {
                        joined.Append('\n');
                    }

                    continue;
                }

                if (joined.Length > 0 && joined[^1] != '\n')
                {
                    joined.Append(' ');
                }

                joined.Append(line);
            }

            return joined.ToString().Trim();
        }

        private static bool IsReference(string name)
        {
            return name is "see" or "seealso" or "paramref" or "typeparamref";
        }

        private static void Append(XElement element, StringBuilder text)
        {
            foreach (var node in element.Nodes())
            {
                switch (node)
                {
                    case XText content:
                        text.Append(content.Value);
                        break;

                    case XElement child when child.Name.LocalName == "para":
                        text.Append('\n');
                        Append(child, text);
                        text.Append('\n');
                        break;

                    case XElement child when IsReference(child.Name.LocalName):
                        // Carries its target in an attribute rather than as text. "T:Mana.ManaTexture"
                        // is not something to show, so it is trimmed to the name.
                        var reference = child.Attribute("cref")?.Value
                            ?? child.Attribute("name")?.Value
                            ?? string.Empty;

                        var separator = reference.LastIndexOf('.');
                        text.Append(separator >= 0 ? reference[(separator + 1)..] : reference.TrimStart('T', 'P', 'M', ':'));
                        break;

                    case XElement child:
                        Append(child, text);
                        break;
                }
            }
        }
    }
}
