namespace ManaLidar;

/// <summary>How much detail a scan keeps, traded against memory while scanning, export time and the GLB's size.</summary>
public enum ScanDetail
{
    Standard,
    High,
    Ultra,
}

public static class ScanDetailSettings
{
    /// <summary>Keyframe photo size. The camera's 1920x1440 is shrunk to this as each photo is kept.</summary>
    public static (int Width, int Height) PhotoSize(ScanDetail detail) => detail switch
    {
        ScanDetail.High => (640, 480),
        ScanDetail.Ultra => (960, 720),
        _ => (320, 240),
    };

    /// <summary>Texture width and height: the most detail the scan can carry, whatever its size.</summary>
    public static int TextureSize(ScanDetail detail) => detail switch
    {
        ScanDetail.High => 4096,
        ScanDetail.Ultra => 4096,
        _ => 2048,
    };

    /// <summary>How far simplifying may move the surface (metres): smaller keeps more of the shape's fine relief.</summary>
    public static float SimplifyError(ScanDetail detail) => detail switch
    {
        ScanDetail.High => 0.004f,
        ScanDetail.Ultra => 0.002f,
        _ => 0.008f,
    };

    /// <summary>Export settings for a level of detail.</summary>
    public static ScanExportOptions Export(ScanDetail detail, string name) => new()
    {
        Name = name,
        TextureSize = TextureSize(detail),
        SimplifyError = SimplifyError(detail),
    };

    /// <summary>Keyframes kept before thinning: about 110, 280 and 420 MB of photos.</summary>
    public static int MaxKeyframes(ScanDetail detail) => detail switch
    {
        ScanDetail.High => 300,
        ScanDetail.Ultra => 200,
        _ => 400,
    };
}
