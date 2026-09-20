using Mana;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A component's family tree, for the tooltip on its inspector header: the class itself, then each base class
    /// above it, stopping before <see cref="ManaComponent"/> -- every component is one, so listing it says nothing.
    /// Shows at a glance what a component is built on (a Hazard is a trigger), which the flat list of sections
    /// on a node does not.
    /// </summary>
    public static class ComponentInheritance
    {
        /// <summary>The type's own name first, then its bases in order, up to but not including ManaComponent.</summary>
        public static List<string> Chain(Type type)
        {
            var chain = new List<string>();
            for (var current = type; current != null && current != typeof(ManaComponent) && current != typeof(ManaObject) && current != typeof(object); current = current.BaseType)
            {
                chain.Add(current.Name);
            }

            return chain;
        }

        /// <summary>The chain as tooltip text, one class per line, each indented under the one it derives from.</summary>
        public static string Describe(Type type)
        {
            var chain = Chain(type);
            if (chain.Count == 0)
            {
                return type.Name;
            }

            var lines = new List<string> { chain[0] };
            for (var i = 1; i < chain.Count; i++)
            {
                lines.Add($"{new string(' ', i * 2)}\u21B3 {chain[i]}");
            }

            return string.Join("\n", lines);
        }
    }
}
