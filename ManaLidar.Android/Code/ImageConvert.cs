using System.Numerics;
using Google.AR.Core;

namespace ManaLidar.Droid;

/// <summary>ARCore's YUV camera image and 16-bit depth image into a <see cref="ScanKeyframe"/>.</summary>
public static class ImageConvert
{
    private const int DepthWidth = 128;
    private const int DepthHeight = 96;

    public static ScanKeyframe ToKeyframe(global::Android.Media.Image color, global::Android.Media.Image depth, CameraIntrinsics intrinsics,
        Matrix4x4 cameraToWorld, double seconds, int width, int height)
    {
        var rgb = ToRgb(color, width, height);
        var depthMetres = ToDepth(depth);

        // Intrinsics are for the full camera image; scale them to the keyframe's size.
        var dimensions = intrinsics.GetImageDimensions();
        var scaleX = width / (float)dimensions[0];
        var scaleY = height / (float)dimensions[1];
        var focal = intrinsics.GetFocalLength();
        var principal = intrinsics.GetPrincipalPoint();
        return new ScanKeyframe(width, height, rgb, cameraToWorld, focal[0] * scaleX, focal[1] * scaleY,
            principal[0] * scaleX, principal[1] * scaleY, seconds, DepthWidth, DepthHeight, depthMetres);
    }

    /// <summary>YUV_420_888 (BT.601 full range, as camera2 delivers it) sampled down to width x height.</summary>
    private static byte[] ToRgb(global::Android.Media.Image image, int width, int height)
    {
        var planes = image.GetPlanes()!;
        var y = Copy(planes[0]);
        var u = Copy(planes[1]);
        var v = Copy(planes[2]);
        int yStride = planes[0].RowStride, cStride = planes[1].RowStride, cPixel = planes[1].PixelStride;
        var rgb = new byte[width * height * 3];
        for (var oy = 0; oy < height; oy++)
        {
            var sy = Math.Min((int)((oy + 0.5f) * image.Height / height), image.Height - 1);
            for (var ox = 0; ox < width; ox++)
            {
                var sx = Math.Min((int)((ox + 0.5f) * image.Width / width), image.Width - 1);
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
    private static float[] ToDepth(global::Android.Media.Image image)
    {
        var plane = image.GetPlanes()![0];
        var bytes = Copy(plane);
        var stride = plane.RowStride;
        var result = new float[DepthWidth * DepthHeight];
        for (var oy = 0; oy < DepthHeight; oy++)
        {
            var sy = Math.Min(oy * image.Height / DepthHeight, image.Height - 1);
            for (var ox = 0; ox < DepthWidth; ox++)
            {
                var sx = Math.Min(ox * image.Width / DepthWidth, image.Width - 1);
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
