using Mana;
using Mana.Code.Components;
using Mana.Code.Content;
using ManaPixelFx;
using ManaPixelFx.Core;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors;

/// <summary>
/// A live pixel effect: which effect, what it is, where its code lives (the C# port and the pixelfx original), the
/// options it is made from -- palette ramps, seed, sizes -- with this one's changes marked and resettable, the
/// timing and placement, and Bake, which turns it into a sprite sheet and swaps the script for a Sprite Sheet.
/// </summary>
public sealed class PixelFxWidget : InspectorWidget<ManaPixelFxComponent>
{
    private const string BakedFolder = "Sprites/Baked";

    protected override UiElement Build(ManaPixelFxComponent fx, InspectorContext context)
    {
        var theme = context.Theme;
        var rows = new StackPanel().Vertical(theme.Spacing);
        rows.Children.Add(InspectorControls.Row(context, "Effect", EffectPicker(fx), "The pixelfx effect it plays, by id."));

        // Rebuilt when the effect changes: each has options of its own.
        var detail = new DynamicContent
        {
            Template = _ => EffectDetail(fx, context),
        };
        detail.Source = () => fx.Effect;
        rows.Children.Add(detail);

        var reflected = new ReflectedPropertiesWidget();
        reflected.IgnoredDeclaringTypes.Add(typeof(ManaComponent));
        reflected.IgnoredDeclaringTypes.Add(typeof(Mana.Code.ManaObject));
        reflected.IgnoredNames.Add(nameof(fx.Effect));
        var settings = (Expander)reflected.Build(fx, context);
        if (settings.Content is { } content)
        {
            settings.Content = null;
            rows.Children.Add(content);
        }
        rows.Children.Add(BakeRow(fx, context));

        var section = new Expander().Content(rows);
        section.BindPolled(x => x.Header, () => $"Pixel FX: {fx.Definition?.Meta.Name ?? fx.Effect}");
        return section;
    }

