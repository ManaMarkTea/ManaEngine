using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaCore.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Flights;
using ManaGeoTools.Code.Planet;
using ManaUI;
using Silk.NET.Input;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// Picks one aircraft out of a planet's flight traffic -- a click on its marker, or its callsign in the panel's search
/// (<see cref="FlightPanelManaView"/>, hosted here) -- and draws it over the globe: the path it has flown (OpenSky's
/// track), a dashed great circle on to its destination, the two airports and a ring round the aircraft.
/// </summary>
public sealed class FlightSelectionOverlay : Canvas
{
    private const float PickReach = 10.0f;
    private const float Inset = 12.0f;
    private const int RouteSegments = 64;

    private static readonly Vector4 Shadow = new(0.0f, 0.0f, 0.0f, 0.65f);
    private static readonly Vector4 Flown = new(1.0f, 1.0f, 1.0f, 0.95f);
    private static readonly Vector4 Ahead = new(1.0f, 0.75f, 0.3f, 0.9f);
    private static readonly Vector4 Dim = new(0.78f, 0.82f, 0.88f, 1.0f);

    private readonly Func<CameraComponent?> _camera;
    private readonly UiElement _panel;

    public FlightSelectionOverlay(UiTheme theme, Func<CameraComponent?> camera)
    {
        _camera = camera;
        IsHitTestVisible = true;
        _panel = FlightPanelManaView.Build(theme, this).At(Inset, Inset);
        Children.Add(_panel);
    }

    /// <summary>What the last search found nothing for, said under the search field.</summary>
    public string? NotFound { get; private set; }

    public void ClearNotFound() => NotFound = null;

