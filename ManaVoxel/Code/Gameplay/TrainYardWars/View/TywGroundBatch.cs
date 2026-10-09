using Mana;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaVoxel.Code.GridMaps;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>
    /// Gathers a stretch of static ground -- kit tiles, props, drawn extras -- into one mesh per texture. A world of
    /// hexes is thousands of little models; drawn one by one (and again for every shadow cascade) they cost more
    /// than everything else in the frame together.
    /// </summary>
    internal sealed class TywGroundBatch
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

        /// <summary>Adds every part of a loaded model, placed by <paramref name="world"/>.</summary>
        /// <param name="tint">Multiplies the model's colours: one shade per tile keeps a field of kit tiles from reading as wallpaper.</param>
        public void Add(GridMapPrototype prototype, Matrix4x4 world, Vector4? tint = null)
        {
            foreach (var part in prototype.Parts)
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
                var normal = i < cpu.Normals.Length ? Vector3.TransformNormal(cpu.Normals[i], world) : Vector3.UnitY;
                group.Normals.Add(normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY);
                group.Colors.Add((i < cpu.Colors.Length ? cpu.Colors[i] : Vector4.One) * shade);
                group.Uv0.Add(i < cpu.TextureCoordinates0.Length ? cpu.TextureCoordinates0[i] : Vector2.Zero);
                group.Uv1.Add(i < cpu.TextureCoordinates1.Length ? cpu.TextureCoordinates1[i] : Vector2.Zero);
            }

            foreach (var index in cpu.Indexes)
            {
                group.Indexes.Add(start + index);
            }
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

        /// <summary>One node per texture, ready to add to the scene.</summary>
        public List<ManaSceneNode> Build(ManaContext context, string name)
        {
            var nodes = new List<ManaSceneNode>();
            foreach (var group in _groups.Values.Where(g => g.Indexes.Count > 0))
            {
                var template = group.Template!;
                var mesh = new ManaMesh(context)
                {
                    Name = name,
                    Materials = new[] { group.Material! }
                };
                mesh.SetCpuData(new ManaMeshCpuData
                {
                    Indexes = group.Indexes.ToArray(),
                    Positions = group.Positions.ToArray(),
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
                mesh.SetSize(group.Min, group.Max);
                var node = new ManaMeshSceneNode(context, mesh) { Name = name, IsEditorTransient = true };
                node.MeshRenderer.Material = group.Material!;
                nodes.Add(node);
            }

            return nodes;
        }
    }
}
