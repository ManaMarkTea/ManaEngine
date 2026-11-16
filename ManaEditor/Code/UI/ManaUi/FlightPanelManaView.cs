using ManaGeoTools.Code.Flights;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// The flight traffic's panel over the viewport: a frosted card with the live aircraft count, where the traffic comes
/// from (a pill: the relay, OpenSky directly, or nothing -- the reason in its tooltip), the callsign search and, once
/// a flight is picked, its route, its numbers in tiles, its airframe and how fresh its report is.
/// </summary>
public static class FlightPanelManaView
{
    public const float Width = 340.0f;

    public static UiElement Build(UiTheme theme, FlightSelectionOverlay overlay)
    {
        FlightTrafficComponent? Traffic() => overlay.Current()?.Traffic;
        FlightState? Selected() => Traffic()?.Selected;
        FlightDetails? Details() => Traffic()?.SelectedDetails;

        var search = new TextBox { Placeholder = "Search a callsign -- UAL2095, DAL785" }.StretchH();
        search.TextCommitted += (_, text) => overlay.Search(text);
        search.TextChanged += (_, _) => overlay.ClearNotFound();
        var notFound = Small(theme, theme.Warning);
        notFound.BindTextPolled(() => overlay.NotFound ?? "");
        notFound.BindPolled(el => el.Visibility, () => Shown(overlay.NotFound != null));

        var header = new Grid().Columns("*,Auto", theme.Spacing).Children(
            new StackPanel().Vertical(1.0f).CenterV().Children(
                Small(theme, theme.TextMuted).Text("LIVE FLIGHTS"),
                Medium(theme, theme.FontSize + 2.0f, theme.Text).BindTextPolled(() => $"{Traffic()?.AircraftShown ?? 0:N0} aircraft in view"))
                .GridColumn(0),
            FeedPill(theme, Traffic).GridColumn(1).CenterV());

        var card = FlightCard(theme, Traffic, Selected, Details, overlay);
        card.BindPolled(el => el.Visibility, () => Shown(Selected() != null));

        var panel = new Border()
            .Fill(theme.Frosted(theme.Surface))
            .Blur(theme.BackdropBlur)
            .Outline(theme.BorderStrong, 1.0f)
            .Rounded(theme.CornerRadiusLarge)
            .Shadow(18.0f)
            .PaddingOf(12.0f)
            .Content(new StackPanel().Vertical(10.0f).Children(header, search, notFound, card));
        panel.Width = Width;
        return panel;
    }

    // Green on the relay (or this machine's own account), amber on a fallback, red with nothing live.
    private static UiElement FeedPill(UiTheme theme, Func<FlightTrafficComponent?> traffic)
    {
        (string Label, Vector4 Tone, UiTone Light) State()
        {
            var t = traffic();
            if (t == null)
            {
                return ("Offline", theme.Danger, UiTone.Danger);
            }
            if (t.Source == FlightFeedSource.None)
            {
                return ("Connecting", theme.Warning, UiTone.Warning);
            }
            if (t.FeedNote != null)
            {
                if (!t.Live)
                {
                    return ("Offline", theme.Danger, UiTone.Danger);
                }
                if (t.Source == FlightFeedSource.Relay)
                {
                    return ("Relay", theme.Warning, UiTone.Warning);
                }
                return ("Direct", theme.Warning, UiTone.Warning);
            }
            if (t.Source == FlightFeedSource.Relay)
            {
                return ("Relay", theme.Success, UiTone.Success);
            }
            return ("OpenSky", theme.Success, UiTone.Success);
        }

        var light = new StatusLight { IsOn = true, Diameter = 9.0f }.CenterV();
        light.BindPolled(el => el.Tone, () => State().Light);
        var label = Small(theme, theme.Text).CenterV().BindTextPolled(() => State().Label);
        var pill = new Border()
            .Rounded(10.0f)
            .PaddingOf(8.0f, 3.0f)
            .Content(new StackPanel().Horizontal(6.0f).Children(light, label));
        pill.BindPolled(el => el.Background, () => State().Tone * new Vector4(1, 1, 1, 0.16f));
        pill.BindPolled(el => el.BorderColor, () => State().Tone * new Vector4(1, 1, 1, 0.7f));
        pill.BorderThickness = 1.0f;
        // The long story -- which source failed and why -- on hover, not across the screen.
        pill.BindPolled(el => el.Tooltip, () => FeedStory(traffic()));
        return pill;
    }