    private static ComboBox EffectPicker(ManaPixelFxComponent fx)
    {
        var effects = PixelEffects.All.Where(effect => !effect.Meta.Id.StartsWith("test/", StringComparison.Ordinal)).ToList();
        var combo = new ComboBox().Options(effects.Select(effect => $"{effect.Meta.Name}  ({effect.Meta.Id})").ToArray());
        int Selected() => effects.FindIndex(effect => string.Equals(effect.Meta.Id, fx.Effect, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = Selected();
        combo.SelectionChanged += (_, index) =>
        {
            if (combo.IsApplyingBinding || index < 0 || index >= effects.Count)
            {
                return;
            }
            // Another effect's options mean nothing to this one.
            fx.Effect = effects[index].Meta.Id;
            fx.Options = new();
        };
        combo.BindPolled(c => c.SelectedIndex, Selected);
        return combo;
    }

    private static UiElement EffectDetail(ManaPixelFxComponent fx, InspectorContext context)
    {
        var theme = context.Theme;
        var rows = new StackPanel().Vertical(theme.Spacing);
        if (fx.Definition is not { } effect)
        {
            rows.Children.Add(new TextBlock().Text($"No ported effect '{fx.Effect}'.").Foreground(theme.Warning).Wrap());
            return rows;
        }
        if (effect.Meta.Description.Length > 0)
        {
            rows.Children.Add(new TextBlock().Text(effect.Meta.Description).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Wrap());
        }
        rows.Children.Add(InspectorControls.Row(context, "Code", CodeButtons(effect, theme), "The C# port it runs, and the pixelfx file it was ported from."));

        rows.Children.Add(new TextBlock().Text("Options").Foreground(theme.Accent).FontSize(theme.FontSizeSmall));
        foreach (var (name, fallback) in effect.Meta.Options)
        {
            if (Editor(fx, name, fallback, context) is { } editor)
            {
                var reset = new ManaUI.Button().Label("↺").Variant(ButtonVariant.Subtle).WithTooltip("Back to the effect's own")
                    .OnClick(() => Change(fx, name, null, context));
                reset.BindPolled(b => b.IsEnabled, () => fx.IsOptionChanged(name));
                rows.Children.Add(InspectorControls.Row(context, Humanise(name), editor, reset, OptionHint(name, fallback)));
            }
        }
        return rows;
    }

    private static UiElement CodeButtons(PixelEffect effect, UiTheme theme)
    {
        var port = ComponentSource.Find(effect.GetType());
        var portButton = new ManaUI.Button().Label($"{effect.GetType().Name}.cs").Variant(ButtonVariant.Subtle).Enabled(port != null)
            .WithTooltip(port?.Path ?? "Local source or debug symbols are unavailable.")
            .OnClick(() =>
            {
                if (port != null)
                {
                    ComponentSource.Open(port);
                }
            });

        // The original sits in a pixelfx checkout in References/ beside the repo, or beside it, when there is one.
        var repo = Path.GetDirectoryName(ManaContentAssets.Root) ?? Directory.GetCurrentDirectory();
        var original = Path.GetFullPath(Path.Combine(repo, "..", "References", "pixelfx", PixelEffects.OriginalFile(effect)));
        var besideRepo = Path.GetFullPath(Path.Combine(repo, "..", "pixelfx", PixelEffects.OriginalFile(effect)));
        if (!File.Exists(original) && File.Exists(besideRepo))
        {
            original = besideRepo;
        }
        var hasOriginal = File.Exists(original);
        var originalButton = new ManaUI.Button().Label(Path.GetFileName(original)).Variant(ButtonVariant.Subtle).Enabled(hasOriginal)
            .WithTooltip(hasOriginal ? original : $"pixelfx's {effect.Meta.Id}.js: clone https://github.com/patch-gg/pixelfx into References/ beside this repo to open it.")
            .OnClick(() => ComponentSource.Open(new ComponentSource.Location(original, 1)));
        return new StackPanel().Horizontal(theme.Spacing).Children(portButton, originalButton);
    }

    /// <summary>An option's editor by the kind of its default: a colour ramp, a number, a flag or text.</summary>
    private static UiElement? Editor(ManaPixelFxComponent fx, string name, object fallback, InspectorContext context)
    {
        switch (fallback)
        {
            case string[] ramp:
                return RampEditor(fx, name, ramp.Length, context);
            case double number:
                return NumberEditor(fx, name, number, context);
            case bool:
            {
                var box = new CheckBox();
                box.IsChecked = fx.OptionValue(name) is true;
                box.CheckedChanged += (_, value) =>
                {
                    if (!box.IsApplyingBinding)
                    {
                        Change(fx, name, value, context);
                    }
                };
                box.BindPolled(b => b.IsChecked, () => fx.OptionValue(name) is true);
                return box;
            }
            case string colour when colour.StartsWith('#'):
            {
                var field = InspectorControls.Colour(
                    () => ToColour(fx.OptionValue(name) as string ?? colour),
                    value => Change(fx, name, ToHex(value), context));
                field.ShowAlpha = false;
                return field;
            }
            case string:
            {
                var text = new TextBox { Text = fx.OptionValue(name) as string ?? string.Empty };
                text.TextChanged += (_, value) =>
                {
                    if (!text.IsApplyingBinding)
                    {
                        Change(fx, name, value, context);
                    }
                };
                return text;
            }
            default:
                return null;
        }
    }

    // A palette: a swatch per step, dark to bright, and pixelfx's ramps to start from.
    private static UiElement RampEditor(ManaPixelFxComponent fx, string name, int steps, InspectorContext context)
    {
        string[] Current() => fx.OptionValue(name) as string[] ?? [];
        var swatches = new UniformRow(steps);
        for (var i = 0; i < steps; i++)
        {
            var step = i;
            var field = InspectorControls.Colour(
                () => ToColour(Current().ElementAtOrDefault(step) ?? "#000000"),
                colour =>
                {
                    var ramp = (string[])Current().Clone();
                    if (step < ramp.Length)
                    {
                        ramp[step] = ToHex(colour);
                        Change(fx, name, ramp, context);
                    }
                });
            field.ShowAlpha = false;
            field.ShowHexLabel = false;
            swatches.Add(field);
        }

        var presets = PixelColor.Pal.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var preset = new ComboBox().Options(["Ramp…", .. presets]);
        preset.SelectedIndex = 0;
        preset.SelectionChanged += (_, index) =>
        {
            if (preset.IsApplyingBinding || index <= 0)
            {
                return;
            }
            Change(fx, name, PixelColor.Fit(PixelColor.Pal[presets[index - 1]], steps).Select(PixelColor.ToHex).ToArray(), context);
            preset.SelectedIndex = 0;
        };
        return new StackPanel().Vertical(context.Theme.Spacing).Children(swatches.Panel, preset);
    }

    private static UiElement NumberEditor(ManaPixelFxComponent fx, string name, double fallback, InspectorContext context)
    {
        // Seeds are whole numbers picking a take; anything else is a size, scaled about its default.
        var seed = name == "seed";
        var maximum = 100.0f;
        if (!seed)
        {
            maximum = MathF.Max(1.0f, (float)Math.Abs(fallback) * 3.0f);
        }
        var slider = InspectorControls.Scalar(
            () => Convert.ToSingle(fx.OptionValue(name) ?? fallback),
            value =>
            {
                double next = value;
                if (seed)
                {
                    next = Math.Round(value);
                }
                Change(fx, name, next, context);
            },
            0.0f,
            maximum);
        if (seed)
        {
            slider.Step = 1.0f;
            slider.DecimalPlaces = 0;
        }
        return slider;
    }

    /// <summary>Changes an option (null resets it), as one undo step.</summary>
    private static void Change(ManaPixelFxComponent fx, string name, object? value, InspectorContext context)
    {
        object? before = null;
        if (fx.IsOptionChanged(name))
        {
            before = fx.OptionValue(name);
        }
        fx.SetOption(name, value);
        context.History?.Record($"Change {Humanise(name)}", () => fx.SetOption(name, before), () => fx.SetOption(name, value));
    }

    private static UiElement BakeRow(ManaPixelFxComponent fx, InspectorContext context)
    {
        var theme = context.Theme;
        var status = new TextBlock().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Wrap();
        var bake = new ManaUI.Button().Label("Bake to sprite sheet").WithTooltip(
            $"Renders one run with these options into Content/{BakedFolder}/ and swaps this script for a Sprite Sheet playing it. Undo puts the live effect back.");
        bake.OnClick(() =>
        {
            try
            {
                var directory = Path.Combine(ManaContentAssets.Root, BakedFolder);
                var name = ManaPixelFxBaker.FreeName(directory, $"{fx.Owner.Name} {fx.Definition?.Meta.Name}".Trim());
                var file = ManaPixelFxBaker.Bake(fx, directory, name);
                var sheet = ManaPixelFxBaker.SheetFor(fx, file);
                ManaPixelFxBaker.Swap(fx, sheet);
                context.History?.Record($"Bake {name}", () => ManaPixelFxBaker.Swap(sheet, fx), () => ManaPixelFxBaker.Swap(fx, sheet));
                if (context.Selection is { } selection && ReferenceEquals(selection.SelectedComponent, fx))
                {
                    selection.SelectedComponent = sheet;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                status.Text = $"Could not bake: {exception.Message}";
            }
        });
        bake.BindPolled(b => b.IsEnabled, () => fx.Definition != null && !OperatingSystem.IsBrowser());
        return new StackPanel().Vertical(theme.Spacing).Children(bake, status);
    }

    private static string? OptionHint(string name, object fallback)
    {
        if (fallback is string[])
        {
            return $"The {Humanise(name).ToLowerInvariant()} ramp, dark to bright. Pick a pixelfx ramp below to start from one.";
        }
        if (name == "seed")
        {
            return "Picks a different but equivalent take on the effect.";
        }
        return null;
    }

    private static string Humanise(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static Vector4 ToColour(string hex)
    {
        var c = PixelColor.Hex(hex);
        return new Vector4(PixelColor.Red(c) / 255.0f, PixelColor.Green(c) / 255.0f, PixelColor.Blue(c) / 255.0f, 1.0f);
    }

    private static string ToHex(Vector4 colour)
    {
        static int Byte(float v) => (int)MathF.Round(Math.Clamp(v, 0.0f, 1.0f) * 255.0f);
        return $"#{Byte(colour.X):x2}{Byte(colour.Y):x2}{Byte(colour.Z):x2}";
    }

    // Swatches sharing a row equally, however narrow the panel.
    private sealed class UniformRow
    {
        private readonly int _count;
        private int _added;

        public UniformRow(int count)
        {
            _count = count;
            Panel = new Grid().Columns(string.Join(", ", Enumerable.Repeat("*", Math.Max(1, count))), 2.0f).Rows("Auto");
        }

        public Grid Panel { get; }

        public void Add(UiElement element)
        {
            if (_added < _count)
            {
                Panel.Children.Add(element.GridColumn(_added));
                _added++;
            }
        }
    }
}
