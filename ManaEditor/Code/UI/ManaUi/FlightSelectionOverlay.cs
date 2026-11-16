using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Flights;
using ManaGeoTools.Code.Planet;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// Picks one aircraft out of a planet's flight traffic -- a click on its marker, or its callsign in the search box --
/// and shows it: a card with its route, airframe and flight data, the path it has flown (OpenSky's track) and a
/// dashed great circle on to its destination.
/// </summary>
public sealed class FlightSelectionOverlay : Canvas
{
    private const float PickReach = 10.0f;
    private const float Inset = 12.0f;
    private const float CardWidth = 340.0f;
    private const int RouteSegments = 64;

    private static readonly Vector4 Shadow = new(0.0f, 0.0f, 0.0f, 0.65f);
    private static readonly Vector4 Card = new(0.04f, 0.06f, 0.1f, 0.86f);
    private static readonly Vector4 Flown = new(1.0f, 1.0f, 1.0f, 0.95f);
    private static readonly Vector4 Ahead = new(1.0f, 0.75f, 0.3f, 0.9f);
    private static readonly Vector4 Dim = new(0.78f, 0.82f, 0.88f, 1.0f);
    private static readonly Vector4 Stale = new(0.55f, 0.58f, 0.62f, 1.0f);

    private readonly Func<CameraComponent?> _camera;
    private readonly TextBox _search;
    private UiRect _card;
    private UiRect _close;
    private string? _notFound;

    public FlightSelectionOverlay(Func<CameraComponent?> camera)
    {
        _camera = camera;
        IsHitTestVisible = true;
        _search = new TextBox { Placeholder = "Find a flight (callsign)", Width = 240.0f }.At(Inset, Inset);
        _search.TextCommitted += (_, text) => Search(text);
        _search.TextChanged += (_, _) => _notFound = null;
        Children.Add(_search);
    }

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

    private (PlanetNode Planet, FlightTrafficComponent Traffic)? Current()
    {
        if (_camera() is not { } camera)
        {
            return null;
        }
        return FindTraffic(camera.Context.World);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _search.Visibility = Visibility.Collapsed;
        if (Current() != null)
        {
            _search.Visibility = Visibility.Visible;
        }
        base.MeasureOverride(availableSize);
        return Vector2.Zero;
    }