    private static UiElement FlightCard(UiTheme theme, Func<FlightTrafficComponent?> traffic, Func<FlightState?> selected,
        Func<FlightDetails?> details, FlightSelectionOverlay overlay)
    {
        var close = new ManaUI.Button().Label("×").Size(26.0f, 26.0f).Top().WithTooltip("Let go of this flight");
        close.Click += (_, _) => traffic()?.Select(null);

        var title = new Grid().Columns("*,Auto", theme.Spacing).Children(
            new StackPanel().Vertical(0.0f).Children(
                Medium(theme, theme.FontSizeHeading + 4.0f, theme.Text).BindTextPolled(() => Callsign(selected())),
                Small(theme, theme.TextMuted).Ellipsis().BindTextPolled(() => details()?.Airline ?? Pending(details(), "Airline unknown")))
                .GridColumn(0),
            close.GridColumn(1));

        var route = Route(theme, () => traffic()?.SelectedRoute, details);

        var tiles = new Grid().Columns("*,*", 8.0f).Rows("Auto,Auto", 8.0f).Children(
            Tile(theme, "ALTITUDE", () => Value(selected(), Feet), () => Value(selected(), Metres)).GridCell(0, 0),
            Tile(theme, "SPEED", () => Value(selected(), s => $"{s.SpeedMetersPerSecond * 1.94384f:N0} kt"),
                () => Value(selected(), s => $"{s.SpeedMetersPerSecond * 3.6f:N0} km/h")).GridCell(0, 1),
            Tile(theme, "HEADING", () => Value(selected(), s => $"{s.TrackDegrees:000}°"),
                () => Value(selected(), s => Compass(s.TrackDegrees))).GridCell(1, 0),
            Tile(theme, "VERTICAL", () => Value(selected(), Vertical),
                () => Value(selected(), Climb)).GridCell(1, 1));

        var airframe = Small(theme, theme.Text).Ellipsis().BindTextPolled(() =>
        {
            var d = details();
            var parts = new[] { d?.AircraftType, d?.Registration }.Where(part => !string.IsNullOrEmpty(part)).ToArray();
            if (parts.Length == 0)
            {
                return Pending(d, "Aircraft not on record");
            }
            return string.Join("  ·  ", parts);
        });

        var footer = Small(theme, theme.TextMuted).Ellipsis().BindTextPolled(() =>
        {
            if (selected() is not { } s)
            {
                return "";
            }
            var age = Math.Max(0.0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - s.PositionTime);
            var track = "";
            if (details() is { Track.Length: > 0 } d)
            {
                track = $"  ·  {d.Track.Length} track points";
            }
            return $"ICAO {s.Icao24}  ·  reported {FormatAge(age)} ago{track}";
        });

        var divider = new Border().Fill(theme.Divider).Height(1.0f).StretchH();
        return new StackPanel().Vertical(10.0f).Children(divider, title, route, tiles, airframe, footer);
    }

    // ORD Chicago -> LAX Los Angeles in the accent when it is the scheduled route and this aircraft is flying it. Otherwise
    // what the aircraft shows, in grey with a "?": where its track took off, and the airport ahead it is coming down
    // to ("likely"); a dash for an end not known. "usually PHL -> PIT" names a route on record that was turned down.
    private static UiElement Route(UiTheme theme, Func<FlightRoute?> route, Func<FlightDetails?> details)
    {
        bool Scheduled() => route() is { Scheduled: true };
        bool HasEnds() => route() is { HasEnds: true };

        UiElement End(Func<RouteAirport?> airport, Func<string> note)
        {
            var code = Medium(theme, theme.FontSizeHeading + 2.0f, theme.Text).BindTextPolled(() => airport()?.Code ?? "—");
            code.BindPolled(el => el.Foreground, () =>
            {
                if (Scheduled())
                {
                    return theme.Text;
                }
                return theme.TextMuted;
            });
            var city = Small(theme, theme.TextMuted).Ellipsis().BindTextPolled(() =>
            {
                var name = airport()?.City ?? "unknown";
                var extra = note();
                if (extra.Length == 0)
                {
                    return name;
                }
                return $"{name} · {extra}";
            });
            return new StackPanel().Vertical(0.0f).Children(code, city);
        }

        string Likely()
        {
            if (route() is { ToGuessed: true })
            {
                return "likely";
            }
            return "";
        }

        var arrow = Medium(theme, theme.FontSizeHeading, theme.Accent).Text("→").CenterV();
        arrow.BindPolled(el => el.Foreground, () =>
        {
            if (Scheduled())
            {
                return theme.Accent;
            }
            return theme.TextMuted;
        });
        var seen = new Border().Rounded(9.0f).Size(18.0f, 18.0f).Fill(theme.Warning).CenterV()
            .Content(Small(theme, theme.Background).Text("?").Center())
            .WithTooltip("Not a schedule: where its track took off, and the airport it is coming down toward");
        seen.BindPolled(el => el.Visibility, () => Shown(HasEnds() && !Scheduled()));

        var row = new Grid().Columns("*,Auto,*,Auto", 10.0f).Children(
            End(() => route()?.From, () => "").GridColumn(0),
            arrow.GridColumn(1),
            End(() => route()?.To, Likely).GridColumn(2),
            seen.GridColumn(3));
        row.BindPolled(el => el.Visibility, () => Shown(HasEnds()));

        var none = Small(theme, theme.TextMuted).BindTextPolled(() =>
        {
            if (route() is { Usual: not null })
            {
                return "Route unknown";
            }
            return Pending(details(), "No route on record for this callsign");
        });
        none.BindPolled(el => el.Visibility, () => Shown(!HasEnds()));

        var usual = Small(theme, theme.TextMuted).Ellipsis().BindTextPolled(() => $"usually {route()?.Usual}  ·  not this trip");
        usual.BindPolled(el => el.Visibility, () => Shown(route() is { Usual: not null }));

        return new Border()
            .Fill(theme.Frosted(theme.SurfaceAlt))
            .Outline(theme.Border, 1.0f)
            .Rounded(theme.CornerRadius)
            .PaddingOf(10.0f, 8.0f)
            .Content(new StackPanel().Vertical(2.0f).Children(row, none, usual));
    }

