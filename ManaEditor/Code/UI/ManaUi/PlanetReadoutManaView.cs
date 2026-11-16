using System.Globalization;
using Mana;
using Mana.Code.Components;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Planet;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// Where the camera is over a planet, bottom left of the view: its height above sea level, the latitude and longitude
/// below it and the compass heading it faces. Shown only while the world has a planet.
/// </summary>
public static class PlanetReadoutManaView
{
    private const float FeetPerMetre = 3.28084f;
    private const float MetresPerMile = 1609.344f;

    /// <summary>Where the camera is; <see cref="TerrainMetres"/> and <see cref="ImageryMetres"/> are how fine the ground below is drawn (0 unknown).</summary>
    public readonly record struct Fix(float AltitudeMetres, float Latitude, float Longitude, float? Heading,
        float TerrainMetres = 0.0f, float ImageryMetres = 0.0f, int? Zoom = null, bool DetailImagery = false);

    public static UiElement Build(UiTheme theme, Func<CameraComponent?> camera)
    {
        Fix? current = null;
        Fix? Read()
        {
            current = Locate(camera());
            return current;
        }

        var tiles = new StackPanel().Horizontal(8.0f).Children(
            Tile(theme, "ALTITUDE", () => Altitude(current), () => AltitudeDetail(current)),
            Tile(theme, "POSITION", () => Latitude(current), () => Longitude(current)),
            Tile(theme, "HEADING", () => Heading(current), () => Compass(current)),
            Tile(theme, "GROUND", () => Ground(current), () => GroundDetail(current)));

        var panel = new Border()
            .Fill(theme.Frosted(theme.Surface))
            .Blur(theme.BackdropBlur)
            .Outline(theme.BorderStrong, 1.0f)
            .Rounded(theme.CornerRadiusLarge)
            .Shadow(18.0f)
            .PaddingOf(8.0f)
            .Content(tiles)
            .Left()
            .Bottom()
            .Margin(12.0f, 0.0f, 0.0f, 12.0f);
        // Read once a frame here; the tiles below show what this found.
        panel.BindPolled(el => el.Visibility, () =>
        {
            if (Read() == null)
            {
                return Visibility.Collapsed;
            }
            return Visibility.Visible;
        });
        return panel;
    }

    /// <summary>The camera's fix over the first planet in its world, or null with no planet.</summary>
    public static Fix? Locate(CameraComponent? camera)
    {
        if (camera == null || FindPlanet(camera.Context.World) is not { } planet
            || !Matrix4x4.Invert(planet.WorldTransform, out var worldToLocal))
        {
            return null;
        }

        var local = Vector3.Transform(camera.Owner.Position, worldToLocal);
        if (local.LengthSquared() < 1e-6f)
        {
            return null;
        }
        var fix = LatLong.FromPointOnSphere(local, planet.Radius, 1.0f);
        var result = new Fix(fix.Altitude, fix.Latitude, fix.Longitude, HeadingOf(Vector3.Normalize(local), Vector3.TransformNormal(camera.Owner.Forward, worldToLocal)));
        if (planet.SurveyBelow(camera.Owner.Position) is { } survey)
        {
            _lastSurvey = survey;
        }
        if (_lastSurvey is { } below)
        {
            var imagery = 0.0f;
            if (below.Detail is { MetresPerPixel: > 0.0f } detail)
            {
                imagery = detail.MetresPerPixel;
            }
            else if (planet.ImageryPath != null && planet.GetComponent<PlanetImageryComponent>() is { Width: > 0 } whole)
            {
                imagery = 2.0f * MathF.PI * PlanetLoader.EarthRadiusMeters / whole.Width;
            }
            result = result with
            {
                TerrainMetres = below.VertexMetres,
                ImageryMetres = imagery,
                Zoom = below.Zoom,
                DetailImagery = below.Detail != null
            };
        }
        return result;
    }

    // The last survey, kept for the frames the planet's rebuild lock is busy.
    private static PlanetNode.PatchSurvey? _lastSurvey;

    private static string Ground(Fix? fix)
    {
        if (fix is not { TerrainMetres: > 0.0f } f)
        {
            return "--";
        }
        return "mesh " + Distance(f.TerrainMetres);
    }

