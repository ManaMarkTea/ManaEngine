using System.Text.Json;
using System.Text.Json.Nodes;

namespace ManaVoxel.Code.Imports
{
    /// <summary>
    /// A .gltf as the importer edits it: the JSON as a tree, its one binary buffer in memory, and the node transforms
    /// worked out to world space. Only what scene conversion needs -- float positions, 16/32-bit indices, TRS nodes.
    /// </summary>
    public sealed class ManaGltfDocument
    {
        private ManaGltfDocument(string path, JsonObject root, byte[] buffer)
        {
            Path = path;
            Root = root;
            Buffer = buffer;
        }

        public string Path { get; private set; }
        public JsonObject Root { get; }

        /// <summary>The first buffer's bytes; edits are written back by <see cref="Save"/>.</summary>
        public byte[] Buffer { get; }

        public string Folder => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".";

        public JsonArray Nodes => Array("nodes");
        public JsonArray Meshes => Array("meshes");
        public JsonArray Accessors => Array("accessors");
        public JsonArray Materials => Array("materials");

        public static ManaGltfDocument Load(string path)
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException($"{path}: not JSON.");
            var buffer = System.Array.Empty<byte>();
            if (root["buffers"] is JsonArray { Count: > 0 } buffers && buffers[0]?["uri"]?.GetValue<string>() is { } uri)
            {
                buffer = File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".", Uri.UnescapeDataString(uri)));
            }
            return new ManaGltfDocument(path, root, buffer);
        }

        /// <summary>Writes the JSON to <paramref name="path"/>, and the buffer, when given, beside it under that name.</summary>
        public void Save(string path, string? bufferFileName)
        {
            var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
            Directory.CreateDirectory(folder);
            if (bufferFileName != null)
            {
                File.WriteAllBytes(System.IO.Path.Combine(folder, bufferFileName), Buffer);
                Root["buffers"]![0]!["uri"] = bufferFileName;
            }
            File.WriteAllText(path, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            Path = path;
        }

        public JsonArray Array(string name)
        {
            if (Root[name] is JsonArray array)
            {
                return array;
            }
            var created = new JsonArray();
            Root[name] = created;
            return created;
        }

        /// <summary>A node's own transform (translation, rotation, scale), as System.Numerics composes them.</summary>
        public static Matrix4x4 LocalMatrix(JsonNode node)
        {
            var translation = Vector(node["translation"], Vector3.Zero);
            var scale = Vector(node["scale"], Vector3.One);
            var rotation = Quaternion.Identity;
            if (node["rotation"] is JsonArray r && r.Count == 4)
            {
                rotation = new Quaternion(F(r[0]), F(r[1]), F(r[2]), F(r[3]));
            }
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        }

        /// <summary>Every node's world matrix, walking the default scene from its roots.</summary>
        public Dictionary<int, Matrix4x4> WorldMatrices()
        {
            var worlds = new Dictionary<int, Matrix4x4>();
            void Walk(int index, Matrix4x4 parent)
            {
                var node = Nodes[index]!;
                var world = LocalMatrix(node) * parent;
                worlds[index] = world;
                if (node["children"] is JsonArray children)
                {
                    foreach (var child in children)
                    {
                        Walk(child!.GetValue<int>(), world);
                    }
                }
            }

            var sceneIndex = Root["scene"]?.GetValue<int>() ?? 0;
            if (Root["scenes"] is JsonArray scenes && scenes.Count > sceneIndex && scenes[sceneIndex]?["nodes"] is JsonArray roots)
            {
                foreach (var root in roots)
                {
                    Walk(root!.GetValue<int>(), Matrix4x4.Identity);
                }
            }
            return worlds;
        }

        /// <summary>Where a float VEC3 accessor's values start in the buffer, or -1 when it is not plain packed floats.</summary>
        public int Vec3Offset(int accessor, out int count)
        {
            var item = Accessors[accessor]!;
            count = item["count"]!.GetValue<int>();
            if (item["componentType"]?.GetValue<int>() != 5126 || item["type"]?.GetValue<string>() != "VEC3" || item["bufferView"] is null)
            {
                return -1;
            }
            var view = Array("bufferViews")[item["bufferView"]!.GetValue<int>()]!;
            if (view["byteStride"] is { } stride && stride.GetValue<int>() != 12)
            {
                return -1;
            }
            return (view["byteOffset"]?.GetValue<int>() ?? 0) + (item["byteOffset"]?.GetValue<int>() ?? 0);
        }

        public Vector3[] ReadVec3(int accessor)
        {
            var offset = Vec3Offset(accessor, out var count);
            if (offset < 0)
            {
                return System.Array.Empty<Vector3>();
            }
            var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(Buffer.AsSpan(offset, count * 12));
            var values = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = new Vector3(floats[i * 3], floats[i * 3 + 1], floats[i * 3 + 2]);
            }
            return values;
        }

        public uint[] ReadIndices(int accessor)
        {
            var item = Accessors[accessor]!;
            var count = item["count"]!.GetValue<int>();
            var view = Array("bufferViews")[item["bufferView"]!.GetValue<int>()]!;
            var offset = (view["byteOffset"]?.GetValue<int>() ?? 0) + (item["byteOffset"]?.GetValue<int>() ?? 0);
            var values = new uint[count];
            switch (item["componentType"]!.GetValue<int>())
            {
                case 5125:
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BitConverter.ToUInt32(Buffer, offset + i * 4);
                    }
                    break;
                case 5123:
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BitConverter.ToUInt16(Buffer, offset + i * 2);
                    }
                    break;
                default:
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = Buffer[offset + i];
                    }
                    break;
            }
            return values;
        }

        public static Vector3 Vector(JsonNode? node, Vector3 fallback)
        {
            if (node is JsonArray a && a.Count >= 3)
            {
                return new Vector3(F(a[0]), F(a[1]), F(a[2]));
            }
            return fallback;
        }

        public static JsonArray ToArray(Vector3 value) => new(value.X, value.Y, value.Z);

        /// <summary>A number, whether parsed from the file (any numeric type) or set by the importer as a float or int.</summary>
        public static float F(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return 0.0f;
            }
            if (value.TryGetValue<double>(out var asDouble))
            {
                return (float)asDouble;
            }
            if (value.TryGetValue<float>(out var asFloat))
            {
                return asFloat;
            }
            if (value.TryGetValue<int>(out var asInt))
            {
                return asInt;
            }
            return 0.0f;
        }

        public static int I(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<int>(out var asInt))
            {
                return asInt;
            }
            return (int)F(node);
        }
    }
}
