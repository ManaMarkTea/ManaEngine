#if !MANA_BROWSER
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ManaEditor.Code.Imports
{
    /// <summary>
    /// FBX to glTF in process, through ufbx (<c>ManaEditor/Native/Fbx</c>, built by <c>Tools/Native/build-mana-fbx.*</c>):
    /// the native side reads the file in metres, Y up, triangulated per material; this writes it out as the plain glTF
    /// the scene importer edits -- one node per FBX node under a <c>RootNode</c>, a primitive per material, the file's
    /// texture names on its materials, its cameras, and its lights (KHR_lights_punctual, shining down their node's -Z).
    /// </summary>
    public static class ManaFbxConverter
    {
        private const int FormatVersion = 1;

        [DllImport("mana_fbx", EntryPoint = "mana_fbx_read")]
        private static extern int Read([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr data, out nuint size, byte[] error, int errorSize);

        [DllImport("mana_fbx", EntryPoint = "mana_fbx_free")]
        private static extern void Free(IntPtr data);

        /// <summary>Converts <paramref name="fbx"/>; returns the .gltf it wrote (beside its buffer, in <c>&lt;stem&gt;_out/</c>).</summary>
        public static string Convert(string fbx, string outputFolder)
        {
            var bytes = ReadNative(fbx);
            var stem = Path.GetFileNameWithoutExtension(fbx);
            var folder = Path.Combine(outputFolder, stem + "_out");
            Directory.CreateDirectory(folder);
            var (root, buffer) = ToGltf(bytes, stem + ".bin");
            File.WriteAllBytes(Path.Combine(folder, stem + ".bin"), buffer);
            var gltf = Path.Combine(folder, stem + ".gltf");
            File.WriteAllText(gltf, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            return gltf;
        }

        private static byte[] ReadNative(string fbx)
        {
            var error = new byte[1024];
            if (Read(Path.GetFullPath(fbx), out var data, out var size, error, error.Length) == 0)
            {
                var message = Encoding.UTF8.GetString(error, 0, Math.Max(0, Array.IndexOf(error, (byte)0)));
                throw new InvalidDataException($"Could not read {Path.GetFileName(fbx)}: {message}");
            }
            try
            {
                var bytes = new byte[checked((int)size)];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                Free(data);
            }
        }

        /// <summary>The native buffer (see mana_fbx.c) as a glTF document and its binary buffer.</summary>
        public static (JsonObject Root, byte[] Buffer) ToGltf(byte[] native, string bufferUri)
        {
            using var reader = new BinaryReader(new MemoryStream(native), Encoding.UTF8);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MFBX" || reader.ReadUInt32() != FormatVersion)
            {
                throw new InvalidDataException("mana_fbx returned a buffer of another format version; rebuild the native library.");
            }

            var nodes = new List<(string Name, int Parent, Vector3 T, Quaternion R, Vector3 S, int Mesh, int Camera, int Light)>();
            var nodeCount = reader.ReadUInt32();
            for (var i = 0; i < nodeCount; i++)
            {
                var name = String(reader);
                var parent = reader.ReadInt32();
                var t = Vec3(reader);
                var r = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var s = Vec3(reader);
                nodes.Add((name, parent, t, r, s, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            }

            var binary = new MemoryStream();
            var bufferViews = new JsonArray();
            var accessors = new JsonArray();
            int AddAccessor(ReadOnlySpan<byte> data, int componentType, string type, int count, int target, JsonArray? min = null, JsonArray? max = null)
            {
                bufferViews.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = binary.Length, ["byteLength"] = data.Length, ["target"] = target });
                binary.Write(data);
                var accessor = new JsonObject { ["bufferView"] = bufferViews.Count - 1, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
                if (min != null && max != null)
                {
                    accessor["min"] = min;
                    accessor["max"] = max;
                }
                accessors.Add(accessor);
                return accessors.Count - 1;
            }

            var meshes = new JsonArray();
            var meshIndex = new List<int>();
            var meshCount = reader.ReadUInt32();
            for (var m = 0; m < meshCount; m++)
            {
                var name = String(reader);
                var primitives = new JsonArray();
                var parts = reader.ReadUInt32();
                for (var p = 0; p < parts; p++)
                {
                    var material = reader.ReadInt32();
                    var vertices = (int)reader.ReadUInt32();
                    var positions = reader.ReadBytes(vertices * 12);
                    var normals = reader.ReadBytes(vertices * 12);
                    var uvs = reader.ReadBytes(vertices * 8);
                    var indexCount = (int)reader.ReadUInt32();
                    var indices = reader.ReadBytes(indexCount * 4);
                    if (vertices == 0 || indexCount == 0)
                    {
                        continue;
                    }
                    var (low, high) = Bounds(positions);
                    var primitive = new JsonObject
                    {
                        ["attributes"] = new JsonObject
                        {
                            ["POSITION"] = AddAccessor(positions, 5126, "VEC3", vertices, 34962, ManaGltfDocument.ToArray(low), ManaGltfDocument.ToArray(high)),
                            ["NORMAL"] = AddAccessor(normals, 5126, "VEC3", vertices, 34962),
                            ["TEXCOORD_0"] = AddAccessor(uvs, 5126, "VEC2", vertices, 34962)
                        },
                        ["indices"] = AddAccessor(indices, 5125, "SCALAR", indexCount, 34963),
                        ["mode"] = 4
                    };
                    if (material >= 0)
                    {
                        primitive["material"] = material;
                    }
                    primitives.Add(primitive);
                }
                // glTF wants at least one primitive: a mesh with no triangles is left off its node.
                if (primitives.Count == 0)
                {
                    meshIndex.Add(-1);
                    continue;
                }
                meshes.Add(new JsonObject { ["name"] = name, ["primitives"] = primitives });
                meshIndex.Add(meshes.Count - 1);
            }

            var images = new JsonArray();
            var textures = new JsonArray();
            JsonObject? Texture(string file)
            {
                if (file.Length == 0)
                {
                    return null;
                }
                images.Add(new JsonObject { ["name"] = file, ["uri"] = Uri.EscapeDataString(file) });
                textures.Add(new JsonObject { ["source"] = images.Count - 1 });
                return new JsonObject { ["index"] = textures.Count - 1 };
            }

            var materials = new JsonArray();
            var materialCount = reader.ReadUInt32();
            for (var i = 0; i < materialCount; i++)
            {
                var name = String(reader);
                var colour = new JsonArray(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var baseColour = Texture(String(reader));
                var normal = Texture(String(reader));
                var pbr = new JsonObject { ["baseColorFactor"] = colour, ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.6 };
                if (baseColour != null)
                {
                    pbr["baseColorTexture"] = baseColour;
                }
                var material = new JsonObject { ["name"] = name, ["pbrMetallicRoughness"] = pbr };
                if (normal != null)
                {
                    material["normalTexture"] = normal;
                }
                materials.Add(material);
            }

            var cameras = new JsonArray();
            var cameraCount = reader.ReadUInt32();
            for (var i = 0; i < cameraCount; i++)
            {
                var yfov = reader.ReadSingle();
                var aspect = reader.ReadSingle();
                var near = reader.ReadSingle();
                var far = reader.ReadSingle();
                var perspective = new JsonObject { ["yfov"] = yfov, ["znear"] = MathF.Max(near, 0.001f), ["zfar"] = MathF.Max(far, near + 1.0f) };
                if (aspect > 0.0f)
                {
                    perspective["aspectRatio"] = aspect;
                }
                cameras.Add(new JsonObject { ["type"] = "perspective", ["perspective"] = perspective });
            }

            var lights = new JsonArray();
            var lightCount = reader.ReadUInt32();
            for (var i = 0; i < lightCount; i++)
            {
                var name = String(reader);
                var type = reader.ReadInt32();
                var colour = Vec3(reader);
                var intensity = reader.ReadSingle();
                var light = new JsonObject { ["name"] = name, ["color"] = ManaGltfDocument.ToArray(colour), ["intensity"] = intensity };
                switch (type)
                {
                    case 1:
                        light["type"] = "directional";
                        break;
                    case 2:
                        light["type"] = "spot";
                        light["spot"] = new JsonObject();
                        break;
                    default:
                        light["type"] = "point";
                        break;
                }
                lights.Add(light);
            }

            var gltfNodes = new JsonArray();
            var children = new List<List<int>>();
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var item = new JsonObject
                {
                    ["name"] = node.Name,
                    ["translation"] = ManaGltfDocument.ToArray(node.T),
                    ["rotation"] = new JsonArray(node.R.X, node.R.Y, node.R.Z, node.R.W),
                    ["scale"] = ManaGltfDocument.ToArray(node.S)
                };
                if (node.Mesh >= 0 && meshIndex[node.Mesh] >= 0)
                {
                    item["mesh"] = meshIndex[node.Mesh];
                }
                if (node.Camera >= 0)
                {
                    item["camera"] = node.Camera;
                }
                gltfNodes.Add(item);
                children.Add(new List<int>());
            }
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Light < 0)
                {
                    continue;
                }
                // Shining along the node's +Z, as FBX2glTF read it: Bistro's sun then stands 28.8 degrees up, where its HDR's
                // sun is (28.6). The FBX convention (-Y, ufbx's local_direction) puts it at 61. glTF shines down -Z: a half turn.
                gltfNodes.Add(new JsonObject
                {
                    ["name"] = nodes[i].Name + " Light",
                    ["rotation"] = new JsonArray(0.0f, 1.0f, 0.0f, 0.0f),
                    ["extensions"] = new JsonObject { ["KHR_lights_punctual"] = new JsonObject { ["light"] = nodes[i].Light } }
                });
                children[i].Add(gltfNodes.Count - 1);
                children.Add(new List<int>());
            }
            var roots = new JsonArray();
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Parent >= 0 && nodes[i].Parent < nodes.Count)
                {
                    children[nodes[i].Parent].Add(i);
                }
                else
                {
                    roots.Add(i);
                }
            }
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i].Count > 0)
                {
                    gltfNodes[i]!["children"] = new JsonArray(children[i].Select(child => (JsonNode)child).ToArray());
                }
            }

            var root = new JsonObject
            {
                ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "ManaEditor ufbx" },
                ["scene"] = 0,
                ["scenes"] = new JsonArray(new JsonObject { ["name"] = "Root Scene", ["nodes"] = roots }),
                ["nodes"] = gltfNodes,
                ["meshes"] = meshes,
                ["accessors"] = accessors,
                ["bufferViews"] = bufferViews,
                ["buffers"] = new JsonArray(new JsonObject { ["uri"] = bufferUri, ["byteLength"] = binary.Length }),
                ["materials"] = materials,
                ["images"] = images,
                ["textures"] = textures
            };
            if (cameras.Count > 0)
            {
                root["cameras"] = cameras;
            }
            if (lights.Count > 0)
            {
                root["extensionsUsed"] = new JsonArray("KHR_lights_punctual");
                root["extensions"] = new JsonObject { ["KHR_lights_punctual"] = new JsonObject { ["lights"] = lights } };
            }
            return (root, binary.ToArray());
        }

        private static string String(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes((int)reader.ReadUInt32()));

        private static Vector3 Vec3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static (Vector3 Min, Vector3 Max) Bounds(byte[] positions)
        {
            var floats = MemoryMarshal.Cast<byte, float>(positions);
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = 0; i + 2 < floats.Length; i += 3)
            {
                var point = new Vector3(floats[i], floats[i + 1], floats[i + 2]);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
            return (min, max);
        }
    }
}
#endif
