using ManaUI;

// Mana has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// The viewport camera gizmo: six view presets and a projection toggle, over the scene.
    /// <para>
    /// Real elements rather than an immediate-mode draw list, which is what the ImGui version had to be.
    /// That matters more here than in a panel: the gizmo sits on top of the viewport, and whether the pointer
    /// is over it decides whether a click orbits the camera or presses a button. Hit testing it as part of
    /// the tree gives that answer for free, where the ImGui version had to report it back by hand.
    /// </para>
    /// </summary>
    public static class ViewportCameraGizmoManaView
    {
        public const float GizmoSize = 166.0f;
        public const float GizmoMargin = 18.0f;
        public const float TripodSize = 94.0f;

        /// <summary>Below this much viewport the gizmo is more in the way than it is useful.</summary>
        public static bool FitsIn(Vector2 sceneSize) =>
            sceneSize.X >= GizmoSize + GizmoMargin * 2.0f && sceneSize.Y >= GizmoSize + GizmoMargin * 2.0f;

        public static bool TripodFitsIn(Vector2 sceneSize) =>
            sceneSize.X >= TripodSize + GizmoMargin * 2.0f && sceneSize.Y >= TripodSize + GizmoMargin * 2.0f;

        /// <summary>
        /// The preset cluster. Laid out on a canvas at the same coordinates the ImGui version used, because
        /// the arrangement is a picture of a cube seen from above rather than anything a layout panel
        /// describes: the arrows are its four sides and the face in the middle is its top.
        /// </summary>
        public static UiElement Build(UiTheme theme, CameraComponent camera)
        {
            var projection = new Button()
                .Variant(ButtonVariant.Subtle)
                .OnClick(() => camera.SetProjectionMode(!camera.OrthoGraphic))
                .WithTooltip("Switch between perspective and orthographic.");

            // Polled rather than set once: the projection is also changed from the inspector and by the
            // presets, and a label that disagreed with the camera would be worse than no label.
            projection.BindPolled(b => b.Text, () => camera.OrthoGraphic ? "ORTHO" : "PERSP");

            return new Canvas().Width(GizmoSize).Height(GizmoSize).Children(
                // The gizmo sits directly on the scene, which is the case backdrop blur exists for: it can
                // stay light enough to see what is under it and still be readable against anything.
                new Border()
                    .Fill(theme.Frosted(theme.Surface with { W = 0.84f }))
                    .Blur(theme.BackdropBlur)
                    .Outline(theme.Border, 1.0f)
                    .Rounded(10.0f)
                    .Shadow(10.0f)
                    .Anchor(left: 0.0f, top: 0.0f, right: 0.0f, bottom: 0.0f),

                Arrow(theme, camera, CameraViewPreset.Back, UiIconDirection.Up, "Back")
                    .At(66.0f, 15.0f).Width(34.0f).Height(24.0f),

                Arrow(theme, camera, CameraViewPreset.Left, UiIconDirection.Left, "Left")
                    .At(20.0f, 62.0f).Width(24.0f).Height(34.0f),

                Face(theme, camera).At(56.0f, 54.0f).Width(54.0f).Height(46.0f),

                Arrow(theme, camera, CameraViewPreset.Right, UiIconDirection.Right, "Right")
                    .At(122.0f, 62.0f).Width(24.0f).Height(34.0f),

                Arrow(theme, camera, CameraViewPreset.Front, UiIconDirection.Down, "Front")
                    .At(66.0f, 106.0f).Width(34.0f).Height(24.0f),

                new Button()
                    .Label("BOTTOM")
                    .Variant(ButtonVariant.Subtle)
                    .OnClick(() => camera.SetViewPreset(CameraViewPreset.Bottom))
                    .WithTooltip("Bottom")
                    .At(12.0f, 136.0f).Width(64.0f).Height(22.0f),

                projection.At(90.0f, 136.0f).Width(56.0f).Height(22.0f));
        }

        /// <summary>The cube's top face, and the only preset that reads as a surface rather than a direction.</summary>
        private static UiElement Face(UiTheme theme, CameraComponent camera)
        {
            return new Button()
                .Label("TOP")
                .Variant(ButtonVariant.Primary)
                .OnClick(() => camera.SetViewPreset(CameraViewPreset.Top))
                .WithTooltip("Top");
        }

        private static UiElement Arrow(
            UiTheme theme,
            CameraComponent camera,
            CameraViewPreset preset,
            UiIconDirection direction,
            string tooltip)
        {
            return new GizmoArrow(direction)
            {
                Command = () => camera.SetViewPreset(preset)
            }.WithTooltip(tooltip);
        }

        /// <summary>
        /// The axis tripod: where world X, Y and Z point from where the camera is standing.
        /// <para>
        /// Not interactive, and deliberately -- it is a readout. Clicking it would be indistinguishable from
        /// clicking the scene behind it, which is what you actually want at the bottom-left corner.
        /// </para>
        /// </summary>
        public static UiElement BuildTripod(UiTheme theme, CameraComponent camera)
        {
            return new AxisTripod(camera) { IsHitTestVisible = false };
        }
    }

    /// <summary>
    /// One of the four direction arrows. A control rather than a Button with an icon, because the hit area
    /// wanted is the triangle's box while the artwork is the triangle itself.
    /// </summary>
    public sealed class GizmoArrow : Control
    {
        private readonly UiIconDirection _direction;

        private bool _pressed;

        public GizmoArrow(UiIconDirection direction)
        {
            _direction = direction;
        }

        public Action? Command { get; set; }

        protected override bool IsSelfHitTestVisible => true;

        protected override Vector2 MeasureOverride(Vector2 availableSize) => new(24.0f, 24.0f);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var color = _pressed || IsPointerOver ? theme.Accent : theme.Text;

            // Inset so the triangle has room to look like one; the hit area stays the full box.
            UiIcons.Triangle(drawList, Bounds.Deflate(new Thickness(3.0f)), _direction, color);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            _pressed = true;
            CapturePointer();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            ReleasePointerCapture();
            InvalidateVisual();

            // Only a release still over the arrow counts, so a press dragged off it is a cancel.
            if (Bounds.Contains(e.Position))
            {
                Command?.Invoke();
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// The three world axes, projected to screen and drawn back to front.
    /// <para>
    /// Every line here is diagonal, which is the whole point of the readout and was also the reason this
    /// could not be drawn at all until the pipeline could rotate a box.
    /// </para>
    /// </summary>
    public sealed class AxisTripod : UiElement
    {
        private const float AxisLength = 34.0f;
        private const float HubRadius = 3.4f;

        private static readonly Vector4 XColor = new(0.88f, 0.20f, 0.25f, 1.0f);
        private static readonly Vector4 YColor = new(0.20f, 0.70f, 0.34f, 1.0f);
        private static readonly Vector4 ZColor = new(0.23f, 0.44f, 0.92f, 1.0f);

        private readonly CameraComponent _camera;

        public AxisTripod(CameraComponent camera)
        {
            _camera = camera;
            Width = ViewportCameraGizmoManaView.TripodSize;
            Height = ViewportCameraGizmoManaView.TripodSize;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize) =>
            new(ViewportCameraGizmoManaView.TripodSize, ViewportCameraGizmoManaView.TripodSize);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var centre = new Vector2(Bounds.X + 38.0f, Bounds.Y + 58.0f);

            var axes = new[]
            {
                Project("X", Vector3.UnitX, XColor),
                Project("Y", Vector3.UnitY, YColor),
                Project("Z", Vector3.UnitZ, ZColor)
            };

            // Back to front, so an axis pointing away from the camera is drawn under the ones in front of
            // it rather than over them -- which is the only cue that says which way it is going.
            Array.Sort(axes, static (a, b) => a.Depth.CompareTo(b.Depth));

            foreach (var axis in axes)
            {
                var end = centre + axis.Offset;
                var faded = axis.Color with { W = axis.PointsAtCamera ? 1.0f : 0.72f };

                // Nothing to draw a line along when the axis is aimed at the viewer, and a 1px smear where
                // the tip already is reads as dirt rather than as a spoke.
                if (axis.Offset.LengthSquared() > 4.0f)
                {
                    drawList.DrawLine(centre, end, faded, 2.2f);
                }

                var tip = new UiRect(end.X - 4.0f, end.Y - 4.0f, 8.0f, 8.0f);

                // Filled toward the viewer, hollow away from it. With honest foreshortening the spoke length
                // no longer distinguishes the two -- an axis pointing at you and one pointing directly away
                // project to the same stub -- so the tip has to.
                if (axis.PointsAtCamera)
                {
                    drawList.FillRect(tip, faded, new CornerRadius(4.0f));
                }
                else
                {
                    drawList.DrawBox(tip, theme.Surface with { W = 0.9f }, faded, 1.6f, new CornerRadius(4.0f));
                }

                Label(drawList, theme, axis, end, faded);
            }

            drawList.FillRect(
                new UiRect(centre.X - HubRadius, centre.Y - HubRadius, HubRadius * 2.0f, HubRadius * 2.0f),
                theme.TextMuted,
                new CornerRadius(HubRadius));
        }

        private void Label(ManaUiDrawList drawList, UiTheme theme, AxisLine axis, Vector2 end, Vector4 color)
        {
            var root = Root;
            if (root == null)
            {
                return;
            }

            var font = root.Fonts.Get(theme.FontFamilyMedium, theme.FontSizeSmall, root.Scale);
            // A foreshortened axis has no direction worth pushing its label along, so it goes straight up
            // off the tip -- which is where there is room, the other labels being out along their spokes.
            var direction = axis.Offset.LengthSquared() > 4.0f
                ? Vector2.Normalize(axis.Offset)
                : new Vector2(0.0f, -1.0f);

            var layout = new TextLayout { Font = font, Text = axis.Label };
            layout.Update();
            var size = layout.Size;
            var padding = new Vector2(4.0f, 2.0f);
            var boxSize = size + padding * 2.0f;

            // Pushed past the tip along the axis, then held inside the element -- an axis pointing straight
            // at the edge would otherwise put its label outside the tripod entirely.
            var boxCentre = end + direction * 12.0f;
            var position = Vector2.Clamp(
                boxCentre - boxSize * 0.5f,
                new Vector2(Bounds.X + 2.0f, Bounds.Y + 2.0f),
                new Vector2(Bounds.Right - boxSize.X - 2.0f, Bounds.Bottom - boxSize.Y - 2.0f));

            var box = new UiRect(position.X, position.Y, boxSize.X, boxSize.Y);

            drawList.DrawBox(
                box,
                theme.Frosted(theme.Surface with { W = 0.88f }),
                theme.Border,
                1.0f,
                new CornerRadius(4.0f),
                theme.BackdropBlur);
            layout.Render(drawList, box.Deflate(new Thickness(padding.X, padding.Y)), color);
        }

        private AxisLine Project(string label, Vector3 axis, Vector4 color) =>
            Project(label, axis, _camera._ViewMatrix, color);

        /// <summary>
        /// A world axis in screen terms: simply where it points, foreshortened by how much of it is aimed at
        /// the camera. Y is negated because screen space grows downward and world space does not.
        /// <para>
        /// The projection is used as it comes, rather than being normalised and re-scaled to a floored
        /// length. That floor is what made the tripod lie: an axis pointing near enough along the view
        /// direction projects to almost nothing, so its direction is decided by the rounding, and rescaling
        /// that to a quarter-length spoke pointed it somewhere definite and wrong. Looking down an axis and
        /// tilting a fiftieth of a radian flipped it end to end.
        /// </para>
        /// <para>
        /// Unscaled, such an axis collapses to a stub at the hub -- which is what it looks like, and what
        /// <see cref="AxisLine.PointsAtCamera"/> is for: the tip says toward or away where a spoke no longer
        /// can.
        /// </para>
        /// <para>
        /// Takes a view matrix rather than a camera so it can be checked: a CameraComponent subscribes to
        /// the window's resize event as it is built, so one cannot exist without a window.
        /// </para>
        /// </summary>
        public static AxisLine Project(string label, Vector3 axis, Matrix4x4 view, Vector4 color)
        {
            var viewAxis = Vector3.TransformNormal(axis, view);

            return new AxisLine(
                label,
                new Vector2(viewAxis.X, -viewAxis.Y) * AxisLength,
                viewAxis.Z,
                color);
        }

        /// <summary>One axis, ready to draw. Depth is the view-space Z, which is what orders them.</summary>
        public readonly record struct AxisLine(string Label, Vector2 Offset, float Depth, Vector4 Color)
        {
            /// <summary>
            /// True when the axis comes toward the viewer. View space looks down -Z, so a positive Z is
            /// out of the screen.
            /// </summary>
            public bool PointsAtCamera => Depth >= 0.0f;
        }
    }
}
