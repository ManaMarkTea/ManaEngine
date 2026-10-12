using System.Numerics;
using Google.AR.Core;

namespace ManaLidar.Droid;

/// <summary>ARCore's YUV camera image and 16-bit depth image into a <see cref="ScanKeyframe"/>.</summary>
public static class ImageConvert
{
    private const int DepthWidth = 128;
    private const int DepthHeight = 96;

    /// <summary>The raw bytes of one frame, copied out so the Images can be closed at once and the slow work done elsewhere.</summary>
    public sealed class RawFrame
    {
        public byte[] Y = null!, U = null!, V = null!, Depth = null!;
        public int ImageWidth, ImageHeight, YStride, ChromaStride, ChromaPixel, DepthImageWidth, DepthImageHeight, DepthStride;
        public float[] Focal = null!, Principal = null!;
        public int[] Dimensions = null!;
    }

    public static RawFrame Copy(global::Android.Media.Image color, global::Android.Media.Image depth, CameraIntrinsics intrinsics)
    {
        var planes = color.GetPlanes()!;
        var depthPlane = depth.GetPlanes()![0];
        return new RawFrame
        {
            Y = Copy(planes[0]), U = Copy(planes[1]), V = Copy(planes[2]), Depth = Copy(depthPlane),
            ImageWidth = color.Width, ImageHeight = color.Height,
            YStride = planes[0].RowStride, ChromaStride = planes[1].RowStride, ChromaPixel = planes[1].PixelStride,
            DepthImageWidth = depth.Width, DepthImageHeight = depth.Height, DepthStride = depthPlane.RowStride,
            Focal = intrinsics.GetFocalLength(), Principal = intrinsics.GetPrincipalPoint(), Dimensions = intrinsics.GetImageDimensions(),
        };
    }

    public static ScanKeyframe ToKeyframe(RawFrame raw, Matrix4x4 cameraToWorld, double seconds, int width, int height)
    {
        var rgb = ToRgb(raw, width, height);
        var depthMetres = ToDepth(raw);

        // Intrinsics are for the full camera image; scale them to the keyframe's size.
        var scaleX = width / (float)raw.Dimensions[0];
        var scaleY = height / (float)raw.Dimensions[1];
        var focal = raw.Focal;
        var principal = raw.Principal;
        return new ScanKeyframe(width, height, rgb, cameraToWorld, focal[0] * scaleX, focal[1] * scaleY,
            principal[0] * scaleX, principal[1] * scaleY, seconds, DepthWidth, DepthHeight, depthMetres);
    }

    /// <summary>YUV_420_888 (BT.601 full range, as camera2 delivers it) sampled down to width x height.</summary>
    private static byte[] ToRgb(RawFrame image, int width, int height)
    {
        byte[] y = image.Y, u = image.U, v = image.V;
        int yStride = image.YStride, cStride = image.ChromaStride, cPixel = image.ChromaPixel;
        var rgb = new byte[width * height * 3];
        for (var oy = 0; oy < height; oy++)
        {
            var sy = Math.Min((int)((oy + 0.5f) * image.ImageHeight / height), image.ImageHeight - 1);
            for (var ox = 0; ox < width; ox++)
            {
                var sx = Math.Min((int)((ox + 0.5f) * image.ImageWidth / width), image.ImageWidth - 1);
                float luma = y[sy * yStride + sx];
                var chroma = (sy / 2) * cStride + (sx / 2) * cPixel;
                float cb = u[chroma] - 128f;
                float cr = v[chroma] - 128f;
                var o = (oy * width + ox) * 3;
                rgb[o] = Clamp(luma + 1.402f * cr);
                rgb[o + 1] = Clamp(luma - 0.344136f * cb - 0.714136f * cr);
                rgb[o + 2] = Clamp(luma + 1.772f * cb);
            }
        }
        return rgb;
    }

    /// <summary>DEPTH16: millimetres in the low 13 bits (confidence above them), resampled to a fixed grid in metres; 0 = unknown.</summary>
    private static float[] ToDepth(RawFrame image)
    {
        var bytes = image.Depth;
        var stride = image.DepthStride;
        var result = new float[DepthWidth * DepthHeight];
        for (var oy = 0; oy < DepthHeight; oy++)
        {
            var sy = Math.Min(oy * image.DepthImageHeight / DepthHeight, image.DepthImageHeight - 1);
            for (var ox = 0; ox < DepthWidth; ox++)
            {
                var sx = Math.Min(ox * image.DepthImageWidth / DepthWidth, image.DepthImageWidth - 1);
                var raw = bytes[sy * stride + sx * 2] | (bytes[sy * stride + sx * 2 + 1] << 8);
                result[oy * DepthWidth + ox] = (raw & 0x1FFF) / 1000f;
            }
        }
        return result;
    }

    private static byte[] Copy(global::Android.Media.Image.Plane plane)
    {
        var buffer = plane.Buffer!;
        var bytes = new byte[buffer.Remaining()];
        buffer.Get(bytes);
        return bytes;
    }

    private static byte Clamp(float value) => (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
}
