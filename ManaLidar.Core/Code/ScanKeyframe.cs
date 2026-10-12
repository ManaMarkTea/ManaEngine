using System.Numerics;

namespace ManaLidar;

/// <summary>
/// A camera photo kept while scanning, small, with the LiDAR depth seen at the same instant -- what the mesh is
/// coloured from afterwards. Camera space is ARKit's: X right, Y up, looking down -Z, image rows going down.
/// </summary>
public sealed class ScanKeyframe
{
    public ScanKeyframe(int width, int height, byte[] rgb, Matrix4x4 cameraToWorld,
        float fx, float fy, float cx, float cy, double timestamp,
        int depthWidth = 0, int depthHeight = 0, float[]? depth = null)
    {
        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException("RGB must be width * height * 3 bytes.", nameof(rgb));
        }
        if (depth != null && depth.Length != depthWidth * depthHeight)
        {
            throw new ArgumentException("Depth must be depthWidth * depthHeight values.", nameof(depth));
        }

        Width = width;
        Height = height;
        Rgb = rgb;
        CameraToWorld = cameraToWorld;
        Fx = fx;
        Fy = fy;
        Cx = cx;
        Cy = cy;
        Timestamp = timestamp;
        DepthWidth = depthWidth;
        DepthHeight = depthHeight;
        Depth = depth;
        Matrix4x4.Invert(cameraToWorld, out var worldToCamera);
        WorldToCamera = worldToCamera;
        Sharpness = MeasureSharpness(width, height, rgb);
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgb { get; }
    /// <summary>System.Numerics convention (row vectors, translation in M41..M43).</summary>
    public Matrix4x4 CameraToWorld { get; }
    public Matrix4x4 WorldToCamera { get; }
    /// <summary>Pinhole intrinsics in pixels of this keyframe's RGB image, not the full camera image.</summary>
    public float Fx { get; }
    public float Fy { get; }
    public float Cx { get; }
    public float Cy { get; }
    public double Timestamp { get; }
    /// <summary>Metres along the view axis, covering the same view as the RGB image; null when the phone gave none.</summary>
    public float[]? Depth { get; }
    public int DepthWidth { get; }
    public int DepthHeight { get; }

    /// <summary>Mean luma gradient: a photo taken mid-swing is smeared and scores low, so colouring leans on the crisp ones.</summary>
    public float Sharpness { get; }

    public Vector3 Position => CameraToWorld.Translation;
    /// <summary>The way the camera looks (its -Z) in world space.</summary>
    public Vector3 Forward => Vector3.Normalize(-new Vector3(CameraToWorld.M31, CameraToWorld.M32, CameraToWorld.M33));

    /// <summary>Projects a world point to image pixels; false when it is behind the camera.</summary>
    public bool Project(Vector3 world, out Vector2 pixel, out float distance)
    {
        var p = Vector3.Transform(world, WorldToCamera);
        distance = -p.Z;
        if (distance <= 1e-3f)
        {
            pixel = default;
            return false;
        }
        pixel = new Vector2(Cx + Fx * p.X / distance, Cy - Fy * p.Y / distance);
        return true;
    }

    /// <summary>The LiDAR depth under an image pixel, or NaN when there is no depth map.</summary>
    public float DepthAt(Vector2 pixel)
    {
        if (Depth == null)
        {
            return float.NaN;
        }
        var x = Math.Clamp((int)(pixel.X / Width * DepthWidth), 0, DepthWidth - 1);
        var y = Math.Clamp((int)(pixel.Y / Height * DepthHeight), 0, DepthHeight - 1);
        return Depth[y * DepthWidth + x];
    }

    /// <summary>Bilinear RGB at an image pixel, 0..1.</summary>
    public Vector3 Sample(Vector2 pixel)
    {
        var x = Math.Clamp(pixel.X - 0.5f, 0, Width - 1.001f);
        var y = Math.Clamp(pixel.Y - 0.5f, 0, Height - 1.001f);
        var x0 = (int)x;
        var y0 = (int)y;
        var fx = x - x0;
        var fy = y - y0;
        var a = Texel(x0, y0);
        var b = Texel(x0 + 1, y0);
        var c = Texel(x0, y0 + 1);
        var d = Texel(x0 + 1, y0 + 1);
        return Vector3.Lerp(Vector3.Lerp(a, b, fx), Vector3.Lerp(c, d, fx), fy) / 255f;
    }

    private static float MeasureSharpness(int width, int height, byte[] rgb)
    {
        static int Luma(byte[] rgb, int i) => rgb[i] * 2 + rgb[i + 1] * 5 + rgb[i + 2];
        long sum = 0;
        long count = 0;
        // Every other row and column is plenty for a score.
        for (var y = 0; y < height - 1; y += 2)
        {
            for (var x = 0; x < width - 1; x += 2)
            {
                var i = (y * width + x) * 3;
                var l = Luma(rgb, i);
                sum += Math.Abs(l - Luma(rgb, i + 3)) + Math.Abs(l - Luma(rgb, i + width * 3));
                count++;
            }
        }
        return count == 0 ? 0 : sum / (8f * count);
    }

    private Vector3 Texel(int x, int y)
    {
        x = Math.Min(x, Width - 1);
        y = Math.Min(y, Height - 1);
        var i = (y * Width + x) * 3;
        return new Vector3(Rgb[i], Rgb[i + 1], Rgb[i + 2]);
    }
}