    private static UiElement Tile(UiTheme theme, string label, Func<string> value, Func<string> detail)
    {
        return new Border()
            .Fill(theme.Frosted(theme.SurfaceAlt))
            .Outline(theme.Border, 1.0f)
            .Rounded(theme.CornerRadius)
            .PaddingOf(10.0f, 7.0f)
            .Content(new StackPanel().Vertical(1.0f).Children(
                Small(theme, theme.TextMuted).Text(label),
                Medium(theme, theme.FontSize + 3.0f, theme.Text).BindTextPolled(value),
                Small(theme, theme.TextMuted).BindTextPolled(detail)));
    }

    private static TextBlock Small(UiTheme theme, Vector4 colour) =>
        new TextBlock().FontSize(theme.FontSizeSmall).Foreground(colour);

    private static TextBlock Medium(UiTheme theme, float size, Vector4 colour) =>
        new TextBlock().FontSize(size).FontFamily(theme.FontFamilyMedium).Foreground(colour);

    private static Visibility Shown(bool shown)
    {
        if (shown)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    private static string FeedStory(FlightTrafficComponent? traffic)
    {
        if (traffic == null)
        {
            return "No flight traffic on this level";
        }
        return traffic.FeedNote ?? traffic.Status;
    }

    private static string Feet(FlightState state)
    {
        if (state.OnGround)
        {
            return "On ground";
        }
        return $"{state.AltitudeMeters * 3.28084f:N0} ft";
    }

    private static string Metres(FlightState state)
    {
        if (state.OnGround)
        {
            return "";
        }
        return $"{state.AltitudeMeters:N0} m";
    }

    private static string Climb(FlightState state)
    {
        if (MathF.Abs(state.VerticalRateMetersPerSecond) < 0.5f)
        {
            return "steady";
        }
        return $"{state.VerticalRateMetersPerSecond * 196.85f:+#,0;-#,0} ft/min";
    }

    private static string Value(FlightState? state, Func<FlightState, string> format)
    {
        if (state is { } s)
        {
            return format(s);
        }
        return "";
    }

    private static string Pending(FlightDetails? details, string missing)
    {
        if (details == null)
        {
            return "Looking it up...";
        }
        return missing;
    }

    private static string Callsign(FlightState? state)
    {
        if (state is not { } s)
        {
            return "";
        }
        if (s.Callsign.Length > 0)
        {
            return s.Callsign;
        }
        return s.Icao24.ToUpperInvariant();
    }

    private static string Vertical(FlightState state)
    {
        if (state.OnGround)
        {
            return "Taxiing";
        }
        if (state.VerticalRateMetersPerSecond > 0.5f)
        {
            return "Climbing";
        }
        if (state.VerticalRateMetersPerSecond < -0.5f)
        {
            return "Descending";
        }
        return "Level";
    }

    private static readonly string[] Points = ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    private static string Compass(float degrees)
    {
        var index = (int)MathF.Round(((degrees % 360.0f) + 360.0f) % 360.0f / 45.0f) % 8;
        return Points[index];
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
