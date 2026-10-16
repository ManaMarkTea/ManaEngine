using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Mana.Code.Content;
using Mana.Code.GameModes;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.GameModes;

public sealed class CodeScreensaverMode : ManaLevelGameMode
{
    /// <summary>Number of terminal columns across the code wall.</summary>
    [Range(2, 8)]
    public int Columns { get; set; } = 4;

    /// <summary>Average source lines passing each second.</summary>
    [Range(0.1, 20)]
    public float ScrollSpeed { get; set; } = 2.4f;

    /// <summary>Zero chooses a fresh arrangement each time play starts.</summary>
    public int Seed { get; set; }

    /// <summary>Soft keyboard ambience; zero silences it.</summary>
    [Range(0, 1)]
    public float KeyboardVolume { get; set; } = 0.16f;

    private CodeWall? _wall;
    private TerminalTypingSounds? _typing;
    public override bool KeepsEditorFloor => false;
    public override bool ShowsPlayHud => false;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ManaLevelGameModes.Register(new()
    {
        Type = "CodeScreensaver", DisplayName = "Code Screensaver",
        Description = "A drifting wall of terminals scrolling real ManaRenderer source files.",
        ModeType = typeof(CodeScreensaverMode), Create = () => new CodeScreensaverMode()
    });

    public override void BeginPlay(ManaPlaySession session)
    {
        _wall = new CodeWall(Math.Clamp(Columns, 2, 8), Seed, SourceFiles.Load());
        _typing = new TerminalTypingSounds(session.Context.Audio, Seed);
    }

    public override UiElement? BuildHud(UiTheme theme) => _wall;
    public override void Tick(float deltaSeconds)
    {
        _wall?.Advance(deltaSeconds, Math.Clamp(ScrollSpeed, 0.1f, 20));
        _typing?.Advance(deltaSeconds, Math.Clamp(KeyboardVolume, 0, 1));
    }
    public override void Suspend() => _typing?.Stop();
    public override void EndPlay()
    {
        _typing?.Stop();
        _typing = null;
        _wall = null;
    }

    private sealed record SourceFile(string Path, string[] Lines);

