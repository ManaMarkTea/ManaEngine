using ManaGeoTools.Code;
using ManaGeoTools.Code.GeoTiff;
using ManaUI;

namespace ManaTests;

/// <summary>The planet starts from Assets/Planet/baseline.tif (Natural Earth land and lakes), not an elevation survey.</summary>
public static class PlanetBaselineChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Planet");
        var library = new GeoTiffLibrary(folder);
        library.LoadAllImageData();
        check("the baseline ships beside the build and loads", Directory.Exists(folder) && File.Exists(Path.Combine(folder, "baseline.tif")), folder);

        float? Height(float longitude, float latitude) => library.FindCurrentTile(new LatLong(longitude, latitude))?.GetHeightAtLatLong(new LatLong(longitude, latitude));
        var pacific = Height(-150, 0);
        var sahara = Height(15, 25);
        var antarctica = Height(0, -80);
        check("open sea reads well below sea level", pacific is < -1000, $"{pacific}");
        check("a continent reads above it", sahara is > 0 && antarctica is > 0, $"{sahara}, {antarctica}");
        var michigan = Height(-87, 44);
        check("a big lake reads just under sea level, so the ocean fills it", michigan is < 0 and > -1000, $"{michigan}");
        check("and the whole thing is small enough to commit without LFS", new FileInfo(Path.Combine(folder, "baseline.tif")).Length < 1_000_000, "");
    }
}
