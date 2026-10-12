using System.Numerics;
using ARKit;
using CoreVideo;

namespace ManaLidar;

/// <summary>ARKit's buffers and matrices into ManaLidar.Core's plain types.</summary>
public static class ArkitConvert
{
    private const int DepthWidth = 128;
    private const int DepthHeight = 96;

    /// <summary>
    /// simd matrices are column-vector (translation in M14/M24/M34); System.Numerics is row-vector
    /// (translation in M41..M43), so the conversion is a transpose.
    /// </summary>
    public static Matrix4x4 ToNumerics(NMatrix4 m) => new(
        m.M11, m.M21, m.M31, m.M41,
        m.M12, m.M22, m.M32, m.M42,
        m.M13, m.M23, m.M33, m.M43,
        m.M14, m.M24, m.M34, m.M44);

    /// <summary>The anchor's current mesh, in world space.</summary>
    public static unsafe ScanMeshPiece ReadMesh(ARMeshAnchor anchor)
    {
        var geometry = anchor.Geometry;
        var positions = ReadVectors(geometry.Vertices);
        var normals = ReadVectors(geometry.Normals);

        var faces = geometry.Faces;
        var faceCount = (int)faces.Count;
        var indexCount = faceCount * (int)faces.IndexCountPerPrimitive;
        var indices = new int[indexCount];
        var bytes = (byte*)faces.Buffer.Contents;
        if (faces.BytesPerIndex == 2)
        {
            for (var i = 0; i < indexCount; i++)
            {
                indices[i] = ((ushort*)bytes)[i];
            }
        }
        else
        {
            for (var i = 0; i < indexCount; i++)
            {
                indices[i] = (int)((uint*)bytes)[i];
            }
        }

        ScanSurface[]? surfaces = null;
        if (geometry.Classification is { } classification && (int)classification.Count == faceCount)
        {
            surfaces = new ScanSurface[faceCount];
            var source = (byte*)classification.Buffer.Contents + classification.Offset;
            for (var f = 0; f < faceCount; f++)
            {
                surfaces[f] = (ScanSurface)source[f * (int)classification.Stride];
            }
        }

        return ScanMeshPiece.FromLocal(new Guid(anchor.Identifier.GetBytes()), positions, normals, indices,
            ToNumerics(anchor.Transform), surfaces);
    }

    /// <summary>Anchor-local float3 data, for the live overlay as well as the export.</summary>
    public static unsafe Vector3[] ReadVectors(ARGeometrySource source)
    {
        var count = (int)source.Count;
        var result = new Vector3[count];
        var bytes = (byte*)source.Buffer.Contents + source.Offset;
        var stride = (int)source.Stride;
        for (var i = 0; i < count; i++)
        {
            var v = (float*)(bytes + i * stride);
            result[i] = new Vector3(v[0], v[1], v[2]);
        }
        return result;
    }

    /// <summary>
    /// A small copy of the frame's photo and LiDAR depth. Everything is copied out before returning, because ARKit
    /// stops handing out frames once a few of its buffers are held.
    /// </summary>
    public static unsafe ScanKeyframe? CaptureKeyframe(ARFrame frame, int keyframeWidth, int keyframeHeight)
    {
        var image = frame.CapturedImage;
        if (image.PlaneCount < 2)
        {
            return null;
        }

        var rgb = new byte[keyframeWidth * keyframeHeight * 3];
        int width;
        int height;
        image.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            width = (int)image.GetWidthOfPlane(0);
            height = (int)image.GetHeightOfPlane(0);
            var lumaStride = (int)image.GetBytesPerRowOfPlane(0);
            var chromaStride = (int)image.GetBytesPerRowOfPlane(1);
            var chromaHeight = (int)image.GetHeightOfPlane(1);
            var luma = new ReadOnlySpan<byte>((void*)image.GetBaseAddress(0), lumaStride * height);
            var chroma = new ReadOnlySpan<byte>((void*)image.GetBaseAddress(1), chromaStride * chromaHeight);
            YCbCrImage.DownsampleToRgb(luma, lumaStride, width, height, chroma, chromaStride,
                keyframeWidth, keyframeHeight, rgb);
        }
        finally
        {
            image.Unlock(CVPixelBufferLock.ReadOnly);
        }

        float[]? depth = null;
        if (frame.SceneDepth?.DepthMap is { } depthMap)
        {
            depth = new float[DepthWidth * DepthHeight];
            depthMap.Lock(CVPixelBufferLock.ReadOnly);
            try
            {
                var dw = (int)depthMap.Width;
                var dh = (int)depthMap.Height;
                var stride = (int)depthMap.BytesPerRow;
                var basePtr = (byte*)depthMap.BaseAddress;
                for (var y = 0; y < DepthHeight; y++)
                {
                    var row = (float*)(basePtr + Math.Min(y * dh / DepthHeight, dh - 1) * stride);
                    for (var x = 0; x < DepthWidth; x++)
                    {
                        depth[y * DepthWidth + x] = row[Math.Min(x * dw / DepthWidth, dw - 1)];
                    }
                }
            }
            finally
            {
                depthMap.Unlock(CVPixelBufferLock.ReadOnly);
            }
        }

        // Intrinsics are for the full camera image; scale them to the keyframe's size.
        var k = frame.Camera.Intrinsics;
        var sx = (float)keyframeWidth / width;
        var sy = (float)keyframeHeight / height;
        return new ScanKeyframe(keyframeWidth, keyframeHeight, rgb, ToNumerics(frame.Camera.Transform),
            k.M11 * sx, k.M22 * sy, k.M13 * sx, k.M23 * sy, frame.Timestamp,
            depth == null ? 0 : DepthWidth, depth == null ? 0 : DepthHeight, depth);
    }
}