    private static class SourceFiles
    {
        public static SourceFile[] Load()
        {
            var files = new List<SourceFile>();
            var root = Directory.GetParent(ManaContentAssets.Root)?.FullName ?? ManaContentAssets.Root;
            // Only source directories: never scan local settings, build outputs or user documents.
            foreach (var folder in new[] { "Mana/Code", "ManaCore", "ManaUI/Code", "ManaGraphics/Code", "ManaGraphics/Assets/Shaders/WebGpu", "ManaVoxel/Code", "Games" })
            {
                var directory = System.IO.Path.Combine(root, folder);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".wgsl", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal))
                {
                    try { Add(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            if (files.Count == 0)
            {
                var assembly = typeof(CodeScreensaverMode).Assembly;
                foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("TerminalSource/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                {
                    using var stream = assembly.GetManifestResourceStream(name)!;
                    using var reader = new StreamReader(stream);
                    Add(name["TerminalSource/".Length..].Replace('\\', '/').Replace("../", ""), reader.ReadToEnd());
                }
            }
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Code screensaver: loaded {files.Count} source files ({(Directory.Exists(System.IO.Path.Combine(root, "Mana", "Code")) ? "checkout" : "bundled")}).");
            return files.ToArray();

            void Add(string path, string text)
            {
                var lines = text.Replace("\r", "").Replace("\t", "    ").Split('\n');
                if (lines.Length >= 24) files.Add(new(path, lines));
            }
        }
    }

    private sealed class CodeWall : UiElement
    {
        private static readonly Vector4 Ink = new(0.72f, 0.8f, 0.83f, 1);
        private static readonly Vector4 Green = new(0.42f, 0.91f, 0.57f, 1);
        private static readonly Vector4 Red = new(1, 0.36f, 0.39f, 1);
        private static readonly Vector4 Muted = new(0.28f, 0.4f, 0.45f, 1);
        private readonly SourceFile[] _sources;
        private readonly Random _random;
        private readonly List<Terminal> _terminals = [];
        private float _time;
        private float _scale, _originX, _originY;

        private sealed class Terminal
        {
            public required SourceFile Source;
            public float X, Y, Width, Height, Scroll, Rate, Phase;
            public int Agent;
            public bool Background;
        }

        public CodeWall(int columns, int seed, SourceFile[] sources)
        {
            IsHitTestVisible = false;
            _sources = sources;
            _random = seed == 0 ? new Random() : new Random(seed);
            if (sources.Length == 0) return;
            // A second, dimmer wall extends past the foreground rather than exposing empty corners.
            for (var layer = 0; layer < 2; layer++)
            for (var col = 0; col < columns + 1; col++)
            for (var row = 0; row < 3; row++)
            {
                var width = 1680f / columns;
                var terminal = new Terminal
                {
                    Source = Pick(), X = col * width - 110 + (layer == 0 ? -width * .4f : 0),
                    Y = row * 410 - 170 + (col % 2) * 65 + (layer == 0 ? 80 : 0),
                    Width = width - (layer == 0 ? 28 : 10), Height = 390 + _random.Next(35),
                    Scroll = 0, Rate = .5f + _random.NextSingle() * 1.5f,
                    Phase = _random.NextSingle() * 20, Agent = _terminals.Count + 1, Background = layer == 0
                };
                terminal.Scroll = _random.Next(terminal.Source.Lines.Length);
                _terminals.Add(terminal);
            }
        }

        private SourceFile Pick() => _sources[_random.Next(_sources.Length)];

        public void Advance(float delta, float speed)
        {
            _time += delta;
            foreach (var terminal in _terminals)
            {
                var burst = MathF.Sin(_time * .65f + terminal.Phase);
                terminal.Scroll += delta * speed * terminal.Rate * (burst > .65f ? 3 : burst < -.7f ? .08f : 1);
                if (terminal.Scroll >= terminal.Source.Lines.Length)
                {
                    terminal.Source = Pick();
                    terminal.Scroll = 0;
                }
            }
            InvalidateVisual();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize) => new(
            float.IsFinite(availableSize.X) ? availableSize.X : 1600,
            float.IsFinite(availableSize.Y) ? availableSize.Y : 900);

        protected override void OnRender(ManaUiDrawList draw)
        {
            if (Root == null) return;
            draw.FillRect(Bounds, new Vector4(.004f, .009f, .016f, 1));
            _scale = MathF.Max(Bounds.Width / 1440, Bounds.Height / 820);
            _originX = Bounds.X + Bounds.Width / 2 - 780 * _scale + MathF.Sin(_time * .045f) * 42 * _scale;
            _originY = Bounds.Y + Bounds.Height / 2 - 440 * _scale + MathF.Cos(_time * .057f) * 26 * _scale;
            var font = Root.Fonts.Get(Root.Theme.FontFamilyMono, 12, Root.Scale);
            draw.PushClip(Bounds);
            foreach (var terminal in _terminals) RenderTerminal(draw, font, terminal);
            if (_sources.Length == 0)
                Text(draw, font, "No source files available", 400, 400, Red, 1);
            draw.PopClip();
        }

        private Vector2 Point(float x, float y) => new(_originX + x * _scale, _originY + y * _scale);

        private void Box(ManaUiDrawList draw, float x, float y, float width, float height, Vector4 color)
        {
            var p = Point(x, y);
            draw.FillRect(new UiRect(p.X, p.Y, width * _scale, height * _scale), color);
        }

        private void RenderTerminal(ManaUiDrawList draw, ManaFont font, Terminal t)
        {
            var x = t.X;
            var y = t.Y + MathF.Sin(_time * .09f + t.Phase) * (t.Background ? 12 : 5);
            var accent = t.Agent % 5 == 0 ? Red : Green;
            draw.PushOpacity(t.Background ? .28f : 1);
            Box(draw, x + 5, y + 8, t.Width, t.Height, new Vector4(0, 0, 0, .65f));
            Box(draw, x, y, t.Width, t.Height, new Vector4(.013f, .025f, .037f, 1));
            Box(draw, x, y, t.Width, 25, new Vector4(.027f, .045f, .057f, 1));
            draw.DrawLine(Point(x, y), Point(x + t.Width, y), Muted, _scale);
            draw.DrawLine(Point(x, y), Point(x, y + t.Height), accent * new Vector4(1, 1, 1, .65f), _scale);
            draw.DrawLine(Point(x, y + t.Height), Point(x + t.Width, y + t.Height), Muted, _scale);
            var capacity = Math.Max(8, (int)((t.Width - 28) / 6.3f));
            Text(draw, font, $"> agent/{t.Agent:00}   ~/{t.Source.Path}", x + 10, y + 6, accent, .9f, capacity);
            var offset = t.Scroll - MathF.Floor(t.Scroll);
            var rows = (int)((t.Height - 60) / 14);
            for (var row = 0; row < rows; row++)
            {
                var line = ((int)t.Scroll + row) % t.Source.Lines.Length;
                var top = y + 37 + row * 14 - offset * 14;
                Text(draw, font, (line + 1).ToString("0000"), x + 10, top, Muted, .85f);
                Text(draw, font, t.Source.Lines[line], x + 44, top, Ink, 1, Math.Max(1, capacity - 6), true);
            }
            Box(draw, x + 1, y + t.Height - 24, t.Width - 2, 23, new Vector4(.02f, .038f, .047f, 1));
            Text(draw, font, $"> reading source   {((int)t.Scroll + 1):0000}/{t.Source.Lines.Length}   " + (((int)(_time * 2 + t.Phase) % 2 == 0) ? "_" : ""),
                x + 10, y + t.Height - 18, accent, .85f, capacity);
            draw.PopOpacity();
        }

        private void Text(ManaUiDrawList draw, ManaFont font, string text, float x, float y, Vector4 color,
            float size, int max = 200, bool syntax = false)
        {
            var pixelScale = Root!.Scale;
            font = Root.Fonts.Get(Root.Theme.FontFamilyMono,
                MathF.Round(12 * size * _scale * pixelScale) / pixelScale, pixelScale);
            var origin = Point(x, y);
            var pen = 0f;
            var quoted = false;
            var comment = false;
            for (var i = 0; i < Math.Min(text.Length, max); i++)
            {
                var ch = text[i];
                if (syntax && ch == '/' && i + 1 < text.Length && text[i + 1] == '/') comment = true;
                if (ch == '"') quoted = !quoted;
                var tint = !syntax ? color : comment ? Muted : quoted || ch == '"' ? Green
                    : char.IsDigit(ch) ? Red : "{}[]();".Contains(ch) ? new Vector4(.39f, .64f, .7f, 1) : color;
                var glyph = font.GetGlyph(ch);
                if (glyph.HasPixels)
                {
                    var px = MathF.Round((origin.X + pen + glyph.Offset.X) * pixelScale) / pixelScale;
                    var py = MathF.Round((origin.Y + font.Ascent + glyph.Offset.Y) * pixelScale) / pixelScale;
                    draw.DrawGlyph(new UiRect(px, py, glyph.Size.X, glyph.Size.Y), glyph.AtlasUv, tint);
                }
                pen += glyph.Advance;
            }
        }
    }
}
