#if !MANA_BROWSER
using System.Numerics;
using Mana;
using Mana.Code.Levels;
using ManaLidar;
using ManaUI;
using ManaEditor.Code.LiveScan;
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// The Live Scan panel (Panels > Live Scan): listens for the phone while it is open, shows what has streamed in, picks
/// how the live mesh is coloured, and bakes the scan into the level with the full textured export.
/// </summary>
public static class LiveScanManaView
{
    public const string PanelId = "livescan";

    public static UiElement Build(UiTheme theme, ManaWorld world, VoxelEditorViewModel editor)
    {
        // Opening the panel does not listen: listening opens a network port, which is what makes Windows ask about
        // its firewall, so that waits for the button.
        // Looked up each time, not once: the panel can be built before the editor makes the session.
        static LiveScanSession? Session() => LiveScanSession.Current;

        var status = new TextBlock().Wrap();
        status.BindPolled(t => t.Text, () => Status(Session()));

        var counts = new TextBlock().FontFamily(theme.FontFamilyMono).Foreground(theme.TextMuted);
        counts.BindPolled(t => t.Text, () => Session() is not { } session ? string.Empty
            : $"{session.PieceCount} pieces  ·  {session.TriangleCount:N0} triangles  ·  {session.PhotoCount} photos"
              + (session.Server is { } server ? $"\n{server.BytesReceived / (1024.0 * 1024.0):0.0} MB received" : string.Empty));

        var colours = new StackPanel().Horizontal(4.0f).Children(
            Chip("Photos", "Each point the colour the photos show there.",
                () => Session()?.Colour == LiveScanSession.Colouring.Photos, () => SetColour(Session(), LiveScanSession.Colouring.Photos)),
            Chip("Coverage", "Red where no photo has seen it, amber where only poorly, green where well: where to point the phone next.",
                () => Session()?.Colour == LiveScanSession.Colouring.Coverage, () => SetColour(Session(), LiveScanSession.Colouring.Coverage)));

        var detail = ScanDetail.High;
        var details = new StackPanel().Horizontal(4.0f);
        foreach (var level in Enum.GetValues<ScanDetail>())
        {
            var value = level;
            details.Children.Add(Chip(value.ToString(), $"{ScanDetailSettings.TextureSize(value)} texture for the baked scan.",
                () => detail == value, () => detail = value));
        }

        var bakeState = new TextBlock().Foreground(theme.TextMuted).Wrap();
        var bakeMessage = string.Empty;
        var bakeProgress = 0f;
        bakeState.BindPolled(t => t.Text, () => Session()?.Baking == true ? $"Baking... {bakeProgress:P0}" : bakeMessage);

        var bake = new Button().Label("Bake into the level").Variant(ButtonVariant.Primary).OnClick(() =>
        {
            if (Session() is not { } session)
            {
                return;
            }
            var folder = Path.GetFullPath("Assets/Lidar");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Live {DateTime.Now:yyyy-MM-dd HH.mm.ss}.glb");
            bakeMessage = string.Empty;
            session.Bake(path, detail, new Progress<float>(p => bakeProgress = p), (file, at) =>
            {
                // On the editor thread (the session runs this from its Update): the scene is safe to touch.
                var model = world.LoadFromGltf(file, into: editor.ActiveScene ?? world.DefaultScene);
                model.LevelSource = ManaLevelSource.ForMesh(file);
                model.Position = at;
                session.Clear();
                bakeMessage = $"Baked into the level as {Path.GetFileName(file)}.";
            }, error => bakeMessage = $"Baking failed: {error}");
        });
        bake.BindPolled(b => b.IsEnabled, () => Session() is { Baking: false, PieceCount: > 0 });

        var receive = new Button().Variant(ButtonVariant.Primary).OnClick(() =>
        {
            if (Session() is not { } session)
            {
                return;
            }
            if (session.Listening)
            {
                session.Stop();
            }
            else
            {
                session.Start();
            }
        });
        receive.BindPolled(b => b.Text, () => Session()?.Listening == true ? "Stop receiving" : "Receive from the phone");
        receive.WithTooltip("Opens this PC to the phone on the local network (Windows may ask once whether to allow it).");

        var clear = new Button().Label("Clear").OnClick(() => Session()?.Clear());
        clear.WithTooltip("Forget everything streamed so far (the phone keeps its own scan).");

        return new StackPanel().Vertical(8.0f).Margin(theme.Spacing).Children(
            PreferencesManaView.Heading(theme, "Live Scan"),
            receive,
            status,
            counts,
            PreferencesManaView.Heading(theme, "Colour"),
            colours,
            PreferencesManaView.Heading(theme, "Bake"),
            details,
            new StackPanel().Horizontal(8.0f).Children(bake, clear),
            bakeState,
            new TextBlock().Text("On the phone: ManaLidar > Stream, then pick this PC. The scan appears here as you walk; "
                    + "measure it with the ruler as it grows.")
                .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap())
            .Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
    }

