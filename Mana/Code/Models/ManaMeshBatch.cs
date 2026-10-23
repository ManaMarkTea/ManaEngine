using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana.Code.Models
{
    /// <summary>
    /// Merges a stretch of static scenery -- tiles, props, drawn extras -- into one mesh per texture. A world built of
    /// thousands of little models costs more drawn one by one (and again for every shadow cascade) than everything
    /// else in the frame together; merged, a stretch of it is a handful of draws.
    /// </summary>
    public sealed class ManaMeshBatch
    {
        private readonly Dictionary<string, Group> _groups = new();

        private sealed class Group
        {
            public ManaMaterial? Material;
            public ManaMeshCpuData? Template;
            public readonly List<Vector3> Positions = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector4> Colors = new();
            public readonly List<Vector2> Uv0 = new();
            public readonly List<Vector2> Uv1 = new();
            public readonly List<uint> Indexes = new();
            public Vector3 Min = new(float.MaxValue);
            public Vector3 Max = new(float.MinValue);
        }

        /// <summary>Whether anything has been added.</summary>
        public bool IsEmpty => _groups.Values.All(group => group.Indexes.Count == 0);

        /// <summary>Adds every part of a loaded model, placed by <paramref name="world"/>.</summary>
        /// <param name="tint">Multiplies the model's colours: a shade per copy keeps a field of the same tile from reading as wallpaper.</param>
        public void Add(ManaModel model, Matrix4x4 world, Vector4? tint = null)
        {
            foreach (var part in model.Parts)
            {
                Add(part.Mesh, part.Material, part.Transform * world, tint);
            }
        }

        public void Add(ManaMesh mesh, ManaMaterial material, Matrix4x4 world, Vector4? tint = null)
        {
            var shade = tint ?? Vector4.One;
            ManaMeshCpuData cpu;
            try
            {
                cpu = mesh.GetOrCreateCpuData();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var key = TextureKey(cpu, material);
            if (!_groups.TryGetValue(key, out var group))
            {
                group = new Group { Material = material, Template = cpu };
                _groups[key] = group;
            }

            var start = (uint)group.Positions.Count;
            for (var i = 0; i < cpu.Positions.Length; i++)
            {
                var position = Vector3.Transform(cpu.Positions[i], world);
                group.Positions.Add(position);
                group.Min = Vector3.Min(group.Min, position);
                group.Max = Vector3.Max(group.Max, position);
                var normal = Vector3.TransformNormal(At(cpu.Normals, i, Vector3.UnitY), world);
                if (normal.LengthSquared() > 1e-12f)
                {
                    normal = Vector3.Normalize(normal);
                }
                else
                {
                    normal = Vector3.UnitY;
                }
                group.Normals.Add(normal);
                group.Colors.Add(At(cpu.Colors, i, Vector4.One) * shade);
                group.Uv0.Add(At(cpu.TextureCoordinates0, i, Vector2.Zero));
                group.Uv1.Add(At(cpu.TextureCoordinates1, i, Vector2.Zero));
            }

            foreach (var index in cpu.Indexes)
            {
                group.Indexes.Add(start + index);
            }
        }

        /// <summary>One node per texture, ready to add to a scene, each named <paramref name="name"/> and its texture.</summary>
        public List<ManaSceneNode> Build(ManaContext context, string name)
        {
            var nodes = new List<ManaSceneNode>();
            foreach (var (key, group) in _groups.Where(g => g.Value.Indexes.Count > 0))
            {
                var template = group.Template!;
                // Whole-unit pivots avoid rounding drift when local vertices return to map coordinates.
                var pivot = RoundedPivot(group.Min, group.Max);
                var mesh = new ManaMesh(context)
                {
                    Name = name,
                    Materials = new[] { group.Material! }
                };
                mesh.SetCpuData(new ManaMeshCpuData
                {
                    Indexes = group.Indexes.ToArray(),
                    Positions = group.Positions.Select(p => p - pivot).ToArray(),
                    Normals = group.Normals.ToArray(),
                    Colors = group.Colors.ToArray(),
                    TextureCoordinates0 = group.Uv0.ToArray(),
                    TextureCoordinates1 = group.Uv1.ToArray(),
                    AlbedoTexturePath = template.AlbedoTexturePath,
                    AlbedoTextureBytes = template.AlbedoTextureBytes,
                    AlbedoFactor = template.AlbedoFactor,
                    NormalTexturePath = template.NormalTexturePath,
                    EmissionTexturePath = template.EmissionTexturePath,
                    MetallicTexturePath = template.MetallicTexturePath,
                    RoughnessTexturePath = template.RoughnessTexturePath,
                    OcclusionTexturePath = template.OcclusionTexturePath,
                    OpacityTexturePath = template.OpacityTexturePath,
                    NearestFiltering = template.NearestFiltering
                });
                mesh.SetSize(group.Min - pivot, group.Max - pivot);
                var node = new ManaMeshSceneNode(context, mesh) { Name = name + " " + key, IsEditorTransient = true, Position = pivot };
                node.SetLocalTransform(Matrix4x4.CreateTranslation(pivot));
                node.MeshRenderer.Material = group.Material!;
                nodes.Add(node);
            }

            return nodes;
        }

        /// <summary>A whole-unit point near the middle of a box, to place a merged mesh at.</summary>
        public static Vector3 RoundedPivot(Vector3 min, Vector3 max)
        {
            var center = (min + max) * 0.5f;
            return new Vector3(MathF.Round(center.X), MathF.Round(center.Y), MathF.Round(center.Z));
        }

        private static T At<T>(T[] values, int index, T fallback)
        {
            if (index < values.Length)
            {
                return values[index];
            }
            return fallback;
        }

        // Models from one pack load their shared colour map separately; they are still one texture to draw with.
        private static string TextureKey(ManaMeshCpuData cpu, ManaMaterial material)
        {
            if (cpu.AlbedoTexturePath is { } path)
            {
                return "path:" + path;
            }

            if (cpu.AlbedoTextureBytes is { } bytes)
            {
                var sum = 17L;
                for (var i = 0; i < bytes.Length; i += Math.Max(1, bytes.Length / 64))
                {
                    sum = sum * 31 + bytes[i];
                }

                return $"bytes:{bytes.Length}:{sum}";
            }

            return $"plain:{material.Unlit}:{material.Name}";
        }
    }
}
