namespace ManaLidar;

/// <summary>
/// Shrinks the camera's own picture format -- bi-planar 4:2:0 full-range YCbCr, what ARFrame.CapturedImage is --
/// straight into small RGB, so a keyframe costs a few hundred KB instead of a 1920x1440 frame.
/// </summary>
public static class YCbCrImage
{
    /// <param name="luma">Y plane, one byte a pixel, <paramref name="lumaStride"/> bytes a row.</param>
    /// <param name="chroma">CbCr plane at half size each way, Cb and Cr interleaved.</param>
    /// <param name="rgb">Output, <paramref name="outWidth"/> * <paramref name="outHeight"/> * 3 bytes.</param>
    public static void DownsampleToRgb(ReadOnlySpan<byte> luma, int lumaStride, int width, int height,
        ReadOnlySpan<byte> chroma, int chromaStride, int outWidth, int outHeight, Span<byte> rgb)
    {
        if (rgb.Length < outWidth * outHeight * 3)
        {
            throw new ArgumentException("Output is too small.", nameof(rgb));
        }

        var stepX = (float)width / outWidth;
        var stepY = (float)height / outHeight;
        for (var oy = 0; oy < outHeight; oy++)
        {
            for (var ox = 0; ox < outWidth; ox++)
            {
                // Four luma taps across the block soften the aliasing of plain point sampling at almost no cost.
                var x0 = (int)((ox + 0.25f) * stepX);
                var x1 = Math.Min((int)((ox + 0.75f) * stepX), width - 1);
                var y0 = (int)((oy + 0.25f) * stepY);
                var y1 = Math.Min((int)((oy + 0.75f) * stepY), height - 1);
                var y = (luma[y0 * lumaStride + x0] + luma[y0 * lumaStride + x1]
                    + luma[y1 * lumaStride + x0] + luma[y1 * lumaStride + x1]) * 0.25f;

                var cx = Math.Min((int)((ox + 0.5f) * stepX), width - 1) / 2;
                var cy = Math.Min((int)((oy + 0.5f) * stepY), height - 1) / 2;
                var ci = cy * chromaStride + cx * 2;
                var cb = chroma[ci] - 128f;
                var cr = chroma[ci + 1] - 128f;

                // BT.601 full range, which is what ARKit's 420f buffers carry.
                var o = (oy * outWidth + ox) * 3;
                rgb[o] = ToByte(y + 1.402f * cr);
                rgb[o + 1] = ToByte(y - 0.344136f * cb - 0.714136f * cr);
                rgb[o + 2] = ToByte(y + 1.772f * cb);
            }
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v + 0.5f), 0, 255);
}