    /// <summary>select_flight: a flight by callsign (or ICAO address), as the search box finds it; no name clears the pick.</summary>
    public static void RegisterCommand(Mana.Code.Automation.ManaCommandRunner runner, ManaWorld world)
    {
        // A flight by callsign (or ICAO address), as the viewport's search box finds it; no name clears the pick.
        runner.Register("select_flight", command =>
        {
            if (FindTraffic(world) is not { } found)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic("Command file: select_flight found no flight traffic, skipping.");
                return;
            }
            if (!string.IsNullOrWhiteSpace(command.Path))
            {
                found.Traffic.UseRecordedSnapshot(OpenSkyStates.Parse(File.ReadAllBytes(command.Path)));
            }
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                found.Traffic.Select(null);
                return;
            }
            if (found.Traffic.Find(command.Name) is not { } state)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: select_flight found no flight '{command.Name}'.");
                return;
            }
            Focus(world.MainCamera, found.Planet, found.Traffic, state);
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: flight {state.Callsign} ({state.Icao24}) selected.");
        });
    }

    /// <summary>The first planet in the world carrying flight traffic, if any.</summary>
    internal static (PlanetNode Planet, FlightTrafficComponent Traffic)? FindTraffic(ManaWorld world)
    {
        foreach (var scene in world.Scenes)
        {
            foreach (var child in scene.Children)
            {
                if (child is not PlanetNode planet)
                {
                    continue;
                }
                foreach (var component in planet.Components)
                {
                    if (component is FlightTrafficComponent { Enabled: true } traffic)
                    {
                        return (planet, traffic);
                    }
                }
            }
        }
        return null;
    }

    public (PlanetNode Planet, FlightTrafficComponent Traffic)? Current()
    {
        if (_camera() is not { } camera)
        {
            return null;
        }
        return FindTraffic(camera.Context.World);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _panel.Visibility = Visibility.Collapsed;
        if (Current() != null)
        {
            _panel.Visibility = Visibility.Visible;
        }
        base.MeasureOverride(availableSize);
        return Vector2.Zero;
    }

    // The panel takes the pointer, and so does an aircraft under it; everywhere else goes on to the viewport.
    public override UiElement? HitTest(Vector2 point)
    {
        if (!Bounds.Contains(point) || Current() is not { } found)
        {
            return null;
        }
        if (base.HitTest(point) is { } child && child != this)
        {
            return child;
        }
        if (!LookButtonDown() && PickAt(found, point) != null)
        {
            return this;
        }
        return null;
    }

    // Aircraft take only left clicks: a right or middle press on one (most of the map, zoomed out) is the camera's look or pan.
    private bool LookButtonDown()
    {
        if (_camera() is not { Context: { } context } || context.Input.Mice.FirstOrDefault() is not { } mouse)
        {
            return false;
        }
        return mouse.IsButtonPressed(MouseButton.Right) || mouse.IsButtonPressed(MouseButton.Middle);
    }

    protected override void OnPointerPressed(UiPointerEventArgs e)
    {
        if (e.Button != PointerButton.Left || Current() is not { } found)
        {
            return;
        }
        if (PickAt(found, e.Position) is { } icao24)
        {
            found.Traffic.Select(icao24);
            e.Handled = true;
        }
    }

    private string? PickAt((PlanetNode Planet, FlightTrafficComponent Traffic) found, Vector2 point)
    {
        if (Root == null || _camera() is not { } camera)
        {
            return null;
        }
        var project = Projector(camera, found.Planet);
        return found.Traffic.Pick(local => project(local, false), point, PickReach);
    }

    // Local planet space to the screen; null behind the camera, or (when asked) over the horizon.
    private delegate Vector2? ProjectPoint(Vector3 local, bool horizon, ManaPosition? preciseWorld = null);

    private ProjectPoint Projector(CameraComponent camera, PlanetNode planet)
    {
        // Relative to the camera, as the renderer draws: through the full matrix a point 6,400 km out rounds to half a
        // metre, and the ring round a followed aircraft shook against it.
        var view = camera._ViewMatrix;
        view.Translation = Vector3.Zero;
        var viewProjection = view * camera._ProjectionMatrix;
        var cameraAt = camera.AttachedRig?.PreciseCameraPosition ?? new ManaPosition(camera.Owner.WorldTransform.Translation);
        var window = new Vector2(Root!.Size.X, Root.Size.Y);
        var world = planet.WorldTransform;
        var origin = planet.PreciseWorldPosition ?? new ManaPosition(world.Translation);
        Matrix4x4.Invert(world, out var toLocal);
        var eye = Vector3.Transform(camera.Owner.WorldTransform.Translation, toLocal);
        var radius = planet.Radius;
        return (local, horizon, preciseWorld) =>
        {
            // Seen past the planet's limb: the line from the eye to the point dips under the sphere.
            if (horizon && Vector3.Dot(local, eye - local) < 0.0f && Vector3.Dot(Vector3.Normalize(local), eye) < radius)
            {
                return null;
            }
            var relative = ((preciseWorld ?? (new ManaPosition(local).TransformOffset(world) + origin)) - cameraAt).ToVector3();
            if (VoxelPlacementDepthOverlay.TryProject(viewProjection, window, relative, out var screen, out var depth)
                && depth >= 0 && depth <= 1)
            {
                return screen;
            }
            return null;
        };
    }

    /// <summary>Selects the flight a callsign names and puts the camera above it.</summary>
    public void Search(string text)
    {
        if (Current() is not { } found || string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (found.Traffic.Find(text) is not { } state)
        {
            NotFound = $"No flight {text.Trim().ToUpperInvariant()} in the feed";
            return;
        }
        if (_camera() is { } camera)
        {
            Focus(camera, found.Planet, found.Traffic, state);
        }
    }

    /// <summary>
    /// Selects <paramref name="state"/>: the traffic's follow rig glides the camera to it, or (with following off) the
    /// camera is put above it, high enough to see the route round it.
    /// </summary>
    public static void Focus(CameraComponent camera, PlanetNode planet, FlightTrafficComponent traffic, FlightState state)
    {
        traffic.Select(state.Icao24);
        if (traffic.FollowSelected)
        {
            return;
        }
        var (center, heading) = traffic.Locate(planet, state);
        var up = Vector3.Normalize(center);
        Matrix4x4.Invert(planet.WorldTransform, out var toLocal);
        var eye = Vector3.Transform(camera.Owner.WorldTransform.Translation, toLocal);
        var height = Math.Clamp(eye.Length() - planet.Radius, planet.Radius * 0.02f, planet.Radius * 0.1f);
        // A little behind it, so the view leans along its way rather than straight down.
        var from = center + up * height - heading * (height * 0.6f);
        var owner = camera.Owner;
        owner.Position = Vector3.Transform(from, planet.WorldTransform);
        var forward = Vector3.Normalize(Vector3.Transform(center, planet.WorldTransform) - owner.Position);
        owner.Rotation = new Vector3(MathF.Asin(Math.Clamp(forward.Y, -1.0f, 1.0f)), MathF.Atan2(-forward.X, -forward.Z), 0.0f);
        owner.UpdateTransform();
    }

    protected override void OnRender(ManaUiDrawList drawList)
    {
        if (Root == null || _camera() is not { } camera || Current() is not { } found)
        {
            return;
        }

        drawList.PushClip(Bounds);
        try
        {
            if (found.Traffic.Selected is not { } state)
            {
                return;
            }
            var project = Projector(camera, found.Planet);
            DrawPaths(drawList, found.Planet, found.Traffic, state, project);
        }
        finally
        {
            drawList.PopClip();
        }
    }

    private void DrawPaths(ManaUiDrawList drawList, PlanetNode planet, FlightTrafficComponent traffic, FlightState state,
        ProjectPoint project)
    {
        var (here, _) = traffic.Locate(planet, state);
        var details = traffic.SelectedDetails;

        // Flown: the track's points, then on to where it is now.
        if (details is { Track.Length: > 0 })
        {
            var points = new List<Vector3>();
            foreach (var point in details.Track)
            {
                points.Add(traffic.Place(planet, point.Latitude, point.Longitude, point.AltitudeMeters));
            }
            points.Add(here);
            Polyline(drawList, points, project, Flown, dashed: false);
        }

        // Ahead: a great circle down to the destination -- the scheduled one this aircraft is flying, or the airport
        // ahead it is descending toward.
        var route = traffic.SelectedRoute;
        if (route?.To is { } destination)
        {
            var end = traffic.Place(planet, destination.Latitude, destination.Longitude, 0.0f);
            Polyline(drawList, GreatCircle(here, end), project, Ahead, dashed: true);
            Pin(drawList, end, destination.Code, project, Ahead);
        }
        if (route?.From is { } origin)
        {
            Pin(drawList, traffic.Place(planet, origin.Latitude, origin.Longitude, 0.0f), origin.Code, project, Dim);
        }

        // Use the rendered origin for both the model and the fallback marker.
        if (project(here, true, traffic.SelectedRenderPosition) is { } at)
        {
            var ring = new UiRect(at.X - 14.0f, at.Y - 14.0f, 28.0f, 28.0f);
            drawList.DrawBorder(ring, Shadow, 4.0f, new CornerRadius(14.0f));
            drawList.DrawBorder(ring, Flown, 2.0f, new CornerRadius(14.0f));
        }
    }

    // Along the sphere between two points, the height eased from the first's to the second's.
    private static List<Vector3> GreatCircle(Vector3 from, Vector3 to)
    {
        var a = Vector3.Normalize(from);
        var b = Vector3.Normalize(to);
        var heightA = from.Length();
        var heightB = to.Length();
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1.0f, 1.0f));
        var points = new List<Vector3>();
        for (var i = 0; i <= RouteSegments; i++)
        {
            var t = (float)i / RouteSegments;
            Vector3 direction;
            if (angle < 1e-5f)
            {
                direction = a;
            }
            else
            {
                direction = (a * MathF.Sin((1 - t) * angle) + b * MathF.Sin(t * angle)) / MathF.Sin(angle);
            }
            points.Add(Vector3.Normalize(direction) * float.Lerp(heightA, heightB, t * t * (3 - 2 * t)));
        }
        return points;
    }

    private static void Polyline(ManaUiDrawList drawList, List<Vector3> points, ProjectPoint project, Vector4 colour, bool dashed)
    {
        Vector2? previous = null;
        var length = 0.0f;
        foreach (var point in points)
        {
            var screen = project(point, true);
            if (screen is { } b && previous is { } a)
            {
                var step = Vector2.Distance(a, b);
                // Dashes of 8 pixels on, 6 off, measured along the whole line.
                var draw = !dashed || (length % 14.0f) < 8.0f;
                length += step;
                if (draw)
                {
                    drawList.DrawLine(a, b, Shadow, 4.0f);
                    drawList.DrawLine(a, b, colour, 2.0f);
                }
            }
            previous = screen;
        }
    }

    private void Pin(ManaUiDrawList drawList, Vector3 local, string code, ProjectPoint project, Vector4 colour)
    {
        if (project(local, true) is not { } at)
        {
            return;
        }
        var dot = new UiRect(at.X - 5.0f, at.Y - 5.0f, 10.0f, 10.0f);
        drawList.FillRect(dot, colour, new CornerRadius(5.0f));
        drawList.DrawBorder(dot, Shadow, 1.5f, new CornerRadius(5.0f));
        Text(drawList, code, at + new Vector2(8.0f, -8.0f), colour, small: false);
    }

    private TextLayout Layout(string text, bool small)
    {
        var theme = Root!.Theme;
        TextLayout layout;
        if (small)
        {
            layout = new TextLayout { Font = Root.Fonts.Get(theme.FontFamily, theme.FontSize + 1, Root.Scale), Text = text };
        }
        else
        {
            layout = new TextLayout { Font = Root.Fonts.Get(theme.FontFamilyMedium, theme.FontSize + 4, Root.Scale), Text = text };
        }
        layout.Update();
        return layout;
    }

    private void Text(ManaUiDrawList drawList, string text, Vector2 at, Vector4 colour, bool small)
    {
        var layout = Layout(text, small);
        layout.Render(drawList, new UiRect(at + new Vector2(1.5f, 1.5f), layout.Size), Shadow);
        layout.Render(drawList, new UiRect(at, layout.Size), colour);
    }
}
