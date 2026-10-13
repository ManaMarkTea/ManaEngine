using System.Numerics;
using ManaLidar;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The live-scan wire format: every message a phone sends comes back out of the stream as it went in, a truncated
/// stream ends cleanly, and a stream out of step is refused rather than read as garbage.
/// </summary>
public static class ScanStreamChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var piece = new ScanMeshPiece(Guid.NewGuid(),
            new[] { new Vector3(1.5f, -0.25f, 3), new Vector3(2, 0, 3), new Vector3(1.5f, 1, 3.5f) },
            new[] { Vector3.UnitY, Vector3.Normalize(new Vector3(1, 1, 0)), -Vector3.UnitZ },
            new[] { 0, 1, 2 }, new[] { ScanSurface.Wall });
        var pose = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(1, 1.4f, -2);
        var keyframe = new ScanKeyframeMessage(4, 2, new byte[] { 1, 2, 3, 4, 5 }, pose, 300, 301, 2, 1, 12.5, 2, 1, new ushort[] { 1500, 2250 });
        var sent = new ScanStreamMessage[]
        {
            new ScanHello("Test Phone", ScanStream.Version),
            new ScanPieceMessage(piece),
            new ScanPoseMessage(pose, 3.25),
            keyframe,
            new ScanRemoveMessage(piece.Id),
            new ScanClearMessage(),
        };

        using var stream = new MemoryStream();
        foreach (var message in sent)
        {
            ScanStream.Write(stream, message);
        }
        stream.Position = 0;
        var got = new List<ScanStreamMessage>();
        while (ScanStream.Read(stream) is { } message)
        {
            got.Add(message);
        }

        Check("every message comes back, in order", got.Select(m => m.GetType()).SequenceEqual(sent.Select(m => m.GetType())),
            string.Join(", ", got.Select(m => m.GetType().Name)));
        Check("the hello names the phone", got.OfType<ScanHello>().FirstOrDefault() is { Device: "Test Phone", Version: ScanStream.Version });

        var back = got.OfType<ScanPieceMessage>().FirstOrDefault()?.Piece;
        Check("a piece keeps its id, positions, triangles and surfaces", back != null && back.Id == piece.Id
            && back.Positions.SequenceEqual(piece.Positions) && back.Indices.SequenceEqual(piece.Indices)
            && back.FaceSurfaces?.SequenceEqual(piece.FaceSurfaces!) == true);
        Check("and its normals to within a degree or so", back != null
            && back.Normals.Zip(piece.Normals).All(n => Vector3.Dot(n.First, n.Second) > 0.999f));

        Check("a pose comes back exact", got.OfType<ScanPoseMessage>().FirstOrDefault() is { } p && p.CameraToWorld == pose && p.Timestamp == 3.25);
        var k = got.OfType<ScanKeyframeMessage>().FirstOrDefault();
        Check("a photo keeps its image, pose, intrinsics and depth", k != null && k.Image.SequenceEqual(keyframe.Image)
            && k.CameraToWorld == pose && k.Fx == 300 && k.Cy == 1 && k.DepthMillimetres?.SequenceEqual(keyframe.DepthMillimetres!) == true);
        var asKeyframe = k?.ToKeyframe(_ => new byte[4 * 2 * 3]);
        Check("and turns back into a keyframe, depth in metres", asKeyframe is { Depth: { } d } && MathF.Abs(d[1] - 2.25f) < 1e-4f);
        Check("a removal names its piece", got.OfType<ScanRemoveMessage>().FirstOrDefault()?.Id == piece.Id);

        // Cut short mid-message: the end of a dropped connection, not an error.
        var whole = stream.ToArray();
        Check("a stream cut short just ends", ScanStream.Read(new MemoryStream(whole[..7])) == null);
        var garbage = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 });
        var refused = false;
        try
        {
            ScanStream.Read(garbage);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }
        Check("a stream out of step is refused, not read as garbage", refused);
    }
}
