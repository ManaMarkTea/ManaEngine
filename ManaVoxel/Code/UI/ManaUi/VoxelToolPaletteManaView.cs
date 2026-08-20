using ManaUI;
using ManaVoxel.Code.Tools;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The voxel tool palette, rebuilt in ManaUI and driven by the real
    /// <see cref="VoxelEditorViewModel"/> -- the same instance the ImGui palette uses, so the two stay in
    /// step and either can drive the editor.
    /// <para>
    /// The view model exposes its state as public mutable fields with no change notification, which is
    /// typical of code that grew up against an immediate-mode UI. Property-path bindings cannot touch a
    /// field, so this reads through polled bindings and writes back through the controls' own events. That
    /// pairing is the intended way to drive an existing model without rewriting it, and it is why polled
    /// bindings exist at all -- the alternative would have been converting the whole view model before a
    /// single panel could be ported.
    /// </para>
    /// </summary>
    public static class VoxelToolPaletteManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel vm)
        {
            // Horizontal rather than vertical: this lives in the toolbar strip under the menu now, not a
            // side column, so a window too narrow for every group scrolls sideways rather than wrapping.
            var scrollable = new StackPanel().Horizontal(theme.Spacing * 2.0f).Children(
                    BuildToolGroup(theme, vm),
                    Divider(theme),
                    BuildColorGroup(theme, vm),
                    BuildShapeColorGroup(theme, vm),
                    BuildStrokeGroup(theme, vm),
                    BuildTextGroup(theme, vm))
                .Scrollable()
                .Scrolls(vertical: ScrollBarVisibility.Disabled, horizontal: ScrollBarVisibility.Auto);

            // Work plane gets a section of its own, pinned to the right rather than folded in with the
            // tools -- it only actually constrains Circle and Text (the only two that read it), and sitting
            // among the other tool controls made it look like it applied to all of them.
            return new Grid().Columns("*, Auto, Auto").Rows("Auto").Padding(6.0f, 3.0f).Children(
                scrollable.GridColumn(0),
                Divider(theme).GridColumn(1),
                BuildWorkPlaneGroup(theme, vm).GridColumn(2));
        }

        // ----- tools ----------------------------------------------------------------------------------

        // Matches the theme's control height, so a tool button is the same size as every other control in
        // the strip and the strip is exactly one control tall.
        private const float ToolButtonSize = 24.0f;

        private static UiElement BuildToolGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            // One row of icon toggles, the way the ImGui palette drew them, rather than a dropdown --
            // the tool being used is something worth seeing at a glance, not something worth a click to
            // reveal. Select is one mode among the seven now, not an independent flag layered over
            // whichever tool happens to be current: picking a tool leaves select mode, and picking
            // select leaves whatever tool was active. IsSelectToolActive and SelectedToolIndex stay the
            // two fields VoxelEditorGameMode already reads -- only how this row writes them changes.
            return new StackPanel().Horizontal(4.0f).Children(
                ToolButton(theme, DrawSelectIcon, "Select",
                    () => vm.IsSelectToolActive,
                    () => vm.IsSelectToolActive = true),
                ToolButton(theme, DrawStrokeIcon, "Stroke", VoxelToolKind.Stroke, vm),
                ToolButton(theme, DrawLineIcon, "Line", VoxelToolKind.Line, vm),
                ToolButton(theme, DrawCircleIcon, "Circle", VoxelToolKind.Circle, vm),
                ToolButton(theme, DrawSphereIcon, "Sphere", VoxelToolKind.Sphere, vm),
                ToolButton(theme, DrawSplineIcon, "Spline", VoxelToolKind.Spline, vm),
                ToolButton(theme, DrawTextIcon, "Text", VoxelToolKind.Text, vm),
                Divider(theme),
                ToolButton(theme, DrawPaintIcon, "Paint", VoxelToolKind.Paint, vm),
                ToolButton(theme, DrawFloodFillIcon, "Flood Fill", VoxelToolKind.FloodFill, vm),
                ToolButton(theme, DrawEraseIcon, "Erase", VoxelToolKind.Erase, vm));
        }

        /// <summary>Sets the current tool and leaves select mode -- radio-style, one of the seven at a time.</summary>
        private static Button ToolButton(
            UiTheme theme,
            Action<ManaUiDrawList, UiRect, Vector4> drawIcon,
            string tooltip,
            VoxelToolKind kind,
            VoxelEditorViewModel vm)
        {
            var index = VoxelToolCatalog.GetToolIndex(kind);
            return ToolButton(theme, drawIcon, tooltip,
                () => !vm.IsSelectToolActive && vm.SelectedToolIndex == index,
                () =>
                {
                    vm.SelectedToolIndex = index;
                    vm.IsSelectToolActive = false;
                });
        }

        /// <summary>
        /// An icon button whose fill and icon colour follow whatever <paramref name="isSelected"/> reports,
        /// polled the same way every other control in this toolbar reads the view model.
        /// </summary>
        private static Button ToolButton(
            UiTheme theme,
            Action<ManaUiDrawList, UiRect, Vector4> drawIcon,
            string tooltip,
            Func<bool> isSelected,
            Action onClick)
        {
            var icon = new ToolIcon(drawIcon)
                .BindPolled(i => i.Color, () => isSelected() ? theme.TextOnAccent : theme.Text);

            return new Button
                {
                    Child = icon,
                    IgnoreThemePadding = true,
                    Width = ToolButtonSize,
                    Height = ToolButtonSize
                }
                .WithTooltip(tooltip)
                .OnClick(onClick)
                .BindPolled(b => b.Variant, () => isSelected() ? ButtonVariant.Primary : ButtonVariant.Subtle);
        }

        /// <summary>
        /// Draws into the box a delegate is given, so each tool's icon is a small function rather than a
        /// control of its own -- there is only ever one of these on screen per tool, and the geometry does
        /// not need a life of its own beyond drawing.
        /// </summary>
        private sealed class ToolIcon : UiElement
        {
            private readonly Action<ManaUiDrawList, UiRect, Vector4> _draw;
            private Vector4 _color;

            public ToolIcon(Action<ManaUiDrawList, UiRect, Vector4> draw)
            {
                _draw = draw;
            }

            public Vector4 Color
            {
                get => _color;
                set => SetVisualProperty(ref _color, value);
            }

            protected override Vector2 MeasureOverride(Vector2 availableSize) => new(18.0f, 18.0f);

            protected override void OnRender(ManaUiDrawList drawList)
            {
                _draw(drawList, Bounds, _color);
            }
        }

        // ----- tool icons -----------------------------------------------------------------------------
        // The shared draw list has no circle or polygon fill -- only rects (with corner radius, so a
        // square with a full radius is a circle), lines and a stroked border. Every icon below is built
        // from just those three calls, tracing the same silhouettes the hand-drawn ImGui icons used.

        private static Vector2 P(UiRect box, float tx, float ty) =>
            new(box.X + box.Width * tx, box.Y + box.Height * ty);

        private static void FillDot(ManaUiDrawList drawList, Vector2 center, float radius, Vector4 color)
        {
            drawList.FillRect(
                new UiRect(center.X - radius, center.Y - radius, radius * 2.0f, radius * 2.0f),
                color,
                new CornerRadius(radius));
        }

        private static void DrawSelectIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // A cursor arrow, stroked as a closed polyline since there is no filled-polygon primitive to
            // draw its silhouette solid.
            var p0 = P(box, 0.08f, 0.06f);
            var p1 = P(box, 0.08f, 0.78f);
            var p2 = P(box, 0.30f, 0.58f);
            var p3 = P(box, 0.44f, 0.92f);
            var p4 = P(box, 0.56f, 0.86f);
            var p5 = P(box, 0.40f, 0.52f);
            var p6 = P(box, 0.66f, 0.52f);

            const float thickness = 1.6f;
            drawList.DrawLine(p0, p1, color, thickness);
            drawList.DrawLine(p1, p2, color, thickness);
            drawList.DrawLine(p2, p3, color, thickness);
            drawList.DrawLine(p3, p4, color, thickness);
            drawList.DrawLine(p4, p5, color, thickness);
            drawList.DrawLine(p5, p6, color, thickness);
            drawList.DrawLine(p6, p0, color, thickness);
        }

        private static void DrawStrokeIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // A brush: the handle as a stroke, the tip as a dot.
            var handleStart = P(box, 0.18f, 0.88f);
            var handleEnd = P(box, 0.70f, 0.30f);
            drawList.DrawLine(handleStart, handleEnd, color, 3.0f);
            FillDot(drawList, handleEnd, box.Width * 0.14f, color);
        }

        private static void DrawLineIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            var from = P(box, 0.18f, 0.82f);
            var to = P(box, 0.82f, 0.18f);
            drawList.DrawLine(from, to, color, 2.2f);
            FillDot(drawList, from, box.Width * 0.09f, color);
            FillDot(drawList, to, box.Width * 0.09f, color);
        }

        private static void DrawRing(
            ManaUiDrawList drawList, Vector2 center, float radiusX, float radiusY, float thickness, Vector4 color)
        {
            var rect = new UiRect(center.X - radiusX, center.Y - radiusY, radiusX * 2.0f, radiusY * 2.0f);
            drawList.DrawBorder(rect, color, thickness, new CornerRadius(MathF.Min(radiusX, radiusY)));
        }

        private static void DrawCircleIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // Outlined rather than filled -- the tool draws a ring by default, and the icon should say so.
            var radius = box.Width * 0.36f;
            DrawRing(drawList, P(box, 0.5f, 0.5f), radius, radius, 1.8f, color);
        }

        private static void DrawSphereIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // The classic wireframe-globe pair: a great circle facing the viewer (the meridian) crossed by
            // the same circle seen edge-on (the equator, foreshortened to an ellipse). No true ellipse
            // primitive exists, so the equator is a wide, short ring instead -- close enough at icon size.
            var centre = P(box, 0.5f, 0.5f);
            var radius = box.Width * 0.36f;
            DrawRing(drawList, centre, radius, radius, 1.6f, color);
            DrawRing(drawList, centre, radius, radius * 0.38f, 1.6f, color);
        }

        private static void DrawSplineIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // An S-curve faceted out of straight segments -- the same "curve with endpoint dots" idea
            // the ImGui icon used, with no bezier primitive to draw it smooth.
            var p0 = P(box, 0.15f, 0.80f);
            var p1 = P(box, 0.40f, 0.30f);
            var p2 = P(box, 0.60f, 0.70f);
            var p3 = P(box, 0.85f, 0.20f);

            drawList.DrawLine(p0, p1, color, 2.0f);
            drawList.DrawLine(p1, p2, color, 2.0f);
            drawList.DrawLine(p2, p3, color, 2.0f);
            FillDot(drawList, p0, box.Width * 0.07f, color);
            FillDot(drawList, p3, box.Width * 0.07f, color);
        }

        private static void DrawTextIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // A text block: a frame with a few lines inside, the last one shorter.
            var frame = new UiRect(
                box.X + box.Width * 0.08f, box.Y + box.Height * 0.12f,
                box.Width * 0.84f, box.Height * 0.76f);
            drawList.DrawBorder(frame, color, 1.4f, new CornerRadius(2.0f));

            var lineHeight = frame.Height / 4.0f;
            for (var i = 0; i < 3; i++)
            {
                var y = frame.Y + lineHeight * (i + 1);
                var width = i == 2 ? frame.Width * 0.55f : frame.Width * 0.75f;
                drawList.DrawLine(
                    new Vector2(frame.X + frame.Width * 0.12f, y),
                    new Vector2(frame.X + frame.Width * 0.12f + width, y),
                    color,
                    1.4f);
            }
        }

        private static void DrawPaintIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // A brush recolouring a swatch instead of drawing a dot -- this recolours voxels that already
            // exist, rather than adding new ones the way Stroke does.
            var handleStart = P(box, 0.16f, 0.22f);
            var handleEnd = P(box, 0.58f, 0.58f);
            drawList.DrawLine(handleStart, handleEnd, color, 2.4f);

            var swatch = new UiRect(
                box.X + box.Width * 0.50f, box.Y + box.Height * 0.50f,
                box.Width * 0.36f, box.Height * 0.36f);
            drawList.FillRect(swatch, color, new CornerRadius(2.5f));
        }

        private static void DrawFloodFillIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // A tipped bucket pouring out its colour: the body as a tilted filled rounded rect, the paint
            // as a trail of shrinking drops.
            var bodyWidth = box.Width * 0.5f;
            var bodyHeight = box.Height * 0.32f;
            var bodyCentre = P(box, 0.36f, 0.38f);
            var body = new UiRect(
                bodyCentre.X - bodyWidth * 0.5f, bodyCentre.Y - bodyHeight * 0.5f,
                bodyWidth, bodyHeight);
            drawList.Rotated(body, -0.55f, color, new CornerRadius(3.0f));

            FillDot(drawList, P(box, 0.66f, 0.66f), box.Width * 0.06f, color);
            FillDot(drawList, P(box, 0.77f, 0.80f), box.Width * 0.045f, color);
            FillDot(drawList, P(box, 0.85f, 0.90f), box.Width * 0.03f, color);
        }

        private static void DrawEraseIcon(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            // An eraser block, struck through -- removing rather than adding or recolouring.
            var body = new UiRect(
                box.X + box.Width * 0.18f, box.Y + box.Height * 0.32f,
                box.Width * 0.64f, box.Height * 0.38f);
            drawList.DrawBorder(body, color, 1.6f, new CornerRadius(3.0f));
            drawList.DrawLine(P(box, 0.24f, 0.24f), P(box, 0.76f, 0.78f), color, 1.6f);
        }

        // ----- paint colour ---------------------------------------------------------------------------

        private static UiElement BuildColorGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            // The swatch opens a real picker now that there is one -- channel sliders included -- so the
            // toolbar only needs room for the swatch itself, not a copy of the picker beside it.
            var field = new ColorField
            {
                Color = vm.PaintVoxelColor,
                ShowAlpha = false,
                Width = 24.0f,
                Height = 24.0f
            };

            field.ColorChanged += (_, color) =>
                vm.PaintVoxelColor = new Vector4(color.X, color.Y, color.Z, vm.PaintVoxelColor.W);

            // Polled as well as written: the palette shares its colour with the ImGui palette and the
            // eyedropper, and neither announces a change.
            field.BindPolled(f => f.Color, () => vm.PaintVoxelColor);

            var group = Group(theme, "Paint colour", field);

            // Circle and Sphere have their own rim/interior colours instead -- "one colour" does not
            // describe a shape whose inside might not even be painted. Erase has no colour at all -- it
            // only ever removes.
            return group.BindPolled(g => g.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex)
                    is VoxelToolKind.Circle or VoxelToolKind.Sphere or VoxelToolKind.Erase
                    ? Visibility.Collapsed
                    : Visibility.Visible);
        }

        private static UiElement BuildShapeColorGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            var stroke = new ColorField
            {
                Color = vm.ShapeStrokeColor,
                ShowAlpha = false,
                Width = 24.0f,
                Height = 24.0f
            };

            stroke.ColorChanged += (_, color) =>
                vm.ShapeStrokeColor = new Vector4(color.X, color.Y, color.Z, vm.ShapeStrokeColor.W);
            stroke.BindPolled(f => f.Color, () => vm.ShapeStrokeColor);

            var fill = new ColorField
            {
                Color = vm.ShapeFillColor,
                ShowAlpha = false,
                Width = 24.0f,
                Height = 24.0f
            };

            fill.ColorChanged += (_, color) =>
                vm.ShapeFillColor = new Vector4(color.X, color.Y, color.Z, vm.ShapeFillColor.W);
            fill.BindPolled(f => f.Color, () => vm.ShapeFillColor);

            // Disabled rather than hidden when there is no fill -- the swatch stays put and just says
            // "not right now", the same contract the Place button already gives for "not available yet".
            fill.BindPolled(f => f.IsEnabled, () => vm.ShapeHasFill);

            var hasFill = new CheckBox()
                .Label("Fill")
                .CenterV()
                .OnChanged(value => vm.ShapeHasFill = value)
                .BindPolled(c => c.IsChecked, () => vm.ShapeHasFill);

            var row = new StackPanel().Horizontal(theme.Spacing).Children(
                Group(theme, "Stroke", stroke),
                Group(theme, "Fill", fill),
                hasFill);

            // Unchecking Fill leaves only the rim painted -- a ring for Circle, a shell for Sphere.
            return row.BindPolled(s => s.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex) is VoxelToolKind.Circle or VoxelToolKind.Sphere
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }

        // ----- work plane -----------------------------------------------------------------------------

        private static UiElement BuildWorkPlaneGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            var plane = new ComboBox()
                .Options(VoxelToolCatalog.PlaneNames)
                .Width(80.0f)
                .OnSelected(index => vm.SelectedWorkPlaneIndex = index)
                .BindPolled(c => c.SelectedIndex, () => vm.SelectedWorkPlaneIndex);

            // Where the plane sits along its own normal axis -- without this "XZ" alone says which two axes
            // it spans but not where along the third one it is, which is why the plane never actually
            // constrained anything before. Auto has no fixed plane to offset, so the field just disables.
            var offset = new NumericBox().Step(1.0f).Places(2).Width(56.0f).Value(vm.WorkPlaneOffset);
            offset.ValueChanged += (_, v) => vm.WorkPlaneOffset = v;
            offset.BindPolled(b => b.Value, () => vm.WorkPlaneOffset);
            offset.BindPolled(b => b.IsEnabled, () => vm.SelectedWorkPlaneIndex != 0);

            var row = new StackPanel().Horizontal(theme.Spacing * 0.5f).Children(plane, offset);
            return Group(theme, "Work plane", row);
        }

        // ----- stroke and text --------------------------------------------------------------------------

        private static UiElement BuildStrokeGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            var row = new StackPanel().Horizontal(theme.Spacing).Children(
                Divider(theme),
                IntSlider(theme, "Radius", 0, 12,
                    () => vm.StrokeRadiusVoxels,
                    v => vm.StrokeRadiusVoxels = v,
                    width: 150.0f));

            // Shown only for the tools that use a radius, matching the ImGui palette. Paint and Erase share
            // the same brush-radius field as Stroke rather than getting a field of their own. The divider
            // is part of this group rather than a fixed fixture, so hiding the group does not leave it
            // stranded with nothing to its right.
            return row.BindPolled(s => s.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex)
                    is VoxelToolKind.Stroke or VoxelToolKind.Line or VoxelToolKind.Spline
                    or VoxelToolKind.Paint or VoxelToolKind.Erase
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }

        private static UiElement BuildTextGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            var text = new TextBox()
                .Hint("Text to stamp")
                .Configure(box => box.MaxLength = 128)
                .Width(160.0f)
                .OnCommitted(value => vm.ToolText = value)
                .BindPolled(b => b.Text, () => vm.ToolText);

            var row = new StackPanel().Horizontal(theme.Spacing).Children(
                Divider(theme),
                Group(theme, "Text", text),
                IntSlider(theme, "Scale", 1, 8, () => vm.TextScale, v => vm.TextScale = v, width: 130.0f),
                IntSlider(theme, "Depth", 1, 16, () => vm.TextDepth, v => vm.TextDepth = v, width: 130.0f));

            return row.BindPolled(s => s.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex) == VoxelToolKind.Text
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }

        private static UiElement IntSlider(
            UiTheme theme,
            string label,
            int minimum,
            int maximum,
            Func<int> read,
            Action<int> write,
            float width = 170.0f)
        {
            var slider = new Slider()
                .Range(minimum, maximum, 1.0f)
                .OnChanged(value => write((int)MathF.Round(value)))
                .BindPolled(s => s.Value, () => read());

            var value = new TextBlock()
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .CenterV()
                .BindTextPolled(read, v => v.ToString());

            return new Grid().Columns("Auto, *, 28", 6.0f).Rows("Auto").Width(width).Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV().GridColumn(0),
                slider.GridColumn(1).CenterV(),
                value.GridColumn(2));
        }

        // ----- helpers --------------------------------------------------------------------------------

        /// <summary>A small caption above a control, stacked to keep a toolbar group readable without a row of its own.</summary>
        /// <summary>
        /// A captioned control, laid out inline rather than stacked.
        /// <para>
        /// Stacking read well in the old vertical palette, but in a horizontal strip it doubles the strip's
        /// height for a label that fits perfectly well beside its control -- and the strip's height comes
        /// straight off the viewport. The caption sits left, both centred, so a row of these is exactly one
        /// control tall.
        /// </para>
        /// </summary>
        private static UiElement Group(UiTheme theme, string caption, UiElement control)
        {
            return new StackPanel().Horizontal(4.0f).Children(
                Caption(caption, theme).CenterV(),
                control.CenterV());
        }

        private static TextBlock Caption(string text, UiTheme theme)
        {
            return new TextBlock()
                .Text(text)
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall);
        }

        private static UiElement Divider(UiTheme theme)
        {
            return new Border().Fill(theme.Divider).Width(1.0f).Margin(2.0f, 4.0f).StretchV();
        }
    }
}
