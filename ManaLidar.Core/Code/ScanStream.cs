using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace ManaLidar;

/// <summary>Something a phone tells the editor while streaming a scan.</summary>
public abstract record ScanStreamMessage;

/// <summary>First thing on a connection: which phone, speaking which version.</summary>
public sealed record ScanHello(string Device, int Version) : ScanStreamMessage;

/// <summary>A mesh piece, new or replacing the one with its id.</summary>
public sealed record ScanPieceMessage(ScanMeshPiece Piece) : ScanStreamMessage;

public sealed record ScanRemoveMessage(Guid Id) : ScanStreamMessage;

/// <summary>Where the phone is and which way it looks, a few times a second.</summary>
public sealed record ScanPoseMessage(Matrix4x4 CameraToWorld, double Timestamp) : ScanStreamMessage;

/// <summary>A kept photo: JPEG (or PNG) bytes, its pose and intrinsics, and its depth in millimetres.</summary>
public sealed record ScanKeyframeMessage(
    int Width, int Height, byte[] Image, Matrix4x4 CameraToWorld, float Fx, float Fy, float Cx, float Cy, double Timestamp,
    int DepthWidth, int DepthHeight, ushort[]? DepthMillimetres) : ScanStreamMessage
{
    /// <summary>From a keyframe, with its photo encoded by <paramref name="encode"/> (width, height, rgb).</summary>
    public static ScanKeyframeMessage From(ScanKeyframe keyframe, Func<int, int, byte[], byte[]> encode) => new(
        keyframe.Width, keyframe.Height, encode(keyframe.Width, keyframe.Height, keyframe.Rgb), keyframe.CameraToWorld,
        keyframe.Fx, keyframe.Fy, keyframe.Cx, keyframe.Cy, keyframe.Timestamp, keyframe.DepthWidth, keyframe.DepthHeight,
        keyframe.Depth?.Select(d => (ushort)Math.Clamp(d * 1000f, 0f, ushort.MaxValue)).ToArray());

    /// <summary>Back to a keyframe, with the photo decoded by <paramref name="decode"/> to RGB of Width x Height.</summary>
    public ScanKeyframe ToKeyframe(Func<byte[], byte[]> decode) => new(
        Width, Height, decode(Image), CameraToWorld, Fx, Fy, Cx, Cy, Timestamp,
        DepthMillimetres == null ? 0 : DepthWidth, DepthMillimetres == null ? 0 : DepthHeight,
        DepthMillimetres?.Select(d => d / 1000f).ToArray());
}

/// <summary>The scan started over: forget everything so far.</summary>
public sealed record ScanClearMessage : ScanStreamMessage;

/// <summary>
/// The live-scan wire format, phone to editor over TCP: each message is a little-endian length, a kind byte, and a
/// deflated body. Plain binary rather than JSON: a room's mesh is megabytes of floats, resent as ARKit refines it.
/// </summary>
public static class ScanStream
{
    public const int Version = 1;
    /// <summary>The editor listens on the first free port from here.</summary>
    public const int Port = 47800;
    /// <summary>What the editor announces itself as over Bonjour, and what the phone looks for.</summary>
    public const string Service = "_manascan._tcp";

    private enum Kind : byte
    {
        Hello = 1,
        Piece = 2,
        Remove = 3,
        Pose = 4,
        Keyframe = 5,
        Clear = 6,
    }

    /// <summary>A message this big is not one of ours: the stream is out of step, or not a phone.</summary>
    private const int MaxMessageBytes = 64 * 1024 * 1024;

