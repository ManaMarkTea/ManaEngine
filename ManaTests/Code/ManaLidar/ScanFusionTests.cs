using System.Numerics;
using ManaLidar;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Depth fusion for phones without LiDAR: a box room seen from a dozen noisy depth frames has to come back as one
/// surface on its walls -- not a sheet per frame -- facing into the room, and refining it has to replace pieces.
/// </summary>
public static class ScanFusionChecks
{
    private static readonly Vector3 RoomMin = new(-2f, 0f, -1.5f);
    private static readonly Vector3 RoomMax = new(2f, 2.5f, 1.5f);

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var volume = new ScanTsdfVolume(voxelSize: 0.04f);
        var random = new Random(7);
        var pieceIds = new HashSet<Guid>();
        var firstFrame = volume.Integrate(DepthFrame(yaw: 0, pitch: -0.5f, random));
        Check("one frame is not yet surface (it needs a second opinion)", firstFrame.All(p => p.TriangleCount == 0),
            $"{firstFrame.Sum(p => p.TriangleCount)} triangles");

        for (var i = 0; i < 16; i++)
        {
            foreach (var piece in volume.Integrate(DepthFrame(yaw: i * MathF.PI / 8, pitch: i % 2 == 0 ? -0.5f : 0.1f, random)))
            {
                pieceIds.Add(piece.Id);
            }
        }
        var pieces = volume.ExtractAll();
        var positions = pieces.SelectMany(p => p.Positions).ToArray();
        Check("frames fuse into a surface", positions.Length > 1000, $"{positions.Length} vertices in {pieces.Count} pieces");
        Check("pieces keep their ids as they refine", pieces.All(p => pieceIds.Contains(p.Id)),
            $"{pieceIds.Count} ids seen");

        var worst = positions.Max(DistanceToWalls);
        var mean = positions.Average(DistanceToWalls);
        Check("the surface lies on the room's walls despite 1 cm depth noise", worst < 0.06f && mean < 0.015f,
            $"mean {mean * 100:F2} cm, worst {worst * 100:F2} cm");

        // One layer: the fused floor's area is no more than the floor itself. Per-frame sheets stack it many times over.
        var floorArea = 0f;
        var facingUp = 0;
        var floorTriangles = 0;
        foreach (var piece in pieces)
        {
            for (var t = 0; t < piece.TriangleCount; t++)
            {
                var a = piece.Positions[piece.Indices[t * 3]];
                var b = piece.Positions[piece.Indices[t * 3 + 1]];
                var c = piece.Positions[piece.Indices[t * 3 + 2]];
                if (MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) > 0.08f)
                {
                    continue;
                }
                var cross = Vector3.Cross(b - a, c - a);
                floorArea += cross.Length() * 0.5f;
                // Horizontal ones only: the wall-floor seam's triangles stand on edge.
                var ny = cross.Y / MathF.Max(cross.Length(), 1e-12f);
                if (MathF.Abs(ny) > 0.7f)
                {
                    floorTriangles++;
                    if (ny > 0)
                    {
                        facingUp++;
                    }
                }
            }
        }
        var roomFloor = (RoomMax.X - RoomMin.X) * (RoomMax.Z - RoomMin.Z);
        Check("the floor is one layer", floorArea > roomFloor * 0.4f && floorArea < roomFloor * 1.1f,
            $"{floorArea:F2} m2 of a {roomFloor:F0} m2 floor");
        Check("and faces up, into the room", facingUp > floorTriangles * 0.99f, $"{facingUp}/{floorTriangles}");
        var normalsUp = pieces.SelectMany(p => p.Positions.Zip(p.Normals)).Where(v => v.First.Y < 0.04f)
            .Count(v => v.Second.Y > 0.8f);
        var floorVertices = positions.Count(p => p.Y < 0.04f);
        Check("its vertex normals agree", normalsUp > floorVertices * 0.9f, $"{normalsUp}/{floorVertices}");

        volume.Clear();
        Check("clearing empties the volume", volume.BlockCount == 0 && volume.ExtractAll().Count == 0);
    }

    private static float DistanceToWalls(Vector3 p) => MathF.Abs(MathF.Min(
        MathF.Min(MathF.Min(p.X - RoomMin.X, RoomMax.X - p.X), MathF.Min(p.Y - RoomMin.Y, RoomMax.Y - p.Y)),
        MathF.Min(p.Z - RoomMin.Z, RoomMax.Z - p.Z)));

    // A 64x48 view from the middle of the room, with depth to its walls (along the view axis) plus noise.
    private static ScanKeyframe DepthFrame(float yaw, float pitch, Random random)
    {
        const int w = 64, h = 48, dw = 64, dh = 48;
        var pose = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(0.2f, 1.4f, 0.1f);
        var origin = pose.Translation;
        var depth = new float[dw * dh];
        for (var y = 0; y < dh; y++)
        {
            for (var x = 0; x < dw; x++)
            {
                var ray = new Vector3((x + 0.5f - w / 2f) / 40f, -(y + 0.5f - h / 2f) / 40f, -1f);
                var dir = Vector3.TransformNormal(ray, pose);
                var s = float.MaxValue;
                for (var axis = 0; axis < 3; axis++)
                {
                    var d = axis switch { 0 => dir.X, 1 => dir.Y, _ => dir.Z };
                    var o = axis switch { 0 => origin.X, 1 => origin.Y, _ => origin.Z };
                    var lo = axis switch { 0 => RoomMin.X, 1 => RoomMin.Y, _ => RoomMin.Z };
                    var hi = axis switch { 0 => RoomMax.X, 1 => RoomMax.Y, _ => RoomMax.Z };
                    if (MathF.Abs(d) > 1e-6f)
                    {
                        s = MathF.Min(s, ((d > 0 ? hi : lo) - o) / d);
                    }
                }
                // ray has z = -1, so the hit's parameter is its depth along the view axis.
                depth[y * dw + x] = s + (float)(random.NextDouble() * 2 - 1) * 0.01f;
            }
        }
        return new ScanKeyframe(w, h, new byte[w * h * 3], pose, 40, 40, w / 2f, h / 2f, 0, dw, dh, depth);
    }
}