    private static string GroundDetail(Fix? fix)
    {
        if (fix is not { } f || f.ImageryMetres <= 0.0f)
        {
            return "";
        }
        var source = "global";
        if (f.DetailImagery)
        {
            source = "tile";
        }
        var zoom = "baseline";
        if (f.Zoom is { } z)
        {
            zoom = string.Create(CultureInfo.InvariantCulture, $"z{z}");
        }
        return $"image {Distance(f.ImageryMetres)} {source} · {zoom}";
    }

    private static string Distance(float metres)
    {
        if (metres >= 1000.0f)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{metres / 1000.0f:0.#} km");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{metres:0} m");
    }

    /// <summary>Compass degrees (0 north, 90 east) of <paramref name="forward"/> over the point <paramref name="up"/>; null at a pole or looking straight down.</summary>
    public static float? HeadingOf(Vector3 up, Vector3 forward)
    {
        // The planet's +Y is its north pole; east is north turned about the way up (lat/long's +longitude).
        var north = Vector3.UnitY - up * up.Y;
        var level = forward - up * Vector3.Dot(forward, up);
        if (north.LengthSquared() < 1e-8f || level.LengthSquared() < 1e-6f * forward.LengthSquared())
        {
            return null;
        }
        north = Vector3.Normalize(north);
        var east = Vector3.Cross(north, up);
        var degrees = MathF.Atan2(Vector3.Dot(level, east), Vector3.Dot(level, north)) * 180.0f / MathF.PI;
        if (degrees < 0.0f)
        {
            degrees += 360.0f;
        }
        return degrees;
    }

    private static PlanetNode? FindPlanet(ManaWorld world)
    {
        foreach (var scene in world.Scenes)
        {
            foreach (var child in scene.Children)
            {
                if (child is PlanetNode planet)
                {
                    return planet;
                }
            }
        }
        return null;
    }

    private static string Altitude(Fix? fix)
    {
        if (fix is not { } f)
        {
            return "";
        }
        if (MathF.Abs(f.AltitudeMetres) < 10000.0f)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{f.AltitudeMetres:N0} m");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{f.AltitudeMetres / 1000.0f:N0} km");
    }

    private static string AltitudeDetail(Fix? fix)
    {
        if (fix is not { } f)
        {
            return "";
        }
        if (MathF.Abs(f.AltitudeMetres) < 30000.0f)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{f.AltitudeMetres * FeetPerMetre:N0} ft");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{f.AltitudeMetres / MetresPerMile:N0} mi");
    }

    private static string Latitude(Fix? fix)
    {
        if (fix is not { } f)
        {
            return "";
        }
        return Degrees(f.Latitude, "N", "S");
    }

    private static string Longitude(Fix? fix)
    {
        if (fix is not { } f)
        {
            return "";
        }
        return Degrees(f.Longitude, "E", "W");
    }

    private static string Degrees(float value, string positive, string negative)
    {
        var letter = positive;
        if (value < 0.0f)
        {
            letter = negative;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{MathF.Abs(value):0.0000}° {letter}");
    }

    private static string Heading(Fix? fix)
    {
        if (fix?.Heading is not { } heading)
        {
            return "--";
        }
        return string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(heading) % 360:000}°");
    }

    private static string Compass(Fix? fix)
    {
        if (fix?.Heading is not { } heading)
        {
            return "";
        }
        string[] points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return points[(int)MathF.Round(heading / 45.0f) % 8];
    }

    private static UiElement Tile(UiTheme theme, string label, Func<string> value, Func<string> detail)
    {
        return new Border()
            .Fill(theme.Frosted(theme.SurfaceAlt))
            .Outline(theme.Border, 1.0f)
            .Rounded(theme.CornerRadius)
            .PaddingOf(10.0f, 7.0f)
            .Content(new StackPanel().Vertical(1.0f).Children(
                new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Text(label),
                new TextBlock().FontSize(theme.FontSize + 3.0f).FontFamily(theme.FontFamilyMedium).Foreground(theme.Text).BindTextPolled(value),
                new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).BindTextPolled(detail)));
    }
}