    // The search box and the card take the pointer, and so does an aircraft under it; everywhere else goes on to the viewport.
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
        if (found.Traffic.SelectedIcao24 != null && _card.Contains(point))
        {
            return this;
        }
        if (PickAt(found, point) != null)
        {
            return this;
        }
        return null;
    }

    protected override void OnPointerPressed(UiPointerEventArgs e)
    {
        if (e.Button != PointerButton.Left || Current() is not { } found)
        {
            return;
        }
        if (found.Traffic.SelectedIcao24 != null && _card.Contains(e.Position))
        {
            if (_close.Contains(e.Position))
            {
                found.Traffic.Select(null);
            }
            e.Handled = true;
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
    private Func<Vector3, bool, Vector2?> Projector(CameraComponent camera, PlanetNode planet)
    {
        var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        var window = new Vector2(Root!.Size.X, Root.Size.Y);
        var world = planet.WorldTransform;
        Matrix4x4.Invert(world, out var toLocal);
        var eye = Vector3.Transform(camera.Owner.WorldTransform.Translation, toLocal);
        var radius = planet.Radius;
        return (local, horizon) =>
        {
            // Seen past the planet's limb: the line from the eye to the point dips under the sphere.
            if (horizon && Vector3.Dot(local, eye - local) < 0.0f && Vector3.Dot(Vector3.Normalize(local), eye) < radius)
            {
                return null;
            }
            if (VoxelPlacementDepthOverlay.TryProject(viewProjection, window, Vector3.Transform(local, world), out var screen, out var depth)
                && depth >= 0 && depth <= 1)
            {
                return screen;
            }
            return null;
        };
    }

    private void Search(string text)
    {
        if (Current() is not { } found || string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (found.Traffic.Find(text) is not { } state)
        {
            _notFound = $"No flight {text.Trim().ToUpperInvariant()} in the feed";
            return;
        }
        if (_camera() is { } camera)
        {
            Focus(camera, found.Planet, found.Traffic, state);
        }
    }

    /// <summary>Selects <paramref name="state"/> and puts the camera above it, high enough to see the route round it.</summary>
    public static void Focus(CameraComponent camera, PlanetNode planet, FlightTrafficComponent traffic, FlightState state)
    {
        traffic.Select(state.Icao24);
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
        _card = default;
        if (Root == null || _camera() is not { } camera || Current() is not { } found)
        {
            return;
        }

        drawList.PushClip(Bounds);
        try
        {
            if (_notFound != null)
            {
                Text(drawList, _notFound, new Vector2(Bounds.X + Inset + 250.0f, Bounds.Y + Inset + 4.0f), Ahead, small: true);
            }
            if (found.Traffic.Selected is not { } state)
            {
                return;
            }
            var project = Projector(camera, found.Planet);
            DrawPaths(drawList, found.Planet, found.Traffic, state, project);
            DrawCard(drawList, found.Traffic, state);
        }
        finally
        {
            drawList.PopClip();
        }
    }

    private void DrawPaths(ManaUiDrawList drawList, PlanetNode planet, FlightTrafficComponent traffic, FlightState state,
        Func<Vector3, bool, Vector2?> project)
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

        // Ahead: a great circle down to the destination, when the route on record is this trip's.
        if (details?.Destination is { } destination && OnRoute(details, state))
        {
            var end = traffic.Place(planet, destination.Latitude, destination.Longitude, 0.0f);
            Polyline(drawList, GreatCircle(here, end), project, Ahead, dashed: true);
            Pin(drawList, end, destination.Code, project, Ahead);
        }
        if (details?.Origin is { } origin)
        {
            Pin(drawList, traffic.Place(planet, origin.Latitude, origin.Longitude, 0.0f), origin.Code, project, Dim);
        }

        // A bracket round the aircraft itself.
        if (project(here, true) is { } at)
        {
            var ring = new UiRect(at.X - 14.0f, at.Y - 14.0f, 28.0f, 28.0f);
            drawList.DrawBorder(ring, Shadow, 4.0f, new CornerRadius(14.0f));
            drawList.DrawBorder(ring, Flown, 2.0f, new CornerRadius(14.0f));
        }
    }

    // adsbdb knows a callsign's usual route; a reused one can be on another trip. Off its great circle by more than
    // this, the route is shown as the usual one and not drawn.
    private const float OffRouteKilometres = 400.0f;

    private static bool OnRoute(FlightDetails details, FlightState state)
    {
        if (details.Origin is not { } origin || details.Destination is not { } destination)
        {
            return false;
        }
        var from = new LatLong(origin.Longitude, origin.Latitude).ToUnitSphere();
        var to = new LatLong(destination.Longitude, destination.Latitude).ToUnitSphere();
        var here = new LatLong(state.Longitude, state.Latitude).ToUnitSphere();
        var normal = Vector3.Cross(from, to);
        if (normal.LengthSquared() < 1e-10f)
        {
            return true;
        }
        var off = MathF.Asin(Math.Clamp(MathF.Abs(Vector3.Dot(here, Vector3.Normalize(normal))), 0.0f, 1.0f)) * PlanetLoader.EarthRadiusMeters / 1000.0f;
        return off < OffRouteKilometres;
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

    private static void Polyline(ManaUiDrawList drawList, List<Vector3> points, Func<Vector3, bool, Vector2?> project, Vector4 colour, bool dashed)
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

    private void Pin(ManaUiDrawList drawList, Vector3 local, string code, Func<Vector3, bool, Vector2?> project, Vector4 colour)
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

    private void DrawCard(ManaUiDrawList drawList, FlightTrafficComponent traffic, FlightState state)
    {
        var details = traffic.SelectedDetails;
        var lines = new List<(string Text, Vector4 Colour, bool Small)>();
        var stale = -1;
        var title = state.Callsign;
        if (title.Length == 0)
        {
            title = state.Icao24.ToUpperInvariant();
        }
        if (details?.Airline is { } airline)
        {
            title += "  " + airline;
        }
        lines.Add((title, Vector4.One, false));

        if (details == null)
        {
            lines.Add(("Looking up its route and path...", Dim, true));
        }
        else
        {
            if (details.Origin is { } origin && details.Destination is { } destination)
            {
                // A route the aircraft is nowhere near is the callsign's usual one, not this trip: greyed and badged.
                var route = $"{origin.Code} {origin.City}  ->  {destination.Code} {destination.City}";
                if (OnRoute(details, state))
                {
                    lines.Add((route, Ahead, true));
                }
                else
                {
                    stale = lines.Count;
                    lines.Add((route, Stale, true));
                }
            }
            else
            {
                lines.Add(("No route on record for this callsign", Dim, true));
            }
            var airframe = string.Join("  ", new[] { details.AircraftType, details.Registration }.Where(part => !string.IsNullOrEmpty(part)));
            if (airframe.Length > 0)
            {
                lines.Add((airframe, Dim, true));
            }
        }

        var feet = state.AltitudeMeters * 3.28084f;
        var knots = state.SpeedMetersPerSecond * 1.94384f;
        var climb = state.VerticalRateMetersPerSecond * 196.85f;
        var vertical = "level";
        if (climb > 100.0f)
        {
            vertical = $"climbing {climb:N0} ft/min";
        }
        else if (climb < -100.0f)
        {
            vertical = $"descending {-climb:N0} ft/min";
        }
        if (state.OnGround)
        {
            lines.Add(($"On the ground  {knots:N0} kt", Vector4.One, true));
        }
        else
        {
            lines.Add(($"{feet:N0} ft ({state.AltitudeMeters:N0} m)  {knots:N0} kt  heading {state.TrackDegrees:000}", Vector4.One, true));
            lines.Add((vertical, Dim, true));
        }
        var age = Math.Max(0.0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - state.PositionTime);
        var track = "";
        if (details is { Track.Length: > 0 })
        {
            track = $"  path of {details.Track.Length} points";
        }
        lines.Add(($"ICAO {state.Icao24}  reported {FormatAge(age)} ago{track}", Dim, true));

        // Measured, then boxed under the search field.
        const float pad = 10.0f;
        var layouts = lines.Select(line => (Layout: Layout(line.Text, line.Small), line.Colour)).ToList();
        var height = pad * 2 + layouts.Sum(line => line.Layout.Size.Y + 3.0f);
        _card = new UiRect(Bounds.X + Inset, Bounds.Y + Inset + 40.0f, CardWidth, height);
        drawList.FillRect(_card, Card, new CornerRadius(8.0f));
        drawList.FillRect(new UiRect(_card.X, _card.Y, 3.0f, _card.Height), Flown, new CornerRadius(1.5f));
        var y = _card.Y + pad;
        for (var i = 0; i < layouts.Count; i++)
        {
            var (layout, colour) = layouts[i];
            layout.Render(drawList, new UiRect(_card.X + pad + 4.0f, y, layout.Size.X, layout.Size.Y), colour);
            if (i == stale)
            {
                Badge(drawList, new Vector2(_card.X + pad + 10.0f + layout.Size.X, y), layout.Size.Y);
            }
            y += layout.Size.Y + 3.0f;
        }

        _close = new UiRect(_card.Right - 26.0f, _card.Y + 6.0f, 20.0f, 20.0f);
        var cross = Layout("x", small: false);
        cross.Render(drawList, new UiRect(_close.Center - cross.Size * 0.5f, cross.Size), Dim);
    }

    // An amber "?" pill: the route beside it is probably not this trip's.
    private void Badge(ManaUiDrawList drawList, Vector2 at, float height)
    {
        var mark = Layout("?", small: true);
        var pill = new UiRect(at.X, at.Y, MathF.Max(height, mark.Size.X + 8.0f), height);
        drawList.FillRect(pill, Ahead, new CornerRadius(height * 0.5f));
        mark.Render(drawList, new UiRect(pill.Center - mark.Size * 0.5f, mark.Size), Card);
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

    private static string FormatAge(double seconds)
    {
        if (seconds >= 90.0)
        {
            return $"{seconds / 60.0:0} min";
        }
        return $"{seconds:0} s";
    }
}
