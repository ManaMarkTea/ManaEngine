using Mana;
using Mana.Code.Content;
using Mana.Code.UI.ManaUi;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The Sprite Asset window: setting up a sprite sheet (its frames, size, pivot and animations, with the chosen
    /// animation playing) or a 9-slice (its nine pieces in a 3 x 3 grid, with a block built from them), once, for
    /// every sprite drawn from it. Opened from the Project panel, or Edit beside an asset in the Inspector.
    /// </summary>
    public static class SpriteAssetManaView
    {
        public const string PanelId = "spriteasset";

        public static UiElement Build(UiTheme theme)
        {
            return new DynamicContent().Showing<object>(() => SpriteAssetEditing.Token, _ =>
                SpriteAssetEditing.Sheet is { } sheet ? SheetEditor(theme, sheet)
                : SpriteAssetEditing.Slice is { } slice ? SliceEditor(theme, slice)
                : new TextBlock().Text("Open a sprite sheet or a 9-slice from the Project panel.").Wrap().Foreground(theme.TextMuted)
                    .Configure(t => t.Margin = new Thickness(8.0f)));
        }

        // ---- Common ------------------------------------------------------------------------------------------------

        private static UiElement Header(UiTheme theme, string kind, Func<string> name, Action<string> rename)
        {
            var box = new TextBox().Value(name()).Hint("Name");
            box.OnCommitted(text =>
            {
                rename(text);
                SpriteAssetEditing.Changed();
            });
            var status = new TextBlock().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Ellipsis();
            status.BindPolled(t => t.Text, () => SpriteAssetEditing.Status);
            return new StackPanel().Vertical(4.0f).Children(
                new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                    box.GridColumn(0),
                    new Button().Label("Revert").Variant(ButtonVariant.Subtle).WithTooltip("Put the asset back as it was when opened")
                        .OnClick(SpriteAssetEditing.Revert).GridColumn(1)),
                new TextBlock().Text($"{kind} · {Relative(SpriteAssetEditing.Path)}").Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Ellipsis(),
                status);
        }

        private static string Relative(string? path) => path == null ? "" : Path.GetRelativePath(ManaContentAssets.Root, path);

        private static UiElement Row(UiTheme theme, string label, UiElement editor, string? tooltip = null)
        {
            var text = new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV();
            if (tooltip != null)
            {
                text.Tooltip = tooltip;
            }
            return new Grid().Columns("100, *", theme.Spacing).Rows("Auto").Children(text.GridColumn(0), editor.GridColumn(1));
        }

        private static UiElement Heading(UiTheme theme, string text) =>
            new TextBlock().Text(text).Foreground(theme.Accent).FontSize(theme.FontSizeSmall).Configure(t => t.Margin = new Thickness(0, 6, 0, 0));

        private static NumericBox Number(Func<float> read, Action<float> write, bool integer = false)
        {
            var box = new NumericBox().Value(read()).Integer(integer);
            box.ValueCommitted += (_, value) =>
            {
                if (!box.IsApplyingBinding)
                {
                    write(value);
                    SpriteAssetEditing.Changed();
                }
            };
            return box;
        }

        // Shown fitted into a box; the picture comes through the texture previews, polled so it follows each change.
        private static UiElement Preview(UiTheme theme, float height, Func<(string? File, Vector4 Cell)> source)
        {
            var image = new UiImage { Fit = UiImageFit.Contain }.Height(height);
            image.BindPolled(i => i.SourceUv, () => Resolve(image, source()).Uv);
            image.BindPolled(i => i.SourceSize, () => Resolve(image, source()).Size);
            return new Border().Fill(theme.SurfaceAlt).Rounded(theme.CornerRadiusCompact).PaddingOf(6.0f).Content(image);
        }

        private static (Vector4 Uv, Vector2 Size) Resolve(UiImage image, (string? File, Vector4 Cell) source)
        {
            if (source.File == null || image.Root?.Images is not { } atlas || !ManaUiTexturePreviews.TryGetForFile(atlas, source.File, out var preview))
            {
                return (Vector4.Zero, Vector2.One);
            }
            // The cell is a part of the picture, 0..1 across and down: one frame of a grid sheet.
            var uv = preview.Uv;
            var cell = source.Cell;
            var width = uv.Z - uv.X;
            var height = uv.W - uv.Y;
            return (new Vector4(uv.X + width * cell.X, uv.Y + height * cell.Y, uv.X + width * cell.Z, uv.Y + height * cell.W),
                new Vector2(preview.Size.X * (cell.Z - cell.X), preview.Size.Y * (cell.W - cell.Y)));
        }

        private static readonly Vector4 Whole = new(0, 0, 1, 1);

        // ---- Sprite sheet ------------------------------------------------------------------------------------------

        private static UiElement SheetEditor(UiTheme theme, ManaSpriteSheet sheet)
        {
            var rows = new StackPanel().Vertical(theme.Spacing).Padding(8.0f);
            rows.Children.Add(Header(theme, "Sprite Sheet", () => sheet.Name, name => sheet.Name = name));

            // The animation playing in the preview: the one picked in the list below, the first until then.
            var previewing = sheet.Animations.FirstOrDefault()?.Name ?? string.Empty;
            var started = Environment.TickCount64;
            (string? File, Vector4 Cell) PreviewFrame()
            {
                var animation = sheet.Animation(previewing) ?? sheet.Animations.FirstOrDefault();
                var frame = animation?.FrameAt((Environment.TickCount64 - started) / 1000.0) ?? 0;
                if (sheet.Frames.Count > 0)
                {
                    return (ManaSpritePictures.TryGetResolved(sheet.Frames[Math.Clamp(frame, 0, sheet.Frames.Count - 1)]), Whole);
                }
                var columns = Math.Max(1, sheet.Columns);
                var rowCount = Math.Max(1, sheet.Rows);
                frame = Math.Clamp(frame, 0, columns * rowCount - 1);
                float column = frame % columns, row = frame / columns;
                return (ManaSpritePictures.TryGetResolved(sheet.Texture), new Vector4(column / columns, row / rowCount, (column + 1) / columns, (row + 1) / rowCount));
            }
            rows.Children.Add(Preview(theme, 140.0f, PreviewFrame));

            rows.Children.Add(Heading(theme, "Pictures"));
            var gridMode = sheet.Frames.Count == 0 && sheet.Texture.Length > 0;
            var mode = new ComboBox().Options(["One picture per frame", "Frames in a grid on one picture"]);
            mode.SelectedIndex = gridMode ? 1 : 0;
            rows.Children.Add(mode);

            var frames = new StackPanel().Vertical(theme.Spacing).Children(
                Row(theme, "Frames", PicturePicker.List(theme, "Frame", () => sheet.Frames.ToArray(), value =>
                {
                    sheet.Frames = [.. value];
                    SpriteAssetEditing.Changed();
                })));
            var grid = new StackPanel().Vertical(theme.Spacing).Children(
                Row(theme, "Picture", PicturePicker.Slot(theme, "Picture", () => sheet.Texture, value =>
                {
                    sheet.Texture = value;
                    SpriteAssetEditing.Changed();
                })),
                Row(theme, "Columns", Number(() => sheet.Columns, value => sheet.Columns = Math.Max(1, (int)value), integer: true)),
                Row(theme, "Rows", Number(() => sheet.Rows, value => sheet.Rows = Math.Max(1, (int)value), integer: true)),
                Row(theme, "Normal map", PicturePicker.Slot(theme, "Normal map", () => sheet.NormalMap, value =>
                {
                    sheet.NormalMap = value;
                    SpriteAssetEditing.Changed();
                }), "Optional: lights shade the frames as if they had depth"));
            frames.Visibility = gridMode ? Visibility.Collapsed : Visibility.Visible;
            grid.Visibility = gridMode ? Visibility.Visible : Visibility.Collapsed;
            mode.SelectionChanged += (_, index) =>
            {
                frames.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
                grid.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            };
            rows.Children.Add(frames);
            rows.Children.Add(grid);

            rows.Children.Add(Heading(theme, "Size"));
            rows.Children.Add(Row(theme, "Pixels per unit", Number(() => sheet.PixelsPerUnit, value => sheet.PixelsPerUnit = MathF.Max(value, 0.01f)),
                "How many of the pictures' pixels make a metre"));
            var pivot = new VectorBox().Components(2).Step(0.05f).Places(2);
            pivot.Value2 = sheet.PivotPoint;
            pivot.ValueCommitted += (_, value) =>
            {
                sheet.Pivot = [value.X, value.Y];
                SpriteAssetEditing.Changed();
            };
            rows.Children.Add(Row(theme, "Pivot", pivot, "Where the node stands on a frame, 0..1 across and up: 0.5, 0 is the bottom middle"));

            rows.Children.Add(Heading(theme, "Animations"));
            var animations = new StackPanel().Vertical(theme.Spacing);
            void RebuildAnimations()
            {
                animations.Children.Clear();
                foreach (var animation in sheet.Animations.ToList())
                {
                    var current = animation;
                    var name = new TextBox().Value(current.Name).Hint("name");
                    name.OnCommitted(text =>
                    {
                        current.Name = text.Trim();
                        SpriteAssetEditing.Changed();
                    });
                    var framesBox = new TextBox().Value(string.Join(", ", current.Frames)).Hint("0, 1, 2");
                    framesBox.Tooltip = "The frames it plays, in order, counted from 0";
                    framesBox.OnCommitted(text =>
                    {
                        current.Frames = text.Split(',', ' ', ';').Select(part => int.TryParse(part, out var index) ? index : -1).Where(index => index >= 0).ToList();
                        SpriteAssetEditing.Changed();
                    });
                    var fps = Number(() => current.FramesPerSecond, value => current.FramesPerSecond = MathF.Max(value, 0.0f));
                    fps.Tooltip = "Frames a second";
                    var loop = new CheckBox().Label("Loop").Checked(current.Loop).OnChanged(value =>
                    {
                        current.Loop = value;
                        SpriteAssetEditing.Changed();
                    });
                    var play = new Button().Label("▶").Variant(ButtonVariant.Subtle).WithTooltip("Play it in the preview").OnClick(() =>
                    {
                        previewing = current.Name;
                        started = Environment.TickCount64;
                    });
                    var remove = new Button().Label("x").Variant(ButtonVariant.Subtle).WithTooltip("Remove this animation").OnClick(() =>
                    {
                        sheet.Animations.Remove(current);
                        SpriteAssetEditing.Changed();
                        RebuildAnimations();
                    });
                    animations.Children.Add(new Grid().Columns("Auto, 70, *, 50, Auto, Auto", 4.0f).Rows("Auto").Children(
                        play.GridColumn(0), name.GridColumn(1), framesBox.GridColumn(2), fps.GridColumn(3), loop.GridColumn(4), remove.GridColumn(5)));
                }
                animations.Children.Add(new Button().Label("+ Add animation").Variant(ButtonVariant.Subtle).OnClick(() =>
                {
                    sheet.Animations.Add(new ManaSpriteAnimation { Name = $"animation {sheet.Animations.Count + 1}", Frames = [0] });
                    SpriteAssetEditing.Changed();
                    RebuildAnimations();
                }));
            }
            RebuildAnimations();
            rows.Children.Add(animations);
            return rows.Scrollable();
        }

        // ---- 9-slice -----------------------------------------------------------------------------------------------

        private static UiElement SliceEditor(UiTheme theme, ManaSpriteSlice slice)
        {
            var rows = new StackPanel().Vertical(theme.Spacing).Padding(8.0f);
            rows.Children.Add(Header(theme, "9-Slice", () => slice.Name, name => slice.Name = name));
            rows.Children.Add(Preview(theme, 110.0f, () => (SpriteAssetEditing.PreviewFile, Whole)));

            rows.Children.Add(Heading(theme, "Pieces"));
            rows.Children.Add(new TextBlock().Text("Corners, edges and the middle. Leave any empty: it uses the middle of its row, then the centre.")
                .Wrap().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall));

            UiElement Piece(string label, Func<string> read, Action<string> write) => PicturePicker.Tile(theme, label, 64.0f, read, value =>
            {
                write(value);
                SpriteAssetEditing.Changed();
            });
            var pieces = new Grid().Columns("64, 64, 64", 4.0f).Rows("64, 64, 64", 4.0f).Children(
                Piece("Top left", () => slice.TopLeft, v => slice.TopLeft = v).GridRow(0).GridColumn(0),
                Piece("Top", () => slice.Top, v => slice.Top = v).GridRow(0).GridColumn(1),
                Piece("Top right", () => slice.TopRight, v => slice.TopRight = v).GridRow(0).GridColumn(2),
                Piece("Left", () => slice.Left, v => slice.Left = v).GridRow(1).GridColumn(0),
                Piece("Centre", () => slice.Center, v => slice.Center = v).GridRow(1).GridColumn(1),
                Piece("Right", () => slice.Right, v => slice.Right = v).GridRow(1).GridColumn(2),
                Piece("Bottom left", () => slice.BottomLeft, v => slice.BottomLeft = v).GridRow(2).GridColumn(0),
                Piece("Bottom", () => slice.Bottom, v => slice.Bottom = v).GridRow(2).GridColumn(1),
                Piece("Bottom right", () => slice.BottomRight, v => slice.BottomRight = v).GridRow(2).GridColumn(2));
            rows.Children.Add(pieces.CenterH());

            rows.Children.Add(Heading(theme, "Size"));
            rows.Children.Add(Row(theme, "Pixels per unit", Number(() => slice.PixelsPerUnit, value => slice.PixelsPerUnit = MathF.Max(value, 0.01f)),
                "How many of the pieces' pixels make a metre: 64 makes a 64-pixel tile one metre"));
            return rows.Scrollable();
        }
    }
}
