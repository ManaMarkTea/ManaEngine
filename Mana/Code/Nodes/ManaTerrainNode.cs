using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana.Code.Nodes
{
    /// <summary>What a terrain brush does where it is dragged.</summary>
    public enum ManaTerrainBrush
    {
        Raise,
        Lower,
        Smooth,
        Flatten,
        Noise,
        Paint
    }

    /// <summary>One dab of a terrain brush, in the terrain's own space.</summary>
    public readonly record struct ManaTerrainDab(
        ManaTerrainBrush Brush,
        float X,
        float Z,
        float Radius,
        float Strength,
        float Falloff = 0.5f,
        float FlattenHeight = 0.0f,
        int Layer = 0);

    /// <summary>
    /// A heightfield: a grid of heights, <see cref="Spacing"/> metres apart and centred on the node, with four paint
    /// layers blended by a weight per sample. Drawn and collided as patches of <see cref="PatchCells"/> cells, so a
    /// brush rebuilds only the patches it touches. Patches are editor-transient children; a level saves the grid.
    /// </summary>
    public sealed class ManaTerrainNode : ManaSceneNode
    {
        public const int PatchCells = 32;
        public const int LayerCount = 4;
        public const int MinimumSamples = 2;
        public const int MaximumSamples = 1025;

        /// <summary>Grass, dirt, rock, sand.</summary>
        public static readonly Vector4[] DefaultLayerColors =
        [
            new(0.17f, 0.30f, 0.09f, 1.0f),
            new(0.34f, 0.22f, 0.12f, 1.0f),
            new(0.33f, 0.32f, 0.30f, 1.0f),
            new(0.66f, 0.58f, 0.40f, 1.0f)
        ];

        private float[] _heights = [];
        private byte[] _splat = [];
        private Patch[] _patches = [];
        private readonly ManaMaterial _material;

        private sealed class Patch
        {
            public required ManaMeshSceneNode Node;
            public ManaCollisionComponent? Body;
            public int Builds;
        }

        public ManaTerrainNode(ManaContext context, int samplesX = 129, int samplesZ = 129, float spacing = 1.0f)
            : base(context, Matrix4x4.Identity)
        {
            Name = "Terrain";
            LevelSource = ManaLevelSource.Terrain;
            _material = new ManaMaterial(context) { Name = "Terrain", Shader = new ManaPbrShader(context) };
            // The layers' colours are in the vertices; a material's default albedo is orange, which would tint them.
            _material.Albedo.Value = Vector4.One;
            _material.Emission.Value = new Vector4(0, 0, 0, 1);
            _material.Roughness.Value = 0.95f;
            _material.Metallic.Value = 0.0f;
            Load(samplesX, samplesZ, spacing, null, null, null);
        }

        public int SamplesX { get; private set; }
        public int SamplesZ { get; private set; }

        /// <summary>Metres between neighbouring samples.</summary>
        public float Spacing { get; private set; } = 1.0f;

        public float Width => (SamplesX - 1) * Spacing;
        public float Depth => (SamplesZ - 1) * Spacing;

        /// <summary>The paint layers' colours, in order; the splat weights blend them.</summary>
        public Vector4[] LayerColors { get; } = (Vector4[])DefaultLayerColors.Clone();

        public int PatchesX => (SamplesX - 2) / PatchCells + 1;
        public int PatchesZ => (SamplesZ - 2) / PatchCells + 1;

        /// <summary>Heights, row by row (x fastest). Change them through the brushes or <see cref="SetHeights"/>.</summary>
        public ReadOnlySpan<float> Heights => _heights;

        /// <summary>Four layer weights a sample (0-255), in the same order as <see cref="Heights"/>.</summary>
        public ReadOnlySpan<byte> Splat => _splat;

        /// <summary>The material every patch draws with.</summary>
        public ManaMaterial Material => _material;

        /// <summary>How many times a patch has been rebuilt -- what the tests read to see a brush stays local.</summary>
        public int PatchBuildCount(int patchX, int patchZ) => _patches[patchZ * PatchesX + patchX].Builds;

        /// <summary>The patch child nodes, for tests and the picker.</summary>
        public IEnumerable<ManaMeshSceneNode> PatchNodes => _patches.Select(patch => patch.Node);

        /// <summary>
        /// Replaces the whole grid. Missing or mis-sized heights start flat, missing splat starts on the first layer.
        /// </summary>
        public void Load(int samplesX, int samplesZ, float spacing, float[]? heights, byte[]? splat, Vector4[]? layers)
        {
            SamplesX = Math.Clamp(samplesX, MinimumSamples, MaximumSamples);
            SamplesZ = Math.Clamp(samplesZ, MinimumSamples, MaximumSamples);
            Spacing = MathF.Max(0.05f, spacing);
            var count = SamplesX * SamplesZ;
            _heights = new float[count];
            if (heights != null && heights.Length == count)
            {
                heights.CopyTo(_heights, 0);
            }

            _splat = new byte[count * LayerCount];
            if (splat != null && splat.Length == _splat.Length)
            {
                splat.CopyTo(_splat, 0);
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    _splat[i * LayerCount] = 255;
                }
            }

            if (layers != null)
            {
                for (var i = 0; i < Math.Min(layers.Length, LayerCount); i++)
                {
                    LayerColors[i] = layers[i];
                }
            }

            RebuildAll();
        }

        /// <summary>
        /// A new grid size and spacing, the old heights and paint resampled across it (the shape stretches with the
        /// terrain rather than being cut off).
        /// </summary>
        public void Resize(int samplesX, int samplesZ, float spacing)
        {
            samplesX = Math.Clamp(samplesX, MinimumSamples, MaximumSamples);
            samplesZ = Math.Clamp(samplesZ, MinimumSamples, MaximumSamples);
            spacing = MathF.Max(0.05f, spacing);
            if (samplesX == SamplesX && samplesZ == SamplesZ && spacing == Spacing)
            {
                return;
            }

            var heights = new float[samplesX * samplesZ];
            var splat = new byte[heights.Length * LayerCount];
            for (var z = 0; z < samplesZ; z++)
            {
                for (var x = 0; x < samplesX; x++)
                {
                    // Where this sample falls on the old grid, in old local metres.
                    var u = x / (float)(samplesX - 1);
                    var v = z / (float)(samplesZ - 1);
                    var i = z * samplesX + x;
                    heights[i] = HeightAt((u - 0.5f) * Width, (v - 0.5f) * Depth);
                    var nearest = (int)MathF.Round(v * (SamplesZ - 1)) * SamplesX + (int)MathF.Round(u * (SamplesX - 1));
                    for (var l = 0; l < LayerCount; l++)
                    {
                        splat[i * LayerCount + l] = _splat[nearest * LayerCount + l];
                    }
                }
            }

            Load(samplesX, samplesZ, spacing, heights, splat, null);
        }

        /// <summary>A copy of the grid, for undo.</summary>
        public (float[] Heights, byte[] Splat) CaptureState() => ((float[])_heights.Clone(), (byte[])_splat.Clone());

        /// <summary>Puts back a <see cref="CaptureState"/>, rebuilding only the patches that differ.</summary>
        public void RestoreState((float[] Heights, byte[] Splat) state)
        {
            if (state.Heights.Length != _heights.Length || state.Splat.Length != _splat.Length)
            {
                return;
            }

            var dirty = new bool[_patches.Length];
            for (var z = 0; z < SamplesZ; z++)
            {
                for (var x = 0; x < SamplesX; x++)
                {
                    var i = z * SamplesX + x;
                    var changed = _heights[i] != state.Heights[i];
                    for (var l = 0; l < LayerCount && !changed; l++)
                    {
                        changed = _splat[i * LayerCount + l] != state.Splat[i * LayerCount + l];
                    }

                    if (changed)
                    {
                        MarkAround(dirty, x, z);
                    }
                }
            }

            state.Heights.CopyTo(_heights, 0);
            state.Splat.CopyTo(_splat, 0);
            RebuildMarked(dirty);
        }

        /// <summary>Sets every height from a function of the local x, z (in metres from the centre).</summary>
        public void SetHeights(Func<float, float, float> height)
        {
            for (var z = 0; z < SamplesZ; z++)
            {
                for (var x = 0; x < SamplesX; x++)
                {
                    var local = SampleLocal(x, z);
                    _heights[z * SamplesX + x] = height(local.X, local.Y);
                }
            }

            RebuildAll();
        }

        /// <summary>Sets one sample's height; call <see cref="RebuildAll"/> (or use a brush) to see it.</summary>
        public float this[int x, int z]
        {
            get => _heights[Math.Clamp(z, 0, SamplesZ - 1) * SamplesX + Math.Clamp(x, 0, SamplesX - 1)];
            set => _heights[z * SamplesX + x] = value;
        }

        /// <summary>The height under a local x, z, between the samples as the triangles have it; edges clamp.</summary>
        public float HeightAt(float localX, float localZ)
        {
            var gx = Math.Clamp((localX + Width * 0.5f) / Spacing, 0.0f, SamplesX - 1);
            var gz = Math.Clamp((localZ + Depth * 0.5f) / Spacing, 0.0f, SamplesZ - 1);
            var x0 = Math.Min((int)gx, SamplesX - 2);
            var z0 = Math.Min((int)gz, SamplesZ - 2);
            var fx = gx - x0;
            var fz = gz - z0;
            var h00 = this[x0, z0];
            var h10 = this[x0 + 1, z0];
            var h01 = this[x0, z0 + 1];
            var h11 = this[x0 + 1, z0 + 1];
            // The quad is cut from (x+1, z) to (x, z+1), as the mesh is.
            if (fx + fz <= 1.0f)
            {
                return h00 + (h10 - h00) * fx + (h01 - h00) * fz;
            }

            return h11 + (h01 - h11) * (1.0f - fx) + (h10 - h11) * (1.0f - fz);
        }

        /// <summary>Whether a local x, z lies over the terrain.</summary>
        public bool Covers(float localX, float localZ) =>
            MathF.Abs(localX) <= Width * 0.5f && MathF.Abs(localZ) <= Depth * 0.5f;

        /// <summary>
        /// Where a local-space ray first meets the surface: marched in steps of half a sample, then halved down
        /// to the crossing. Null when it misses.
        /// </summary>
        public Vector3? Raycast(Vector3 origin, Vector3 direction, float maxDistance = 10000.0f)
        {
            if (direction.LengthSquared() < 1e-12f)
            {
                return null;
            }

            direction = Vector3.Normalize(direction);
            // Start where the ray enters the terrain's box, so a far camera does not march empty air.
            var half = new Vector3(Width * 0.5f, 0.0f, Depth * 0.5f);
            var (low, high) = HeightRange();
            if (!ClipToBox(origin, direction, new Vector3(-half.X, low - 1.0f, -half.Z), new Vector3(half.X, high + 1.0f, half.Z), out var enter, out var leave))
            {
                return null;
            }

            leave = MathF.Min(leave, maxDistance);
            var step = Spacing * 0.5f;
            var previous = MathF.Max(0.0f, enter);
            float Above(float t)
            {
                var p = origin + direction * t;
                return p.Y - HeightAt(p.X, p.Z);
            }

            if (Above(previous) < 0.0f)
            {
                return null;
            }

            for (var t = previous + step; t <= leave + step; t += step)
            {
                var at = MathF.Min(t, leave);
                if (Above(at) <= 0.0f)
                {
                    var a = previous;
                    var b = at;
                    for (var i = 0; i < 20; i++)
                    {
                        var mid = (a + b) * 0.5f;
                        if (Above(mid) > 0.0f)
                        {
                            a = mid;
                        }
                        else
                        {
                            b = mid;
                        }
                    }

                    return origin + direction * b;
                }

                previous = at;
            }

            return null;
        }

        /// <summary>
        /// Applies one dab: raise, lower, smooth, flatten, noise or paint within its radius, weighted by the falloff
        /// (0 is a hard disc, 1 fades from the centre). Rebuilds the patches it touched and returns how many.
        /// </summary>
        public int Apply(ManaTerrainDab dab)
        {
            var radius = MathF.Max(dab.Radius, Spacing * 0.5f);
            var minX = Math.Max(0, (int)MathF.Floor((dab.X - radius + Width * 0.5f) / Spacing));
            var maxX = Math.Min(SamplesX - 1, (int)MathF.Ceiling((dab.X + radius + Width * 0.5f) / Spacing));
            var minZ = Math.Max(0, (int)MathF.Floor((dab.Z - radius + Depth * 0.5f) / Spacing));
            var maxZ = Math.Min(SamplesZ - 1, (int)MathF.Ceiling((dab.Z + radius + Depth * 0.5f) / Spacing));
            if (minX > maxX || minZ > maxZ)
            {
                return 0;
            }

            // Smoothing pulls each sample toward the average around it, as the ground was before this dab (so it does not
            // smear one way) and over a reach that grows with the brush: a 3 x 3 average left a rounded hill as it was.
            float[]? blurred = null;
            if (dab.Brush == ManaTerrainBrush.Smooth)
            {
                blurred = Blurred(minX, maxX, minZ, maxZ, Math.Clamp((int)MathF.Round(radius * 0.35f / Spacing), 1, 24));
            }

            var dirty = new bool[_patches.Length];
            var layer = Math.Clamp(dab.Layer, 0, LayerCount - 1);
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var local = SampleLocal(x, z);
                    var distance = MathF.Sqrt((local.X - dab.X) * (local.X - dab.X) + (local.Y - dab.Z) * (local.Y - dab.Z));
                    var weight = Weight(distance / radius, dab.Falloff) * Math.Clamp(dab.Strength, 0.0f, 1.0f);
                    if (weight <= 0.0f)
                    {
                        continue;
                    }

                    var i = z * SamplesX + x;
                    switch (dab.Brush)
                    {
                        case ManaTerrainBrush.Raise:
                            _heights[i] += weight * radius * 0.1f;
                            break;
                        case ManaTerrainBrush.Lower:
                            _heights[i] -= weight * radius * 0.1f;
                            break;
                        case ManaTerrainBrush.Flatten:
                            _heights[i] += (dab.FlattenHeight - _heights[i]) * weight;
                            break;
                        case ManaTerrainBrush.Smooth:
                            _heights[i] += (blurred![(z - minZ) * (maxX - minX + 1) + (x - minX)] - _heights[i]) * Math.Min(1.0f, weight * 2.0f);
                            break;
                        case ManaTerrainBrush.Noise:
                            _heights[i] += (Hash(x, z) - 0.5f) * weight * radius * 0.1f;
                            break;
                        case ManaTerrainBrush.Paint:
                            PaintSample(i, layer, weight);
                            break;
                    }

                    MarkAround(dirty, x, z);
                }
            }

            return RebuildMarked(dirty);
        }

        /// <summary>Rebuilds every patch: after <see cref="Load"/>, or heights set one by one.</summary>
        public void RebuildAll()
        {
            var patchesX = PatchesX;
            var patchesZ = PatchesZ;
            if (_patches.Length != patchesX * patchesZ)
            {
                RemovePatches();
                _patches = new Patch[patchesX * patchesZ];
                for (var pz = 0; pz < patchesZ; pz++)
                {
                    for (var px = 0; px < patchesX; px++)
                    {
                        var mesh = new ManaMesh(Context) { Name = $"Terrain patch {px},{pz}", Materials = [_material] };
                        var node = new ManaMeshSceneNode(Context, mesh) { Name = $"Terrain patch {px},{pz}", IsEditorTransient = true };
                        node.MeshRenderer.Material = _material;
                        lock (Context.World.SceneLock)
                        {
                            AddChild(node);
                        }

                        _patches[pz * patchesX + px] = new Patch { Node = node };
                    }
                }
            }

            var all = new bool[_patches.Length];
            Array.Fill(all, true);
            RebuildMarked(all);
        }

        private int RebuildMarked(bool[] dirty)
        {
            var rebuilt = 0;
            for (var i = 0; i < dirty.Length; i++)
            {
                if (dirty[i])
                {
                    BuildPatch(i % PatchesX, i / PatchesX);
                    rebuilt++;
                }
            }

            return rebuilt;
        }

        // A sample's height reaches the normals of the cells around it, which can sit in the next patch over.
        private void MarkAround(bool[] dirty, int x, int z)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var sx = Math.Clamp(x + dx, 0, SamplesX - 1);
                    var sz = Math.Clamp(z + dz, 0, SamplesZ - 1);
                    var px = Math.Min(sx / PatchCells, PatchesX - 1);
                    var pz = Math.Min(sz / PatchCells, PatchesZ - 1);
                    dirty[pz * PatchesX + px] = true;
                    // A sample on a patch's left or near edge is the far edge of the patch before it too.
                    if (sx % PatchCells == 0 && sx > 0)
                    {
                        dirty[pz * PatchesX + Math.Min((sx - 1) / PatchCells, PatchesX - 1)] = true;
                    }

                    if (sz % PatchCells == 0 && sz > 0)
                    {
                        dirty[Math.Min((sz - 1) / PatchCells, PatchesZ - 1) * PatchesX + px] = true;
                    }
                }
            }
        }

        private void BuildPatch(int px, int pz)
        {
            var patch = _patches[pz * PatchesX + px];
            var x0 = px * PatchCells;
            var z0 = pz * PatchCells;
            var x1 = Math.Min(x0 + PatchCells, SamplesX - 1);
            var z1 = Math.Min(z0 + PatchCells, SamplesZ - 1);
            var columns = x1 - x0 + 1;
            var rows = z1 - z0 + 1;
            var positions = new Vector3[columns * rows];
            var normals = new Vector3[positions.Length];
            var colors = new Vector4[positions.Length];
            var uvs = new Vector2[positions.Length];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var v = (z - z0) * columns + (x - x0);
                    var local = SampleLocal(x, z);
                    var position = new Vector3(local.X, this[x, z], local.Y);
                    positions[v] = position;
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                    // Central differences over the whole grid, so the seams between patches shade as one.
                    var dx = (this[x + 1, z] - this[x - 1, z]) / ((Math.Min(x + 1, SamplesX - 1) - Math.Max(x - 1, 0)) * Spacing);
                    var dz = (this[x, z + 1] - this[x, z - 1]) / ((Math.Min(z + 1, SamplesZ - 1) - Math.Max(z - 1, 0)) * Spacing);
                    normals[v] = Vector3.Normalize(new Vector3(-dx, 1.0f, -dz));
                    colors[v] = SampleColor(z * SamplesX + x);
                    uvs[v] = new Vector2(local.X, local.Y);
                }
            }

            var indexes = new uint[(columns - 1) * (rows - 1) * 6];
            var triangles = new Vector3[indexes.Length];
            var n = 0;
            for (var z = 0; z < rows - 1; z++)
            {
                for (var x = 0; x < columns - 1; x++)
                {
                    var a = (uint)(z * columns + x);
                    var b = a + (uint)columns;
                    var c = a + 1;
                    var d = b + 1;
                    // Counter-clockwise seen from above: (x, z), (x, z+1), (x+1, z), then (x+1, z), (x, z+1), (x+1, z+1).
                    indexes[n++] = a; indexes[n++] = b; indexes[n++] = c;
                    indexes[n++] = c; indexes[n++] = b; indexes[n++] = d;
                }
            }

            for (var i = 0; i < indexes.Length; i++)
            {
                triangles[i] = positions[indexes[i]] * Scale;
            }

            var mesh = patch.Node.Mesh;
            mesh.SetCpuData(new ManaMeshCpuData
            {
                Indexes = indexes,
                Positions = positions,
                Normals = normals,
                Colors = colors,
                TextureCoordinates0 = uvs,
                TextureCoordinates1 = uvs
            });
            mesh.SetSize(min, max);
            patch.Builds++;
            RebuildCollider(patch, triangles);
        }

        private void RebuildCollider(Patch patch, Vector3[] triangles)
        {
            var physics = Context.Physics;
            if (physics == null)
            {
                return;
            }

            RemoveCollider(patch);
            // The body takes its pose from the patch's world transform, which follows the terrain's.
            patch.Node.UpdateTransform();
            patch.Body = physics.AddTriangleMeshBody(patch.Node, triangles);
            patch.Body.FollowsVisualBounds = false;
        }

        private void RemoveCollider(Patch patch)
        {
            if (patch.Body == null)
            {
                return;
            }

            var physics = Context.Physics;
            if (physics != null && physics.PhysicsMap.ContainsKey(patch.Body.Body))
            {
                physics.RemoveBody(patch.Body.Body);
            }

            lock (Context.World.SceneLock)
            {
                patch.Node.Components.Remove(patch.Body);
            }

            patch.Body = null;
        }

        private void RemovePatches()
        {
            foreach (var patch in _patches)
            {
                RemoveCollider(patch);
                lock (Context.World.SceneLock)
                {
                    RemoveChild(patch.Node);
                }
            }

            _patches = [];
        }

        private Vector2 SampleLocal(int x, int z) => new(x * Spacing - Width * 0.5f, z * Spacing - Depth * 0.5f);

        private (float Low, float High) HeightRange()
        {
            var low = float.MaxValue;
            var high = float.MinValue;
            foreach (var h in _heights)
            {
                low = MathF.Min(low, h);
                high = MathF.Max(high, h);
            }

            return (low, high);
        }

        private Vector4 SampleColor(int sample)
        {
            var color = Vector4.Zero;
            var total = 0.0f;
            for (var l = 0; l < LayerCount; l++)
            {
                var w = _splat[sample * LayerCount + l];
                color += LayerColors[l] * w;
                total += w;
            }

            if (total <= 0.0f)
            {
                return LayerColors[0];
            }

            return color / total;
        }

        // Moves weight toward one layer; the others give it up in proportion, so the four still add to 255.
        private void PaintSample(int sample, int layer, float weight)
        {
            var target = new float[LayerCount];
            for (var l = 0; l < LayerCount; l++)
            {
                var current = _splat[sample * LayerCount + l] / 255.0f;
                var goal = 0.0f;
                if (l == layer)
                {
                    goal = 1.0f;
                }
                target[l] = current + (goal - current) * weight;
            }

            var sum = target.Sum();
            var given = 0;
            for (var l = 0; l < LayerCount; l++)
            {
                var value = (int)MathF.Round(target[l] / sum * 255.0f);
                if (l == LayerCount - 1)
                {
                    value = 255 - given;
                }
                value = Math.Clamp(value, 0, 255);
                _splat[sample * LayerCount + l] = (byte)value;
                given += value;
            }
        }

        // The box average of the heights within `reach` samples of each one in the rectangle, edges clamped, from a
        // summed-area table over the rectangle and its margin: the same cost however wide the reach.
        private float[] Blurred(int minX, int maxX, int minZ, int maxZ, int reach)
        {
            var x0 = Math.Max(0, minX - reach);
            var x1 = Math.Min(SamplesX - 1, maxX + reach);
            var z0 = Math.Max(0, minZ - reach);
            var z1 = Math.Min(SamplesZ - 1, maxZ + reach);
            var width = x1 - x0 + 2;
            var table = new double[width * (z1 - z0 + 2)];
            for (var z = z0; z <= z1; z++)
            {
                var row = 0.0;
                for (var x = x0; x <= x1; x++)
                {
                    row += _heights[z * SamplesX + x];
                    table[(z - z0 + 1) * width + (x - x0 + 1)] = table[(z - z0) * width + (x - x0 + 1)] + row;
                }
            }

            var columns = maxX - minX + 1;
            var result = new float[columns * (maxZ - minZ + 1)];
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var left = Math.Max(x0, x - reach) - x0;
                    var right = Math.Min(x1, x + reach) - x0 + 1;
                    var top = Math.Max(z0, z - reach) - z0;
                    var bottom = Math.Min(z1, z + reach) - z0 + 1;
                    var sum = table[bottom * width + right] - table[top * width + right] - table[bottom * width + left] + table[top * width + left];
                    result[(z - minZ) * columns + (x - minX)] = (float)(sum / ((right - left) * (bottom - top)));
                }
            }
            return result;
        }

        // 1 at the centre to 0 at the rim; falloff 0 keeps it 1 all the way out, 1 fades from the middle (smoothstep).
        private static float Weight(float t, float falloff)
        {
            if (t >= 1.0f)
            {
                return 0.0f;
            }

            var inner = 1.0f - Math.Clamp(falloff, 0.0f, 1.0f);
            if (t <= inner)
            {
                return 1.0f;
            }

            var s = (t - inner) / (1.0f - inner);
            return 1.0f - s * s * (3.0f - 2.0f * s);
        }

        private static float Hash(int x, int z)
        {
            var h = (uint)(x * 374761393 + z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        private static bool ClipToBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float enter, out float leave)
        {
            enter = 0.0f;
            leave = float.MaxValue;
            for (var axis = 0; axis < 3; axis++)
            {
                var o = origin[axis];
                var d = direction[axis];
                if (MathF.Abs(d) < 1e-9f)
                {
                    if (o < min[axis] || o > max[axis])
                    {
                        return false;
                    }
                    continue;
                }

                var t0 = (min[axis] - o) / d;
                var t1 = (max[axis] - o) / d;
                if (t0 > t1)
                {
                    (t0, t1) = (t1, t0);
                }
                enter = MathF.Max(enter, t0);
                leave = MathF.Min(leave, t1);
                if (enter > leave)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
