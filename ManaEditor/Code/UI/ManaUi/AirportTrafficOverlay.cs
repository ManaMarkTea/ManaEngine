using Mana;
using Mana.Code.Components;
using Mana.Code.UI;
using ManaGeoTools.Code.Flights;
using ManaGeoTools.Code.Planet;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// The busiest airports under a planet's flight traffic, ringed and labelled with the aircraft coming and going
/// ("DFW +162"): rings grow and redden with the count. Labels that would overlap a busier one are left off.
/// </summary>
public sealed class AirportTrafficOverlay : UiElement
{
    private static readonly Vector4 Shadow = new(0.0f, 0.0f, 0.0f, 0.6f);
    private static readonly Vector4 Backing = new(0.02f, 0.04f, 0.08f, 0.55f);
    private static readonly Vector4 Quiet = new(1.0f, 0.86f, 0.25f, 1.0f);
    private static readonly Vector4 Busy = new(1.0f, 0.5f, 0.15f, 1.0f);
    private static readonly Vector4 Busiest = new(1.0f, 0.22f, 0.15f, 1.0f);

    private readonly Func<CameraComponent?> _camera;
    private readonly List<UiRect> _labels = new();

    public AirportTrafficOverlay(Func<CameraComponent?> camera)
    {
        _camera = camera;
        IsHitTestVisible = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    protected override void OnRender(ManaUiDrawList drawList)
    {
        if (Root == null || _camera() is not { } camera)
        {
            return;
        }

        var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        var window = new Vector2(Root.Size.X, Root.Size.Y);
        var cameraPosition = camera.Owner.WorldTransform.Translation;
        drawList.PushClip(Bounds);
        try
        {
            foreach (var scene in camera.Context.World.Scenes)
            {
                foreach (var child in scene.Children)
                {
                    if (child is not PlanetNode planet)
                    {
                        continue;
                    }

                    foreach (var component in planet.Components)
                    {
                        if (component is FlightTrafficComponent { Enabled: true, ShowAirports: true } traffic)
                        {
                            Draw(drawList, planet, traffic, cameraPosition, viewProjection, window);
                        }
                    }
                }
            }
        }
        finally
        {
            drawList.PopClip();
        }
    }

    private void Draw(ManaUiDrawList drawList, PlanetNode planet, FlightTrafficComponent traffic, Vector3 cameraPosition, Matrix4x4 viewProjection, Vector2 window)
    {
        var airports = traffic.BusyAirports;
        var shown = Math.Min(airports.Count, Math.Max(0, traffic.AirportCount));
        if (shown == 0 || !Matrix4x4.Invert(planet.WorldTransform, out var toLocal))
        {
            return;
        }

        var camera = Vector3.Transform(cameraPosition, toLocal);
        var busiest = (float)airports[0].Count;
        var placed = new List<(AirportCount Airport, Vector2 Screen, float Radius, Vector4 Colour)>();
        for (var i = 0; i < shown; i++)
        {
            var airport = airports[i];
            // Past the horizon the planet hides it.
            if (Vector3.Dot(airport.Airport.Direction, camera) < planet.Radius)
            {
                continue;
            }

            var world = Vector3.Transform(airport.Airport.Direction * planet.Radius, planet.WorldTransform);
            if (!VoxelPlacementDepthOverlay.TryProject(viewProjection, window, world, out var screen, out var depth) || depth < 0 || depth > 1)
            {
                continue;
            }

            var share = airport.Count / busiest;
            var radius = 7.0f + 2.6f * MathF.Sqrt(airport.Count);
            placed.Add((airport, screen, radius, Ramp(share)));
        }

        // Quietest first, so the busy rings sit on top; labels busiest first, so they win any overlap.
        for (var i = placed.Count - 1; i >= 0; i--)
        {
            var (_, screen, radius, colour) = placed[i];
            var ring = new UiRect(screen.X - radius, screen.Y - radius, radius * 2.0f, radius * 2.0f);
            drawList.DrawBorder(ring, Shadow, 4.5f, new CornerRadius(radius));
            drawList.DrawBorder(ring, colour, 2.5f, new CornerRadius(radius));
        }

        _labels.Clear();
        foreach (var (airport, screen, radius, colour) in placed)
        {
            Label(drawList, airport, screen, radius, colour);
        }
    }

    private void Label(ManaUiDrawList drawList, AirportCount airport, Vector2 screen, float radius, Vector4 colour)
    {
        var theme = Root!.Theme;
        var font = Root.Fonts.Get(theme.FontFamilyMedium, theme.FontSize + 4, Root.Scale);
        var code = new TextLayout { Font = font, Text = airport.Airport.Code };
        var count = new TextLayout { Font = font, Text = $" +{airport.Count}" };
        code.Update();
        count.Update();
        var width = code.Size.X + count.Size.X;
        var height = MathF.Max(code.Size.Y, count.Size.Y);
        var at = new Vector2(screen.X + radius + 5.0f, screen.Y - height * 0.5f);
        var box = new UiRect(at.X, at.Y, width, height);
        foreach (var taken in _labels)
        {
            if (taken.IntersectsWith(box))
            {
                return;
            }
        }

        _labels.Add(box);
        // A dark backing: the labels sit on thick traffic, which no shadow alone reads over.
        drawList.FillRect(new UiRect(box.X - 4.0f, box.Y - 1.0f, box.Width + 8.0f, box.Height + 2.0f), Backing, new CornerRadius(4.0f));
        var shadow = new Vector2(1.5f, 1.5f);
        code.Render(drawList, new UiRect(at + shadow, code.Size), Shadow);
        count.Render(drawList, new UiRect(at + shadow + new Vector2(code.Size.X, 0.0f), count.Size), Shadow);
        code.Render(drawList, new UiRect(at, code.Size), Vector4.One);
        count.Render(drawList, new UiRect(at + new Vector2(code.Size.X, 0.0f), count.Size), colour);
    }

    // Yellow for a quiet airport, through orange, to red for the busiest in view.
    private static Vector4 Ramp(float share)
    {
        if (share < 0.5f)
        {
            return Vector4.Lerp(Quiet, Busy, share * 2.0f);
        }

        return Vector4.Lerp(Busy, Busiest, (share - 0.5f) * 2.0f);
    }
}
