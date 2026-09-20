using System.Reflection;
using Mana;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaGraphics.Code.Diagnostics;

namespace ManaVoxel.Code.UI.ManaUi
{
    internal sealed class SceneComponentDefinition
    {
        public required string Label { get; init; }
        public required string Description { get; init; }
        public required Type ComponentType { get; init; }
        public bool AllowMultiple { get; init; }
        public required Func<ManaSceneNode, ManaComponent> Create { get; init; }

        public bool CanAdd(ManaSceneNode node) =>
            AllowMultiple || !node.Components.Any(ComponentType.IsInstanceOfType);

        public override string ToString() => Label;
    }

    internal static class SceneComponentCatalog
    {
        private static readonly object CatalogLock = new();
        private static readonly HashSet<Assembly> Scanned = new();
        private static readonly List<SceneComponentDefinition> Discovered = new();
        private static IReadOnlyList<SceneComponentDefinition> _definitions = Array.Empty<SceneComponentDefinition>();

        /// <summary>Cached discovery, including component assemblies loaded after editor startup.</summary>
        public static IReadOnlyList<SceneComponentDefinition> Definitions
        {
            get
            {
                lock (CatalogLock)
                {
                    var changed = false;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (assembly.IsDynamic || !Scanned.Add(assembly)) continue;
                        foreach (var type in LoadableTypes(assembly))
                        {
                            if (!type.IsVisible || type.IsAbstract || type.ContainsGenericParameters
                                || !type.IsSubclassOf(typeof(ManaComponent))) continue;

                            var policy = type.GetCustomAttribute<EditorComponentAttribute>();
                            if (policy?.CanAdd == false) continue;
                            var constructor = type.GetConstructor(new[] { typeof(ManaSceneNode) });
                            if (constructor == null) continue;

                            Discovered.Add(new SceneComponentDefinition
                            {
                                Label = ComponentDisplayName.For(type),
                                Description = $"Add {type.FullName} ({assembly.GetName().Name}).",
                                ComponentType = type,
                                AllowMultiple = policy?.AllowMultiple == true,
                                Create = node => (ManaComponent)constructor.Invoke(new object[] { node })
                            });
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        _definitions = Discovered.OrderBy(d => d.Label, StringComparer.Ordinal)
                            .ThenBy(d => d.ComponentType.FullName, StringComparer.Ordinal).ToArray();
                    }
                    return _definitions;
                }
            }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
            catch (NotSupportedException) { return Array.Empty<Type>(); }
        }

        public static ManaComponent? AddToNode(
            ManaSceneNode node,
            SceneComponentDefinition definition,
            SceneSelectionState? selection,
            EditorUndoHistory? history = null)
        {
            var before = selection == null ? (EditorSelectionSnapshot?)null : EditorSelectionSnapshot.Capture(selection);
            ManaComponent component;
            var world = node.Context.World;
            lock (world.SceneLock)
            {
                if (!definition.CanAdd(node)) return null;
                try
                {
                    component = definition.Create(node);
                }
                catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException)
                {
                    ManaConsole.WriteDiagnostic($"Could not add {definition.Label}: {ex.InnerException?.Message ?? ex.Message}");
                    return null;
                }
                node.Components.Add(component);
                if (world.IsPlaying) component.BeginPlay();
            }

            if (selection != null)
            {
                selection.SelectedScene = null;
                selection.SelectedNode = node;
                selection.SelectedComponent = component;
                selection.NotifySceneGraphChanged();
            }

            history?.RecordComponentInsertion(component, selection, before);
            return component;
        }
    }
}
