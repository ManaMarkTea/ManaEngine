using System.Numerics;
using System.Runtime.InteropServices;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace ManaLidar;

public sealed class ScanExportOptions
{
    public string Name { get; set; } = "Room Scan";
    /// <summary>Colour vertices from the keyframes; off leaves each surface its classification colour.</summary>
    public bool Colors { get; set; } = true;
    /// <summary>Floor at y = 0 and the room centred on the origin, instead of wherever the phone started.</summary>
    public bool Recenter { get; set; } = true;
    /// <summary>KHR_materials_unlit: photo colours already carry the room's light, so lighting them again darkens them.</summary>
    public bool Unlit { get; set; } = true;
    /// <summary>0..1 as pieces are coloured; colouring is nearly all of an export's time.</summary>
    public IProgress<float>? Progress { get; set; }
}

/// <summary>Turns a scan into one glTF mesh: POSITION, NORMAL and COLOR_0 (RGBA8), metres, Y up.</summary>
public static class ScanGltfExporter
{
    public static void SaveGlb(string path, IReadOnlyList<ScanMeshPiece> pieces, IReadOnlyList<ScanKeyframe> keyframes,
        ScanExportOptions? options = null)
    {
        Build(pieces, keyframes, options).SaveGLB(path);
    }

    public static ModelRoot Build(IReadOnlyList<ScanMeshPiece> pieces, IReadOnlyList<ScanKeyframe> keyframes,
        ScanExportOptions? options = null)
    {
        options ??= new ScanExportOptions();
        var vertexCount = pieces.Sum(p => p.Positions.Length);
        var indexCount = pieces.Sum(p => p.Indices.Length);
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var colors = new uint[vertexCount];
        var indices = new uint[indexCount];

        var offset = options.Recenter ? RecenterOffset(pieces) : Vector3.Zero;
        var vertexBase = 0;
        var indexBase = 0;
        for (var p = 0; p < pieces.Count; p++)
        {
            var piece = pieces[p];
            var pieceColors = options.Colors && keyframes.Count > 0
                ? ScanColorizer.Colorize(piece, keyframes)
                : ScanColorizer.VertexSurfaces(piece).Select(ScanColorizer.SurfaceColor).ToArray();
            for (var i = 0; i < piece.Positions.Length; i++)
            {
                positions[vertexBase + i] = piece.Positions[i] + offset;
                normals[vertexBase + i] = piece.Normals[i];
                colors[vertexBase + i] = PackRgba(pieceColors[i]);
            }
            for (var i = 0; i < piece.Indices.Length; i++)
            {
                indices[indexBase + i] = (uint)(piece.Indices[i] + vertexBase);
            }
            vertexBase += piece.Positions.Length;
            indexBase += piece.Indices.Length;
            options.Progress?.Report((p + 1f) / pieces.Count);
        }

        var model = ModelRoot.CreateModel();
        var material = model.CreateMaterial(options.Name);
        if (options.Unlit)
        {
            material.InitializeUnlit();
        }
        else
        {
            // A room is not metal: glTF's default metallic factor of 1 would turn every wall into a mirror.
            material.InitializePBRMetallicRoughness();
            if (material.FindChannel("MetallicRoughness") is { } metallicRoughness)
            {
                metallicRoughness.Parameter = new Vector4(0f, 1f, 0f, 0f);
            }
        }

        var mesh = model.CreateMesh(options.Name);
        var primitive = mesh.CreatePrimitive();
        primitive.Material = material;
        if (vertexCount > 0)
        {
            primitive.SetVertexAccessor("POSITION", Accessor(model, positions, BufferMode.ARRAY_BUFFER,
                new AttributeFormat(DimensionType.VEC3)));
            primitive.SetVertexAccessor("NORMAL", Accessor(model, normals, BufferMode.ARRAY_BUFFER,
                new AttributeFormat(DimensionType.VEC3)));
            primitive.SetVertexAccessor("COLOR_0", Accessor(model, colors, BufferMode.ARRAY_BUFFER,
                new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE, true)));

            var indexBytes = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
            var indexAccessor = model.CreateAccessor();
            indexAccessor.SetIndexData(model.UseBufferView(indexBytes, 0, null, 0, BufferMode.ELEMENT_ARRAY_BUFFER),
                0, indices.Length, IndexEncodingType.UNSIGNED_INT);
            primitive.SetIndexAccessor(indexAccessor);
        }

        model.UseScene("Scene").CreateNode(options.Name).Mesh = mesh;
        return model;
    }

    /// <summary>
    /// What to add to every position to put the floor at y = 0 and the room's middle over the origin. The floor is
    /// the median height of floor-classified points when ARKit found enough of them, else a low percentile, so one
    /// stray point under the floor cannot sink the room.
    /// </summary>
    public static Vector3 RecenterOffset(IReadOnlyList<ScanMeshPiece> pieces)
    {
        var all = pieces.SelectMany(p => p.Positions).ToArray();
        if (all.Length == 0)
        {
            return Vector3.Zero;
        }

        var floorHeights = new List<float>();
        foreach (var piece in pieces)
        {
            var surfaces = ScanColorizer.VertexSurfaces(piece);
            for (var i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] == ScanSurface.Floor)
                {
                    floorHeights.Add(piece.Positions[i].Y);
                }
            }
        }
        var floor = floorHeights.Count >= 50
            ? Percentile(floorHeights.ToArray(), 0.5f)
            : Percentile(all.Select(p => p.Y).ToArray(), 0.02f);

        var xs = all.Select(p => p.X).ToArray();
        var zs = all.Select(p => p.Z).ToArray();
        var centreX = (Percentile(xs, 0.02f) + Percentile(xs, 0.98f)) * 0.5f;
        var centreZ = (Percentile(zs, 0.02f) + Percentile(zs, 0.98f)) * 0.5f;
        return new Vector3(-centreX, -floor, -centreZ);
    }

    private static float Percentile(float[] values, float fraction)
    {
        Array.Sort(values);
        return values[Math.Clamp((int)(fraction * (values.Length - 1)), 0, values.Length - 1)];
    }

    private static uint PackRgba(Vector3 c)
    {
        static uint B(float v) => (uint)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
        // Little-endian RGBA8: red in the lowest byte.
        return B(c.X) | (B(c.Y) << 8) | (B(c.Z) << 16) | (255u << 24);
    }

    private static SharpGLTF.Schema2.Accessor Accessor<T>(ModelRoot model, T[] data, BufferMode mode, AttributeFormat format)
        where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
        var accessor = model.CreateAccessor();
        accessor.SetVertexData(model.UseBufferView(bytes, 0, null, 0, mode), 0, data.Length, format);
        return accessor;
    }
}
