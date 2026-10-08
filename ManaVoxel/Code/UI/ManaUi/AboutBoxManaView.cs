using System.Runtime.InteropServices;
using ManaCore.Diagnostics;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// View > About: what this is, which build, what it runs on, and who made what. The graphics diagnostics
    /// table is a panel of its own (dock id "about"), a click away.
    /// </summary>
    public static class AboutBoxManaView
    {
        private const float CardWidth = 540.0f;

        public static void Show(UiRoot root, Action? openDiagnostics)
        {
            var theme = root.Theme;
            var popup = new UiPopup { StaysOpen = true };
            popup.BindPolled(p => p.Width, () => root.Size.X);
            popup.BindPolled(p => p.Height, () => root.Size.Y);

            var card = new Border()
                .Fill(theme.Surface)
                .Outline(theme.Border, 1.0f)
                .Rounded(theme.CornerRadiusLarge)
                .Shadow(24.0f)
                .Content(Build(theme, () => root.Popups.Close(popup), openDiagnostics == null ? null : () =>
                {
                    root.Popups.Close(popup);
                    openDiagnostics();
                }));
            card.Width = CardWidth;
            card.BindPolled(c => c.Height, () => MathF.Min(640.0f, root.Size.Y - 80.0f));

            var overlay = new Grid().Rows("*").Columns("*").Children(card.CenterH().CenterV());
            overlay.Background = new Vector4(0, 0, 0, 0.55f);
            popup.Child = overlay;

            // Modal, as Search Location is: nothing behind it takes input while it is up.
            var contentEnabled = root.ContentLayer.IsEnabled;
            var floatingEnabled = root.FloatingLayer.IsEnabled;
            root.ContentLayer.IsEnabled = false;
            root.FloatingLayer.IsEnabled = false;
            popup.Closed += (_, _) =>
            {
                root.ContentLayer.IsEnabled = contentEnabled;
                root.FloatingLayer.IsEnabled = floatingEnabled;
            };
            root.Popups.ShowAt(popup, Vector2.Zero);
        }

        /// <summary>Header and build details on top, the credits scrolling beneath, the buttons pinned to the bottom.</summary>
        public static UiElement Build(UiTheme theme, Action close, Action? openDiagnostics)
        {
            var buttons = new StackPanel().Horizontal(theme.Spacing).Right();
            if (openDiagnostics != null)
            {
                buttons.Children.Add(new Button().Label("Graphics Diagnostics").OnClick(openDiagnostics));
            }

            buttons.Children.Add(new Button().Label("Close").Variant(ButtonVariant.Primary).OnClick(close));

            var top = new StackPanel().Vertical(theme.Spacing * 2.0f).Padding(24.0f).Children(Header(theme), Details(theme));
            var credits = new StackPanel().Vertical(theme.Spacing * 2.0f).Padding(24.0f).Children(Credits(theme));

            var footer = new Border()
                .Fill(theme.SurfaceAlt)
                .PaddingOf(16.0f, 12.0f)
                .Content(buttons);

            var rule = new Border().Fill(theme.Border);
            rule.Height = 1.0f;

            return new Grid().Rows("Auto, Auto, *, Auto").Columns("*").Children(
                top.GridRow(0),
                rule.GridRow(1),
                credits.Scrollable().GridRow(2),
                footer.GridRow(3));
        }

        private static UiElement Header(UiTheme theme)
        {
            var badge = new Border()
                .Fill(theme.Accent.WithAlpha(0.18f))
                .Rounded(10.0f)
                .PaddingOf(8.0f, 2.0f)
                .Content(new TextBlock().Text($"v{ManaVersion.Number}").FontSize(theme.FontSizeSmall).Foreground(theme.Accent))
                .CenterV();

            return new StackPanel().Vertical(4.0f).Children(
                new StackPanel().Horizontal(10.0f).Children(
                    new TextBlock().Text("Mana Renderer").FontFamily(theme.FontFamilyMedium).FontSize(theme.FontSizeHeading + 6.0f),
                    badge),
                new TextBlock()
                    .Text("A C# renderer and voxel level editor -- WebGPU first, OpenGL as a fallback.")
                    .Foreground(theme.TextMuted)
                    .Wrap());
        }

        private static UiElement Details(UiTheme theme)
        {
            (string Label, string Value)[] rows =
            {
                ("Version", ManaVersion.Number),
                ("Commit", ManaVersion.Commit.Length == 0 ? "not from a git checkout" : ManaVersion.Commit),
                ("Built", ManaVersion.BuildDate.Length == 0 ? "unknown" : ManaVersion.BuildDate),
                ("Runtime", $".NET {Environment.Version}"),
                ("System", RuntimeInformation.OSDescription)
            };

            return Card(theme, Table(theme, rows.Select(row => (row.Label, row.Value)).ToArray(), 80.0f, bold: false));
        }

        // Only what is known for certain; a source not on record is left off rather than guessed.
        private static (string Heading, (string Name, string Detail)[] Entries)[] CreditList() => new[]
        {
            ("Made by", new[] { ("Mana Mark Tea", "Mark Tulewicz -- MIT License") }),
            ("Fonts", new[]
            {
                ("Roboto", "Google -- Apache License 2.0"),
                ("Antonio", "Vernon Adams -- SIL OFL 1.1"),
                ("Barlow Condensed", "Jeremy Tribby -- SIL OFL 1.1"),
                // Emoji come from whichever font this build draws them with.
                ManaFontManager.PlatformColorEmoji != null
                    ? ("Emoji", "your browser's own emoji font")
                    : ("Noto Color Emoji", "Google -- SIL OFL 1.1")
            }),
            ("Art", new[]
            {
                ("Kenney", "kenney.nl -- CC0. Animated Characters (Protagonists, Survivors), Blocky Characters, City Kit " +
                    "(Roads), Cube Pets, Cursor Pack, Factory Kit, Furniture Kit, Mini Arena, Mini Characters, Mini " +
                    "Dungeon, Modular Dungeon Kit, New Platformer Pack, Pirate Kit, Space Station Kit, Toy Car Kit, " +
                    "Train Kit, Watercraft Pack"),
                ("glTF samples", "Flight Helmet, Lantern and Water Bottle -- Khronos Group glTF Sample Assets; Microsoft CC0 notices " +
                    "are kept beside each model"),
                ("Sky photos", "Kloofendal 48d Partly Cloudy, Wasteland Clouds -- Poly Haven, CC0"),
                ("Moon", "CGI Moon Kit, from Lunar Reconnaissance Orbiter data -- NASA's Scientific Visualization Studio, public domain"),
                ("Atmosphere", "after Sebastian Lague's Solar System -- MIT License"),
                ("Liquid shader", "Mirza Beig -- reuse permission and attribution in LiquidFill.frag")
            }),
            ("Libraries", new[]
            {
                ("Silk.NET", "MIT -- with wgpu (MIT / Apache 2.0), GLFW and OpenAL Soft"),
                ("BepuPhysics v2", "Apache License 2.0"),
                ("StbImageSharp", "after stb_image -- public domain / MIT"),
                ("SharpGLTF", "MIT"),
                ("StbTrueTypeSharp", "after stb_truetype -- public domain / MIT"),
                ("Also", "CommunityToolkit.Mvvm (MIT), Serilog (Apache 2.0), Stateless (Apache 2.0), GeometRi (MIT)")
            })
        };

        private static UiElement Credits(UiTheme theme)
        {
            var sections = new StackPanel().Vertical(theme.Spacing * 2.0f);
            sections.Children.Add(new TextBlock().Text("Credits").FontFamily(theme.FontFamilyMedium).FontSize(theme.FontSizeHeading));

            foreach (var (heading, entries) in CreditList())
            {
                sections.Children.Add(new StackPanel().Vertical(6.0f).Children(
                    new TextBlock().Text(heading.ToUpperInvariant()).FontSize(theme.FontSizeSmall).Foreground(theme.Accent),
                    Card(theme, Table(theme, entries, 130.0f, bold: true))));
            }

            return sections;
        }

        /// <summary>Name on the left in a fixed column, the detail wrapping beside it.</summary>
        private static Grid Table(UiTheme theme, (string Name, string Detail)[] rows, float nameWidth, bool bold)
        {
            var grid = new Grid().Columns($"{nameWidth}, *", theme.Spacing).Rows(string.Join(", ", rows.Select(_ => "Auto")), 6.0f);
            for (var i = 0; i < rows.Length; i++)
            {
                var name = new TextBlock().Text(rows[i].Name).GridRow(i).GridColumn(0);
                if (bold)
                {
                    name.FontFamily(theme.FontFamilyMedium);
                }
                else
                {
                    name.Foreground(theme.TextMuted);
                }

                grid.Children.Add(name);
                grid.Children.Add(new TextBlock().Text(rows[i].Detail).Foreground(bold ? theme.TextMuted : theme.Text).Wrap()
                    .GridRow(i).GridColumn(1));
            }

            return grid;
        }

        private static UiElement Card(UiTheme theme, UiElement content)
        {
            return new Border()
                .Fill(theme.SurfaceAlt)
                .Rounded(6.0f)
                .PaddingOf(12.0f, 10.0f)
                .Content(content);
        }
    }
}