    public static void Write(Stream stream, ScanStreamMessage message)
    {
        using var body = new MemoryStream();
        Kind kind;
        using (var deflate = new DeflateStream(body, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(deflate, Encoding.UTF8, leaveOpen: true))
        {
            kind = WriteBody(w, message);
        }
        var header = new byte[5];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), (int)body.Length + 1);
        header[4] = (byte)kind;
        stream.Write(header);
        body.Position = 0;
        body.CopyTo(stream);
    }

    /// <summary>The next message, or null at the end of the stream.</summary>
    public static ScanStreamMessage? Read(Stream stream)
    {
        var header = new byte[5];
        if (!ReadExactly(stream, header))
        {
            return null;
        }
        var length = BitConverter.ToInt32(header, 0);
        if (length < 1 || length > MaxMessageBytes)
        {
            throw new InvalidDataException($"A live-scan message of {length} bytes: the stream is out of step.");
        }
        var payload = new byte[length - 1];
        if (!ReadExactly(stream, payload))
        {
            return null;
        }
        using var deflate = new DeflateStream(new MemoryStream(payload), CompressionMode.Decompress);
        using var r = new BinaryReader(deflate, Encoding.UTF8);
        return ReadBody(r, (Kind)header[4]);
    }

    private static Kind WriteBody(BinaryWriter w, ScanStreamMessage message)
    {
        switch (message)
        {
            case ScanHello hello:
                w.Write(hello.Version);
                w.Write(hello.Device);
                return Kind.Hello;
            case ScanPieceMessage { Piece: var piece }:
                w.Write(piece.Id.ToByteArray());
                w.Write(piece.Positions.Length);
                foreach (var p in piece.Positions)
                {
                    w.Write(p.X);
                    w.Write(p.Y);
                    w.Write(p.Z);
                }
                // Normals to a byte a component: plenty for lighting and colouring, a sixth of the size.
                foreach (var n in piece.Normals)
                {
                    w.Write((sbyte)Math.Clamp(MathF.Round(n.X * 127f), -127, 127));
                    w.Write((sbyte)Math.Clamp(MathF.Round(n.Y * 127f), -127, 127));
                    w.Write((sbyte)Math.Clamp(MathF.Round(n.Z * 127f), -127, 127));
                }
                w.Write(piece.Indices.Length);
                foreach (var i in piece.Indices)
                {
                    w.Write(i);
                }
                w.Write(piece.FaceSurfaces != null);
                if (piece.FaceSurfaces != null)
                {
                    w.Write(piece.FaceSurfaces.Select(s => (byte)s).ToArray());
                }
                return Kind.Piece;
            case ScanRemoveMessage remove:
                w.Write(remove.Id.ToByteArray());
                return Kind.Remove;
            case ScanPoseMessage pose:
                WriteMatrix(w, pose.CameraToWorld);
                w.Write(pose.Timestamp);
                return Kind.Pose;
            case ScanKeyframeMessage k:
                w.Write(k.Width);
                w.Write(k.Height);
                w.Write(k.Image.Length);
                w.Write(k.Image);
                WriteMatrix(w, k.CameraToWorld);
                w.Write(k.Fx);
                w.Write(k.Fy);
                w.Write(k.Cx);
                w.Write(k.Cy);
                w.Write(k.Timestamp);
                w.Write(k.DepthMillimetres != null ? k.DepthWidth : 0);
                w.Write(k.DepthMillimetres != null ? k.DepthHeight : 0);
                foreach (var d in k.DepthMillimetres ?? Array.Empty<ushort>())
                {
                    w.Write(d);
                }
                return Kind.Keyframe;
            case ScanClearMessage:
                return Kind.Clear;
            default:
                throw new ArgumentException($"Not a live-scan message: {message.GetType().Name}");
        }
    }

    private static ScanStreamMessage ReadBody(BinaryReader r, Kind kind)
    {
        switch (kind)
        {
            case Kind.Hello:
            {
                var version = r.ReadInt32();
                return new ScanHello(r.ReadString(), version);
            }
            case Kind.Piece:
            {
                var id = new Guid(r.ReadBytes(16));
                var count = r.ReadInt32();
                var positions = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    positions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                }
                var normals = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    var n = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte());
                    normals[i] = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitY;
                }
                var indices = new int[r.ReadInt32()];
                for (var i = 0; i < indices.Length; i++)
                {
                    indices[i] = r.ReadInt32();
                }
                ScanSurface[]? surfaces = null;
                if (r.ReadBoolean())
                {
                    surfaces = r.ReadBytes(indices.Length / 3).Select(b => (ScanSurface)b).ToArray();
                }
                return new ScanPieceMessage(new ScanMeshPiece(id, positions, normals, indices, surfaces));
            }
            case Kind.Remove:
                return new ScanRemoveMessage(new Guid(r.ReadBytes(16)));
            case Kind.Pose:
                return new ScanPoseMessage(ReadMatrix(r), r.ReadDouble());
            case Kind.Keyframe:
            {
                var width = r.ReadInt32();
                var height = r.ReadInt32();
                var image = r.ReadBytes(r.ReadInt32());
                var pose = ReadMatrix(r);
                float fx = r.ReadSingle(), fy = r.ReadSingle(), cx = r.ReadSingle(), cy = r.ReadSingle();
                var timestamp = r.ReadDouble();
                var depthWidth = r.ReadInt32();
                var depthHeight = r.ReadInt32();
                ushort[]? depth = null;
                if (depthWidth > 0 && depthHeight > 0)
                {
                    depth = new ushort[depthWidth * depthHeight];
                    for (var i = 0; i < depth.Length; i++)
                    {
                        depth[i] = r.ReadUInt16();
                    }
                }
                return new ScanKeyframeMessage(width, height, image, pose, fx, fy, cx, cy, timestamp, depthWidth, depthHeight, depth);
            }
            case Kind.Clear:
                return new ScanClearMessage();
            default:
                throw new InvalidDataException($"Unknown live-scan message kind {(byte)kind}.");
        }
    }

    private static void WriteMatrix(BinaryWriter w, Matrix4x4 m)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                w.Write(m[row, column]);
            }
        }
    }

    private static Matrix4x4 ReadMatrix(BinaryReader r)
    {
        var m = new Matrix4x4();
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                m[row, column] = r.ReadSingle();
            }
        }
        return m;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = stream.Read(buffer, read, buffer.Length - read);
            if (got == 0)
            {
                return false;
            }
            read += got;
        }
        return true;
    }
}