    private static string Status(LiveScanSession? session)
    {
        if (session == null)
        {
            return "Live scans need the desktop editor.";
        }
        if (session.StatusMessage is { } message)
        {
            return message;
        }
        if (session.Server is not { } server)
        {
            return "Not receiving. Press Receive from the phone when you are ready to stream.";
        }
        var where = string.Join(", ", server.Addresses);
        if (server.Phone is { } phone)
        {
            return $"Streaming from {phone}.";
        }
        return $"Waiting for the phone. {(server.Announced ? $"This PC shows up as \"Mana on {Environment.MachineName}\"; or" : "Enter")} "
            + $"the address {where} in the app.";
    }

    private static void SetColour(LiveScanSession? session, LiveScanSession.Colouring colour)
    {
        if (session != null)
        {
            session.Colour = colour;
        }
    }

    private static Button Chip(string label, string tooltip, Func<bool> on, Action click)
    {
        var chip = new Button
        {
            IgnoreThemePadding = true,
            Padding = new Thickness(8.0f, 0.0f, 8.0f, 0.0f),
            Height = 24.0f
        }.Label(label).OnClick(click);
        chip.WithTooltip(tooltip);
        chip.BindPolled(b => b.Variant, () => on() ? ButtonVariant.Primary : ButtonVariant.Subtle);
        return chip;
    }
}

/// <summary>Where the phone is while it streams: a small camera pyramid in the viewport, pointing the way it looks.</summary>
public sealed class LiveScanOverlay : UiElement
{
    private static readonly Vector4 PhoneColour = new(0.35f, 0.85f, 1.0f, 1.0f);
    private readonly Func<CameraComponent?> _camera;

    public LiveScanOverlay(Func<CameraComponent?> camera)
    {
        _camera = camera;
        IsHitTestVisible = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    protected override void OnRender(ManaUiDrawList drawList)
    {
        if (Root == null || LiveScanSession.Current?.PhonePose is not { } pose || _camera() is not { } camera
            || LiveScanSession.Current.Server?.Phone == null)
        {
            return;
        }
        var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        var size = Root.Size;
        bool Project(Vector3 world, out Vector2 screen) =>
            VoxelPlacementDepthOverlay.TryProject(viewProjection, size, world, out screen, out var depth) && depth >= 0 && depth <= 1;

        // A phone held upright looks down its -Z; a 4:3 picture 25 cm out.
        var eye = Vector3.Transform(Vector3.Zero, pose);
        var corners = new[] { new Vector3(-0.15f, -0.11f, -0.25f), new Vector3(0.15f, -0.11f, -0.25f), new Vector3(0.15f, 0.11f, -0.25f), new Vector3(-0.15f, 0.11f, -0.25f) }
            .Select(c => Vector3.Transform(c, pose)).ToArray();
        if (!Project(eye, out var e))
        {
            return;
        }
        var shadow = new Vector4(0, 0, 0, 0.6f);
        void Edge(Vector3 a, Vector3 b)
        {
            if (Project(a, out var p) && Project(b, out var q))
            {
                drawList.DrawLine(p, q, shadow, 3.5f);
                drawList.DrawLine(p, q, PhoneColour, 1.6f);
            }
        }
        for (var i = 0; i < 4; i++)
        {
            Edge(eye, corners[i]);
            Edge(corners[i], corners[(i + 1) % 4]);
        }
        drawList.FillRect(new UiRect(e.X - 5, e.Y - 5, 10, 10), shadow, new CornerRadius(5));
        drawList.FillRect(new UiRect(e.X - 3.5f, e.Y - 3.5f, 7, 7), PhoneColour, new CornerRadius(3.5f));
    }
}
#endif
