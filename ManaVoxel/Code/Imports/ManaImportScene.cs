using System.Text.Json.Nodes;

namespace ManaVoxel.Code.Imports
{
    /// <summary>A light worth placing where something glows: world position, its piece's size, a colour at most 1.</summary>
    public sealed record ManaImportGlow(Vector3 Position, float Size, Vector3 Colour);

    /// <summary>The scene-wide passes of an import: pivots, bounds, the authored camera and sun, and what glows.</summary>
    public static class ManaImportScene
    {
        /// <summary>
        /// Puts each mesh's origin at the middle of its geometry. Exported scenes often leave every part at the scene
        /// origin with its vertices in place (glTF has no FBX-style pivot), so a part would turn about a point metres
        /// away. Vertices shift by the mesh's bounds centre; each node using the mesh moves by as much in its parent's
        /// space and its children move back, so nothing moves in the world. Returns the meshes moved.
        /// </summary>
        public static int RecentrePivots(ManaGltfDocument gltf)
        {
            var owners = new Dictionary<int, HashSet<int>>();
            for (var mesh = 0; mesh < gltf.Meshes.Count; mesh++)
            {
                foreach (var primitive in Primitives(gltf, mesh))
                {
                    var position = primitive["attributes"]!["POSITION"]!.GetValue<int>();
                    if (!owners.TryGetValue(position, out var set))
                    {
                        owners[position] = set = new HashSet<int>();
                    }
                    set.Add(mesh);
                }
            }

            var centres = new Dictionary<int, Vector3>();
            for (var mesh = 0; mesh < gltf.Meshes.Count; mesh++)
            {
                var primitives = Primitives(gltf, mesh).ToList();
                var accessors = primitives.Select(p => p["attributes"]!["POSITION"]!.GetValue<int>()).Distinct().ToList();
                // Shared positions cannot move for one mesh alone; morph targets would need moving too.
                if (accessors.Count == 0 || accessors.Any(a => owners[a].Count > 1) || primitives.Any(p => p["targets"] != null)
                    || accessors.Any(a => gltf.Vec3Offset(a, out _) < 0))
                {
                    continue;
                }
                var low = new Vector3(float.MaxValue);
                var high = new Vector3(float.MinValue);
                foreach (var accessor in accessors)
                {
                    low = Vector3.Min(low, ManaGltfDocument.Vector(gltf.Accessors[accessor]!["min"], Vector3.Zero));
                    high = Vector3.Max(high, ManaGltfDocument.Vector(gltf.Accessors[accessor]!["max"], Vector3.Zero));
                }
                var centre = (low + high) * 0.5f;
                if (centre.Length() < 0.01f)
                {
                    continue;
                }

                foreach (var accessor in accessors)
                {
                    var offset = gltf.Vec3Offset(accessor, out var count);
                    var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(gltf.Buffer.AsSpan(offset, count * 12));
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);
                    for (var i = 0; i < count; i++)
                    {
                        floats[i * 3] -= centre.X;
                        floats[i * 3 + 1] -= centre.Y;
                        floats[i * 3 + 2] -= centre.Z;
                        var point = new Vector3(floats[i * 3], floats[i * 3 + 1], floats[i * 3 + 2]);
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }
                    // From the stored floats themselves: readers check the bounds against them exactly.
                    gltf.Accessors[accessor]!["min"] = ManaGltfDocument.ToArray(min);
                    gltf.Accessors[accessor]!["max"] = ManaGltfDocument.ToArray(max);
                }
                centres[mesh] = centre;
            }

