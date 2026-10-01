using System.Runtime.InteropServices;
using ManaCore.Diagnostics;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// View > About: what this is, which build, and what it runs on. The graphics diagnostics table is a
    /// panel of its own (dock id "about"), a click away.
    /// </summary>
    public static class AboutBoxManaView
    {
        public static void Show(UiRoot root, Action? openDiagnostics)
        {
            var theme = root.Theme;
            var popup = new UiPopup { StaysOpen = true };
            popup.BindPolled(p => p.Width, () => root.Size.X);
            popup.BindPolled(p => p.Height, () => root.Size.Y);

            var card = new Border()
                .Fill(theme.Surface)
                .Outline(theme.Border, 1.0f)
                .PaddingOf(20.0f, 20.0f)
                .Content(Build(theme, () => root.Popups.Close(popup), openDiagnostics == null ? null : () =>
                {
                    root.Popups.Close(popup);
                    openDiagnostics();
                }));
            card.Width = 500.0f;

            var overlay = new Grid().Rows("*").Columns("*").Children(card.CenterH().CenterV());
            overlay.Background = new Vector4(0, 0, 0, 0.55f);
            popup.Child = overlay;

            // Modal, as Search Area is: nothing behind it takes input while it is up.
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

        public static UiElement Build(UiTheme theme, Action close, Action? openDiagnostics)
        {
            var details = new Grid().Columns("Auto, *", theme.Spacing * 2.0f).Rows("Auto, Auto, Auto, Auto, Auto", 4.0f);
            var row = 0;
            void Detail(string label, string value)
            {
                details.Children.Add(new TextBlock().Text(label).Foreground(theme.TextMuted).GridRow(row).GridColumn(0));
                details.Children.Add(new TextBlock().Text(value).Wrap().GridRow(row).GridColumn(1));
                row++;
            }

            Detail("Version", ManaVersion.Number);
            Detail("Commit", ManaVersion.Commit.Length == 0 ? "not from a git checkout" : ManaVersion.Commit);
            Detail("Built", ManaVersion.BuildDate.Length == 0 ? "unknown" : ManaVersion.BuildDate);
            Detail("Runtime", $".NET {Environment.Version}");
            Detail("System", RuntimeInformation.OSDescription);

            var buttons = new StackPanel().Horizontal(theme.Spacing).Children(
                new Button().Label("Close").Variant(ButtonVariant.Primary).OnClick(close));
            if (openDiagnostics != null)
            {
                buttons.Children.Insert(0, new Button().Label("Graphics Diagnostics").OnClick(openDiagnostics));
            }

            return new StackPanel().Vertical(theme.Spacing * 1.5f).Children(
                new TextBlock().Text("Mana Renderer").FontFamily(theme.FontFamilyMedium).FontSize(theme.FontSizeHeading),
                new TextBlock()
                    .Text("A C# renderer and voxel level editor: WebGPU first, OpenGL as a fallback.")
                    .Foreground(theme.TextMuted)
                    .Wrap(),
                details,
                Credits(theme).Scrollable().Height(240.0f),
                buttons.CenterH());
        }

        // Only what is known for certain; a source not on record is left off rather than guessed.
        private static readonly (string Heading, string[] Lines)[] CreditList =
        {
            ("Made by", new[] { "Mana Mark Tea (Mark Tulewicz) -- MIT License" }),
            ("Fonts", new[]
            {
                "Roboto -- Google, Apache License 2.0",
                "Antonio -- Vernon Adams, SIL Open Font License 1.1",
                "Barlow Condensed -- Jeremy Tribby, SIL Open Font License 1.1"
            }),
            ("Models, sprites and sounds", new[]
            {
                "Kenney (kenney.nl), CC0: Animated Characters (Protagonists, Survivors), Blocky Characters, City Kit " +
                "(Roads), Cube Pets, Cursor Pack, Factory Kit, Furniture Kit, Mini Arena, Mini Characters, Mini Dungeon, " +
                "Modular Dungeon Kit, New Platformer Pack, Pirate Kit, Space Station Kit, Toy Car Kit, Train Kit, " +
                "Watercraft Pack",
                "glTF sample models (Duck, Flight Helmet, Lantern, Sci-Fi Helmet, Water Bottle, Animated Cube) -- " +
                "the Khronos Group glTF Sample Assets, each under its own licence"
            }),
            ("Sky", new[]
            {
                "Atmosphere after Sebastian Lague's Solar System -- MIT License",
                "Sky photos (Kloofendal 48d Partly Cloudy, Wasteland Clouds) -- Poly Haven, CC0"
            }),
            ("Libraries", new[]
            {
                "Silk.NET (MIT) with wgpu (MIT / Apache 2.0), GLFW and OpenAL Soft",
                "BepuPhysics v2 -- Apache License 2.0",
                "ImageSharp -- Six Labors Split License",
                "SharpGLTF -- MIT",
                "StbTrueTypeSharp -- after stb_truetype (public domain / MIT)",
                "CommunityToolkit.Mvvm (MIT), Serilog (Apache 2.0), Stateless (Apache 2.0), GeometRi (MIT)"
            })
        };

        private static UiElement Credits(UiTheme theme)
        {
            var list = new StackPanel().Vertical(4.0f);
            list.Children.Add(new TextBlock().Text("Credits").FontFamily(theme.FontFamilyMedium));

            foreach (var (heading, lines) in CreditList)
            {
                list.Children.Add(new TextBlock().Text(heading).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall)
                    .Margin(0.0f, 6.0f, 0.0f, 0.0f));
                foreach (var line in lines)
                {
                    list.Children.Add(new TextBlock().Text(line).FontSize(theme.FontSizeSmall).Wrap());
                }

                // Emoji come from whichever font this build draws them with.
                if (heading == "Fonts")
                {
                    list.Children.Add(new TextBlock().FontSize(theme.FontSizeSmall).Wrap().Text(
                        ManaFontManager.PlatformColorEmoji != null
                            ? "Emoji -- your browser's own emoji font"
                            : "Noto Color Emoji -- Google, SIL Open Font License 1.1"));
                }
            }

            return list;
        }
    }
}
