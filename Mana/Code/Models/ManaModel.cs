using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Diagnostics;

namespace Mana.Code.Models
{
    /// <summary>A mesh of a loaded model, in the model's own space: what a batch merges and bounds are measured from.</summary>
    public sealed record ManaModelPart(string Name, ManaMesh Mesh, ManaMaterial Material, Matrix4x4 Transform);

    /// <summary>
    /// A model or prefab read once and placed many times: each copy keeps its tree (so a game can move a part by its
    /// name, or turn a group) and shares this model's meshes and materials, so a copy costs no GPU buffers.
    /// </summary>
    public sealed class ManaModel
    {
        /// <summary>
        /// Reads an asset -- a model (a repo path or a <c>zip:</c> pack path) or a prefab (<c>.manaprefab</c>, a content
        /// path) -- into a holding scene the world never draws. Mesh import is the app's, so the app sets it.
        /// </summary>
        public static Func<string, ManaContext, ManaScene, ManaSceneNode>? Loader { get; set; }

        /// <summary>
        /// A part named "Owner ..." is drawn in its player's colour: author it white (or grey, for a shade of it) and
        /// <see cref="ManaModelLibrary.Paint"/> it, or tint it as it is batched.
        /// </summary>
        public static bool IsOwnerPart(string name) => name.StartsWith("Owner", StringComparison.Ordinal);

        private ManaSceneNode? Source { get; init; }

        public string Name { get; private init; } = string.Empty;

        public IReadOnlyList<ManaModelPart> Parts { get; private init; } = [];

        public Vector3 BoundsMin { get; private init; }

        public Vector3 BoundsMax { get; private init; }

        public string? Error { get; private init; }

        public bool HasGeometry => Error == null && Parts.Count > 0;

        public static ManaModel Load(string asset, ManaContext context, Func<string, ManaContext, ManaScene, ManaSceneNode>? loader = null)
        {
            var name = Path.GetFileNameWithoutExtension(asset);
            loader ??= Loader;
            if (loader == null)
            {
                return new ManaModel { Name = name, Error = "no model loader (the app registers one)" };
            }

            try
            {
                return FromNode(name, loader(asset, context, new ManaScene(context) { Name = "Model Prototypes" }));
            }
            catch (Exception exception)
            {
                ManaLog.WriteDiagnostic($"Model '{asset}' could not be loaded: {exception.Message}");
                return new ManaModel { Name = name, Error = exception.Message };
            }
        }

        /// <summary>A model of an already-built tree. A tree holding a single node (a placed prefab's root) is that node.</summary>
        public static ManaModel FromNode(string name, ManaSceneNode root)
        {
            root.UpdateTransform();
            var source = root;
            if (root.Children.Count == 1 && !Renderers(root).Any())
            {
                source = root.Children[0];
            }

            Matrix4x4.Invert(root.WorldTransform, out var toRoot);
            var parts = new List<ManaModelPart>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            Collect(root);
            if (parts.Count == 0)
            {
                return new ManaModel { Name = name, Error = "nothing to draw" };
            }

            return new ManaModel { Name = name, Parts = parts, BoundsMin = min, BoundsMax = max, Source = source };

            void Collect(ManaSceneNode node)
            {
                foreach (var renderer in Renderers(node))
                {
                    var transform = node.WorldTransform * toRoot;
                    parts.Add(new ManaModelPart(node.Name, renderer.Mesh, renderer.Material, transform));
                    try
                    {
                        foreach (var position in renderer.Mesh.GetOrCreateCpuData().Positions)
                        {
                            var point = Vector3.Transform(position, transform);
                            min = Vector3.Min(min, point);
                            max = Vector3.Max(max, point);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // No CPU copy to measure; it still draws.
                    }
                }

                foreach (var child in node.Children)
                {
                    Collect(child);
                }
            }
        }

        /// <summary>A copy of the model's tree, placed by <paramref name="placement"/>, sharing its meshes and materials.</summary>
        public ManaSceneNode Instantiate(ManaContext context, string name, Matrix4x4 placement)
        {
            if (Source == null)
            {
                return new ManaSceneNode(context, Matrix4x4.Identity) { Name = name, IsEditorTransient = true };
            }

            var copy = Copy(context, Source);
            copy.Name = name;
            SetTransform(copy, Source.LocalTransform * placement);
            return copy;
        }

        /// <summary>Every node under <paramref name="root"/> (and itself) that draws a mesh.</summary>
        public static IEnumerable<ManaMeshSceneNode> MeshNodes(ManaSceneNode root)
        {
            if (root is ManaMeshSceneNode mesh)
            {
                yield return mesh;
            }
            foreach (var found in root.Children.SelectMany(MeshNodes))
            {
                yield return found;
            }
        }

        private static IEnumerable<MeshRendererComponent> Renderers(ManaSceneNode node) =>
            node.Components.OfType<MeshRendererComponent>().Where(renderer => renderer.Mesh != null);

        private static ManaSceneNode Copy(ManaContext context, ManaSceneNode node)
        {
            var renderers = Renderers(node).ToList();
            ManaSceneNode copy;
            if (renderers.Count > 0)
            {
                // The first mesh makes it a mesh node, as games expect; a node drawing several carries the rest too.
                var mesh = new ManaMeshSceneNode(context, renderers[0].Mesh);
                mesh.MeshRenderer.Material = renderers[0].Material;
                foreach (var extra in renderers.Skip(1))
                {
                    mesh.Components.Add(new MeshRendererComponent(mesh, extra.Mesh) { Material = extra.Material });
                }
                copy = mesh;
            }
            else
            {
                copy = new ManaSceneNode(context, Matrix4x4.Identity);
            }

            copy.Name = node.Name;
            copy.IsEditorTransient = true;
            SetTransform(copy, node.LocalTransform);
            foreach (var light in node.Components.OfType<ManaLightSourceComponent>())
            {
                copy.Components.Add(new ManaLightSourceComponent(copy)
                {
                    Color = light.Color, Intensity = light.Intensity, Range = light.Range, LightType = light.LightType, Direction = light.Direction,
                    Enabled = light.Enabled, CastsShadows = light.CastsShadows, FlickerAmount = light.FlickerAmount, FlickerSpeed = light.FlickerSpeed
                });
            }
            foreach (var child in node.Children)
            {
                copy.AddChild(Copy(context, child));
            }
            return copy;
        }

        // Position, rotation and scale rather than a fixed matrix, so a game can still move the part from there.
        private static void SetTransform(ManaSceneNode node, Matrix4x4 transform)
        {
            Matrix4x4.Decompose(transform, out var scale, out var rotation, out var position);
            node.Position = position;
            node.Rotation = rotation.ToEuler();
            node.Scale = scale;
            node.UpdateTransform();
        }
    }
}