            foreach (var node in gltf.Nodes)
            {
                if (node?["mesh"] is not { } meshNode || node["skin"] != null || !centres.TryGetValue(meshNode.GetValue<int>(), out var centre))
                {
                    continue;
                }
                var rotation = Quaternion.Identity;
                if (node["rotation"] is JsonArray r && r.Count == 4)
                {
                    rotation = new Quaternion(ManaGltfDocument.F(r[0]), ManaGltfDocument.F(r[1]), ManaGltfDocument.F(r[2]), ManaGltfDocument.F(r[3]));
                }
                var scale = ManaGltfDocument.Vector(node["scale"], Vector3.One);
                var translation = ManaGltfDocument.Vector(node["translation"], Vector3.Zero);
                node["translation"] = ManaGltfDocument.ToArray(translation + Vector3.Transform(scale * centre, rotation));
                if (node["children"] is JsonArray children)
                {
                    foreach (var child in children)
                    {
                        var childNode = gltf.Nodes[child!.GetValue<int>()]!;
                        childNode["translation"] = ManaGltfDocument.ToArray(ManaGltfDocument.Vector(childNode["translation"], Vector3.Zero) - centre);
                    }
                }
            }
            return centres.Count;
        }

        /// <summary>The scene's world box, from every mesh's accessor bounds through its node.</summary>
        public static (Vector3 Min, Vector3 Max) Bounds(ManaGltfDocument gltf)
        {
            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);
            foreach (var (index, world) in gltf.WorldMatrices())
            {
                if (gltf.Nodes[index]?["mesh"] is not { } mesh)
                {
                    continue;
                }
                foreach (var primitive in Primitives(gltf, mesh.GetValue<int>()))
                {
                    var accessor = gltf.Accessors[primitive["attributes"]!["POSITION"]!.GetValue<int>()]!;
                    var min = ManaGltfDocument.Vector(accessor["min"], Vector3.Zero);
                    var max = ManaGltfDocument.Vector(accessor["max"], Vector3.Zero);
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var placed = Vector3.Transform(Corner(min, max, corner), world);
                        low = Vector3.Min(low, placed);
                        high = Vector3.Max(high, placed);
                    }
                }
            }
            if (low.X > high.X)
            {
                return (Vector3.Zero, Vector3.Zero);
            }
            return (low, high);
        }

        /// <summary>The file's own camera: where it stands and a point ten metres ahead, or null when it has none.</summary>
        public static (Vector3 Eye, Vector3 Look)? Camera(ManaGltfDocument gltf)
        {
            foreach (var (index, world) in gltf.WorldMatrices())
            {
                if (gltf.Nodes[index]?["camera"] == null)
                {
                    continue;
                }
                var forward = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, world));
                return (world.Translation, world.Translation + forward * 10.0f);
            }
            return null;
        }

        /// <summary>
        /// The first directional light (KHR_lights_punctual): its colour and the way it shines. FBX2glTF keeps the FBX
        /// light's axis, which leaves the node's -Z pointing at the light; its +Z is the way light travels.
        /// </summary>
        public static (Vector3 Colour, Vector3 Direction)? Sun(ManaGltfDocument gltf)
        {
            if (gltf.Root["extensions"]?["KHR_lights_punctual"]?["lights"] is not JsonArray lights)
            {
                return null;
            }
            foreach (var (index, world) in gltf.WorldMatrices())
            {
                if (gltf.Nodes[index]?["extensions"]?["KHR_lights_punctual"]?["light"] is not { } lightIndex)
                {
                    continue;
                }
                var light = lights[lightIndex.GetValue<int>()];
                if (light?["type"]?.GetValue<string>() != "directional")
                {
                    continue;
                }
                var colour = ManaGltfDocument.Vector(light["color"], Vector3.One);
                return (colour, Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world)));
            }
            return null;
        }

        /// <summary>
        /// One light per glowing thing. Each primitive of an emissive material is cut into its connected pieces (one
        /// string-light mesh holds dozens of bulbs), and pieces closer than 0.6 m join (a lamp's panes).
        /// </summary>
        public static List<ManaImportGlow> Glows(ManaGltfDocument gltf, IReadOnlyDictionary<int, Vector3> emissiveColours)
        {
            var pieces = new List<(Vector3 Centre, float Size, Vector3 Colour)>();
            foreach (var (index, world) in gltf.WorldMatrices())
            {
                if (gltf.Nodes[index]?["mesh"] is not { } mesh)
                {
                    continue;
                }
                foreach (var primitive in Primitives(gltf, mesh.GetValue<int>()))
                {
                    if (primitive["material"] is not { } material || !emissiveColours.TryGetValue(material.GetValue<int>(), out var colour)
                        || primitive["indices"] is not { } indices)
                    {
                        continue;
                    }
                    var positions = gltf.ReadVec3(primitive["attributes"]!["POSITION"]!.GetValue<int>());
                    if (positions.Length == 0)
                    {
                        continue;
                    }
                    for (var i = 0; i < positions.Length; i++)
                    {
                        positions[i] = Vector3.Transform(positions[i], world);
                    }
                    pieces.AddRange(Pieces(positions, gltf.ReadIndices(indices.GetValue<int>())).Select(p => (p.Centre, p.Size, colour)));
                }
            }

            var glows = new List<ManaImportGlow>();
            foreach (var piece in pieces.OrderByDescending(p => p.Size))
            {
                var near = glows.FindIndex(g => Vector3.Distance(g.Position, piece.Centre) < 0.6f);
                if (near >= 0)
                {
                    glows[near] = glows[near] with { Size = MathF.Max(glows[near].Size, piece.Size) };
                    continue;
                }
                glows.Add(new ManaImportGlow(piece.Centre, piece.Size, piece.Colour));
            }
            return glows;
        }

        // Connected pieces of a triangle list: split vertices weld at the millimetre, then union-find over the triangles.
        private static IEnumerable<(Vector3 Centre, float Size)> Pieces(Vector3[] positions, uint[] triangles)
        {
            var weld = new Dictionary<(long, long, long), int>();
            var welded = new int[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                var key = ((long)MathF.Round(positions[i].X * 1000), (long)MathF.Round(positions[i].Y * 1000), (long)MathF.Round(positions[i].Z * 1000));
                if (!weld.TryGetValue(key, out var id))
                {
                    weld[key] = id = weld.Count;
                }
                welded[i] = id;
            }
            var parents = Enumerable.Range(0, weld.Count).ToArray();
            int Find(int item)
            {
                while (parents[item] != item)
                {
                    parents[item] = parents[parents[item]];
                    item = parents[item];
                }
                return item;
            }
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (triangles[t] >= welded.Length || triangles[t + 1] >= welded.Length || triangles[t + 2] >= welded.Length)
                {
                    continue;
                }
                var a = Find(welded[triangles[t]]);
                parents[Find(welded[triangles[t + 1]])] = a;
                parents[Find(welded[triangles[t + 2]])] = a;
            }

            var groups = new Dictionary<int, (Vector3 Sum, int Count, Vector3 Min, Vector3 Max)>();
            for (var i = 0; i < positions.Length; i++)
            {
                var root = Find(welded[i]);
                groups.TryGetValue(root, out var group);
                if (group.Count == 0)
                {
                    group.Min = new Vector3(float.MaxValue);
                    group.Max = new Vector3(float.MinValue);
                }
                groups[root] = (group.Sum + positions[i], group.Count + 1, Vector3.Min(group.Min, positions[i]), Vector3.Max(group.Max, positions[i]));
            }
            return groups.Values.Select(g => (g.Sum / g.Count, Vector3.Distance(g.Min, g.Max)));
        }

        /// <summary>One of a box's eight corners: bit 0 picks max x, bit 1 max y, bit 2 max z.</summary>
        public static Vector3 Corner(Vector3 min, Vector3 max, int corner)
        {
            var point = min;
            if ((corner & 1) != 0)
            {
                point.X = max.X;
            }
            if ((corner & 2) != 0)
            {
                point.Y = max.Y;
            }
            if ((corner & 4) != 0)
            {
                point.Z = max.Z;
            }
            return point;
        }

        public static IEnumerable<JsonNode> Primitives(ManaGltfDocument gltf, int mesh)
        {
            if (gltf.Meshes[mesh]?["primitives"] is not JsonArray primitives)
            {
                yield break;
            }
            foreach (var primitive in primitives)
            {
                if (primitive?["attributes"]?["POSITION"] != null)
                {
                    yield return primitive;
                }
            }
        }
    }
}
