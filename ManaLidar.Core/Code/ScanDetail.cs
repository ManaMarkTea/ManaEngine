namespace ManaLidar;

/// <summary>How much colour detail a scan keeps, traded against memory while scanning and the GLB's size.</summary>
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

    /// <summary>Longest triangle edge in the export (metres): the finest colour detail the GLB can show.</summary>
    public static float MaxEdge(ScanDetail detail) => detail switch
    {
        ScanDetail.High => 0.02f,
        ScanDetail.Ultra => 0.01f,
        _ => 0.04f,
    };

    /// <summary>Keyframes kept before thinning: about 110, 280 and 420 MB of photos.</summary>
    public static int MaxKeyframes(ScanDetail detail) => detail switch
    {
        ScanDetail.High => 300,
        ScanDetail.Ultra => 200,
        _ => 400,
    };
}
