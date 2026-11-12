using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics.Resources;

/// <summary>Cached render geometry; never replaces or mutates the source scene's objects.</summary>
public sealed class ManaStaticMeshBatcher
{
    public int MaxVertices { get; init; } = 1 << 20;
    private long _nextId;
    private ManaRenderMeshInstance[]? _lastInput;
    private List<ManaRenderMeshInstance>? _lastOutput;
    private Dictionary<Surface, List<Cached>> _cache = new();
    private sealed record Source(string? Instance, string? Mesh, ulong Version, ulong Geometry, Matrix4x4 World);
    private sealed record Cached(Source[] Sources, ManaRenderMeshInstance Instance);
    private sealed record Surface(
        string? StaticBatchKey,
        string? ShaderName,
        Vector4 Albedo,
        float Metallic,
        float Roughness,
        bool FlatShaded,
        bool Unlit,
        float AmbientOcclusion,
        Vector4 Emission,
        float EmissionStrength,
        float? PbrDebugMode,
        bool IncludeInEnvironmentCapture,
        bool IsTransparent,
        bool DisableDepthWrite,
        bool AlwaysOnTop,
        bool OutlineEnabled,
        byte PostEffects,
        Vector4 UvTransform,
        float InsetDepth,
        float NormalStrength,
        ManaOpacityMode OpacityMode,
        float AlphaCutoff,
        float Opacity,
        string? AlbedoTexturePath,
        string? NormalTexturePath,
        string? EmissionTexturePath,
        string? MetallicTexturePath,
        string? RoughnessTexturePath,
        string? OcclusionTexturePath,
        string? OpacityTexturePath,
        byte[]? AlbedoTextureBytes,
        Vector4? AlbedoFactor,
        bool NearestFiltering);
    private static Surface Key(ManaRenderMeshInstance i) => new(
        i.StaticBatchKey,
        i.ShaderName,
        i.Albedo,
        i.Metallic,
        i.Roughness,
        i.FlatShaded,
        i.Unlit,
        i.AmbientOcclusion,
        i.Emission,
        i.EmissionStrength,
        i.PbrDebugMode,
        i.IncludeInEnvironmentCapture,
        i.IsTransparent,
        i.DisableDepthWrite,
        i.AlwaysOnTop,
        i.OutlineEnabled,
        i.PostEffects,
        i.UvTransform,
        i.InsetDepth,
        i.NormalStrength,
        i.OpacityMode,
        i.AlphaCutoff,
        i.Opacity,
        i.MeshData.AlbedoTexturePath,
        i.MeshData.NormalTexturePath,
        i.MeshData.EmissionTexturePath,
        i.MeshData.MetallicTexturePath,
        i.MeshData.RoughnessTexturePath,
        i.MeshData.OcclusionTexturePath,
        i.MeshData.OpacityTexturePath,
        i.MeshData.AlbedoTextureBytes,
        i.MeshData.AlbedoFactor,
        i.MeshData.NearestFiltering);

    public List<ManaRenderMeshInstance> Build(IReadOnlyList<ManaRenderMeshInstance> input)
    {
        if (_lastInput != null && _lastOutput != null && input.SequenceEqual(_lastInput)) return _lastOutput;
        var output = new List<ManaRenderMeshInstance>();
        var groups = new Dictionary<Surface, List<List<ManaRenderMeshInstance>>>();
        var vertexCounts = new Dictionary<Surface, int>();
        foreach (var instance in input)
        {
            if (instance.MeshData.Positions.Length == 0 || instance.MeshData.Indexes.Length == 0) continue;
            if (instance.StaticBatchKey == null || instance.IsTransparent || instance.AlwaysOnTop || instance.OutlineEnabled
                || instance.CustomShaderUniforms is { Count: > 0 } || (instance.ShaderName != null && instance.ShaderName != "ManaPbrShader")
                || !Matrix4x4.Invert(instance.WorldTransform, out _))
            {
                output.Add(instance);
                continue;
            }
            var key = Key(instance);
            if (!groups.TryGetValue(key, out var chunks)) groups[key] = chunks = new();
            if (chunks.Count == 0 || vertexCounts.GetValueOrDefault(key) + instance.MeshData.Positions.Length > MaxVertices)
            {
                chunks.Add(new());
                vertexCounts[key] = 0;
            }
            chunks[^1].Add(instance);
            vertexCounts[key] += instance.MeshData.Positions.Length;
        }
        var next = new Dictionary<Surface, List<Cached>>();
        foreach (var (key, chunks) in groups)
        {
            var retained = new List<Cached>();
            _cache.TryGetValue(key, out var previous);
            for (var chunk = 0; chunk < chunks.Count; chunk++)
            {
                var parts = chunks[chunk];
                if (parts.Count == 1) { output.Add(parts[0]); continue; }
                var sources = parts.Select(i => new Source(i.InstanceKey, i.MeshKey, i.MeshVersion, i.MeshData.GeometryHash, i.WorldTransform)).ToArray();
                var old = previous?.FirstOrDefault(c => c.Sources.SequenceEqual(sources));
                var cached = old ?? new Cached(sources, Merge(parts, $"static-batch:{++_nextId}"));
                retained.Add(cached);
                output.Add(cached.Instance);
            }
            next[key] = retained;
        }
        _cache = next;
        _lastInput = input.ToArray();
        _lastOutput = output;
        return output;
    }

