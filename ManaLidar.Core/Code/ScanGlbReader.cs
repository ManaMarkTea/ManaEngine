using System.Numerics;
using SharpGLTF.Schema2;

namespace ManaLidar;

/// <summary>
/// Reads a scan back for viewing on the phone: every mesh in the file (a room, or each room of a building) with its
/// node's placement applied, its UVs and encoded texture, or its vertex colours. The phones have no Mana to load
/// glTF with, so this is what their viewers draw from.
/// </summary>
public static class ScanGlbReader
{
    public static IReadOnlyList<ScanRoomMesh> Read(string path) => Read(ModelRoot.Load(path));

    public static IReadOnlyList<ScanRoomMesh> Read(ModelRoot model)
    {
        var rooms = new List<ScanRoomMesh>();
        foreach (var node in model.DefaultScene?.VisualChildren ?? Enumerable.Empty<Node>())
        {
            Collect(node, rooms);
        }
        return rooms;
    }

    private static void Collect(Node node, List<ScanRoomMesh> rooms)
    {
        if (node.Mesh is { } mesh)
        {
            var world = node.WorldMatrix;
            foreach (var primitive in mesh.Primitives)
            {
                if (primitive.GetVertexAccessor("POSITION") is not { } positionAccessor)
                {
                    continue;
                }
                var positions = positionAccessor.AsVector3Array().Select(p => Vector3.Transform(p, world)).ToArray();
                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array()
                    .Select(n => Vector3.Normalize(Vector3.TransformNormal(n, world))).ToArray()
                    ?? Enumerable.Repeat(Vector3.UnitY, positions.Length).ToArray();
                var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array().ToArray();
                var colours = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray()
                    .Select(c => Pack(new Vector3(c.X, c.Y, c.Z))).ToArray();
                var image = primitive.Material?.FindChannel("BaseColor")?.Texture?.PrimaryImage?.Content.Content.ToArray();
                var indices = primitive.GetIndices().ToArray();

                rooms.Add(new ScanRoomMesh
                {
                    Name = string.IsNullOrEmpty(node.Name) ? mesh.Name ?? "Scan" : node.Name,
                    Positions = positions,
                    Normals = normals,
                    Indices = indices,
                    Uvs = image != null ? uvs : null,
                    Image = uvs != null ? image : null,
                    Colors = colours,
                    Unlit = primitive.Material?.Unlit ?? true,
                });
            }
        }
        foreach (var child in node.VisualChildren)
        {
            Collect(child, rooms);
        }
    }

    private static uint Pack(Vector3 c)
    {
        static uint B(float v) => (uint)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
        return B(c.X) | (B(c.Y) << 8) | (B(c.Z) << 16) | (255u << 24);
    }
}
