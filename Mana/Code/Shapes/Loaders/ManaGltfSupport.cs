using System.Text;
using System.Text.Json;

namespace Mana.Code.Shapes.Loaders
{
    /// <summary>
    /// Whether a glTF file is one this engine can actually read, asked before handing it to the loader.
    /// <para>
    /// glTF is extensible, and a file can list extensions in <c>extensionsRequired</c> that a reader must
    /// understand or refuse the file outright. SharpGLTF -- the pure-managed loader everything here goes
    /// through -- implements none of the compression extensions, and what it reports when it meets one is
    /// <c>LinkException: Extensions: KHR_draco_mesh_compression ... seems to be malformed</c>, which points
    /// the blame at the file. The file is fine. This says so in those words instead, before the loader gets
    /// a chance to say otherwise.
    /// </para>
    /// </summary>
    public static class ManaGltfSupport
    {
        /// <summary>
        /// A loadable sibling of an unsupported file, when the asset pack ships one.
        /// <para>
        /// Khronos's own sample sets publish the same model several times over --
        /// glTF, glTF-Binary, glTF-Draco, glTF-Embedded -- in sibling folders that share the file's own
        /// name. Only tried when the path itself already sits in one of those "glTF*" folders, so this
        /// never goes looking through an unrelated directory structure for a same-named file that just
        /// happens to exist.
        /// </para>
        /// </summary>
        public static string? FindSupportedAlternative(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory == null || !Path.GetFileName(directory).StartsWith("glTF", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == null)
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var fullPath = Path.GetFullPath(path);

            foreach (var variant in new[] { "glTF", "glTF-Binary", "glTF-Embedded" })
            {
                foreach (var extension in new[] { ".gltf", ".glb" })
                {
                    var candidate = Path.Combine(parent, variant, name + extension);
                    if (string.Equals(candidate, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (File.Exists(candidate) && DescribeUnsupported(candidate) == null)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// What each unsupported extension means, in a sentence someone can act on. Anything not named here
        /// is reported by its raw extension name, which is still better than "malformed".
        /// </summary>
        private static readonly Dictionary<string, string> Explanations = new(StringComparer.Ordinal)
        {
            ["KHR_draco_mesh_compression"] =
                "its geometry is Draco-compressed. Re-export it without Draco, or use the plain glTF/GLB "
                + "copy of the same model if the pack ships one.",
            ["EXT_meshopt_compression"] =
                "its geometry is meshopt-compressed. Re-export it without compression.",
            ["KHR_texture_basisu"] =
                "its textures are Basis Universal / KTX2, which the texture loader does not read."
        };

        /// <summary>Extensions a file may require and still load.</summary>
        private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
        {
            "KHR_materials_unlit",
            "KHR_materials_emissive_strength",
            "KHR_texture_transform",
            "KHR_materials_pbrSpecularGlossiness"
        };

        /// <summary>
        /// Why this file cannot be loaded, or null when nothing rules it out. Reads only the JSON header, so
        /// it costs a few kilobytes rather than a full import -- which matters because the asset grid asks
        /// this about files it is merely showing.
        /// </summary>
        public static string? DescribeUnsupported(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(path);
                return DescribeUnsupported(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable for a reason that has nothing to do with the format; let the loader report it.
                return null;
            }
        }

        /// <summary>The same, over an open stream -- for a glTF read out of a zip rather than off disk.</summary>
        public static string? DescribeUnsupported(Stream stream)
        {
            try
            {
                var json = ReadJsonChunk(stream);
                if (json == null)
                {
                    return null;
                }

                foreach (var name in RequiredExtensions(json))
                {
                    if (Supported.Contains(name))
                    {
                        continue;
                    }

                    return Explanations.TryGetValue(name, out var explanation)
                        ? explanation
                        : $"it requires the glTF extension {name}, which this engine does not implement.";
                }

                return null;
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                // Not parseable as a glTF header at all. That is the loader's error to report, not this
                // one's -- refusing here would blame the wrong thing for every truly corrupt file.
                return null;
            }
        }

        /// <summary>
        /// The names in the top-level <c>extensionsRequired</c> array.
        /// <para>
        /// The array is cut out by hand and parsed on its own rather than parsing the document. A .gltf can
        /// carry its buffers inline as base64 and run to tens of megabytes, none of which says anything
        /// about extensions -- and the header this is handed may be a truncated prefix of exactly such a
        /// file, which no document parser will accept.
        /// </para>
        /// </summary>
        private static IEnumerable<string> RequiredExtensions(string json)
        {
            const string Key = "\"extensionsRequired\"";

            var keyIndex = json.IndexOf(Key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                yield break;
            }

            var open = json.IndexOf('[', keyIndex + Key.Length);
            var close = open < 0 ? -1 : json.IndexOf(']', open);
            if (open < 0 || close < 0)
            {
                yield break;
            }

            using var document = JsonDocument.Parse(json.AsSpan(open, close - open + 1).ToString());
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.GetString() is { Length: > 0 } name)
                {
                    yield return name;
                }
            }
        }

        /// <summary>
        /// The JSON, whether the file is .gltf (JSON all the way down) or .glb (a 12-byte header, then a
        /// length-prefixed JSON chunk). Returns null for anything that is neither.
        /// </summary>
        private static string? ReadJsonChunk(Stream stream)
        {
            const uint GlbMagic = 0x46546C67; // "glTF"
            const uint JsonChunk = 0x4E4F534A; // "JSON"

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (stream.Length < 4)
            {
                return null;
            }

            var magic = reader.ReadUInt32();
            if (magic != GlbMagic)
            {
                // Plain JSON. Capped rather than read whole: extensionsRequired is a top-level key and glTF
                // writers put it in the first few hundred bytes, while the buffers further down can be tens
                // of megabytes of base64.
                stream.Position = 0;
                var cap = (int)Math.Min(stream.Length, 512 * 1024);
                var buffer = new byte[cap];
                var read = stream.Read(buffer, 0, cap);
                return Encoding.UTF8.GetString(buffer, 0, read);
            }

            reader.ReadUInt32(); // version
            reader.ReadUInt32(); // total length

            var chunkLength = reader.ReadUInt32();
            var chunkType = reader.ReadUInt32();
            if (chunkType != JsonChunk || chunkLength == 0 || chunkLength > stream.Length)
            {
                return null;
            }

            return Encoding.UTF8.GetString(reader.ReadBytes((int)chunkLength));
        }
    }
}