    public void Clear()
    {
        _cache.Clear();
        _lastInput = null;
        _lastOutput = null;
    }

    private static ManaRenderMeshInstance Merge(List<ManaRenderMeshInstance> parts, string id)
    {
        var count = parts.Sum(i => i.MeshData.Positions.Length);
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var colors = new Vector4[count];
        var uv0 = new Vector2[count];
        var uv1 = new Vector2[count];
        var indexes = new uint[parts.Sum(i => i.MeshData.Indexes.Length)];
        int vertex = 0, index = 0;
        foreach (var part in parts)
        {
            var data = part.MeshData;
            Matrix4x4.Invert(part.WorldTransform, out var inverse);
            var normalTransform = Matrix4x4.Transpose(inverse);
            // iOS AOT can read past the end of Vector2/Vector3 arrays; read their scalar storage instead.
            var points = MemoryMarshal.Cast<Vector3, float>(data.Positions.AsSpan());
            var directions = MemoryMarshal.Cast<Vector3, float>(data.Normals.AsSpan());
            var tex0 = MemoryMarshal.Cast<Vector2, float>(data.TextureCoordinates0.AsSpan());
            var tex1 = MemoryMarshal.Cast<Vector2, float>(data.TextureCoordinates1.AsSpan());
            for (var v = 0; v < data.Positions.Length; v++)
            {
                positions[vertex + v] = Vector3.Transform(new Vector3(points[v * 3], points[v * 3 + 1], points[v * 3 + 2]), part.WorldTransform);
                var n = Vector3.TransformNormal(v < data.Normals.Length ? new Vector3(directions[v * 3], directions[v * 3 + 1], directions[v * 3 + 2]) : Vector3.UnitY, normalTransform);
                normals[vertex + v] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
                colors[vertex + v] = v < data.Colors.Length ? data.Colors[v] : Vector4.One;
                uv0[vertex + v] = v < data.TextureCoordinates0.Length ? new Vector2(tex0[v * 2], tex0[v * 2 + 1]) : Vector2.Zero;
                uv1[vertex + v] = v < data.TextureCoordinates1.Length ? new Vector2(tex1[v * 2], tex1[v * 2 + 1]) : Vector2.Zero;
            }
            for (var i = 0; i < data.Indexes.Length; i++) indexes[index + i] = data.Indexes[i] + (uint)vertex;
            index += data.Indexes.Length;
            vertex += data.Positions.Length;
        }
        var template = parts[0].MeshData;
        var merged = new ManaMeshCpuData
        {
            Positions = positions, Normals = normals, Colors = colors, Indexes = indexes,
            TextureCoordinates0 = uv0, TextureCoordinates1 = uv1,
            AlbedoTexturePath = template.AlbedoTexturePath,
            NormalTexturePath = template.NormalTexturePath,
            EmissionTexturePath = template.EmissionTexturePath,
            MetallicTexturePath = template.MetallicTexturePath,
            RoughnessTexturePath = template.RoughnessTexturePath,
            OcclusionTexturePath = template.OcclusionTexturePath,
            OpacityTexturePath = template.OpacityTexturePath,
            AlbedoTextureBytes = template.AlbedoTextureBytes,
            AlbedoFactor = template.AlbedoFactor,
            NearestFiltering = template.NearestFiltering,
        };
        return parts[0] with { InstanceKey = id, MeshKey = id, MeshVersion = 1, Name = "Static render batch",
            MeshData = merged, WorldTransform = Matrix4x4.Identity };
    }
}
