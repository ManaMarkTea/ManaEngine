using ManaUI;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// The viewport's navigation gizmo, after Blender's: the world axes as balls (click one to look down it, drag
    /// to orbit), with round zoom, pan and projection buttons under it.
    /// </summary>
    public static class ViewportCameraGizmoManaView
    {
        /// <summary>The axis ball's diameter.</summary>
        public const float GizmoSize = 96.0f;
        public const float GizmoMargin = 18.0f;

        private const float ButtonSize = 28.0f;
        private const float ButtonGap = 4.0f;
        private const int ButtonCount = 4;

        /// <summary>The ball and the button column under it.</summary>
        public const float GizmoHeight = GizmoSize + ButtonGap * 2.0f + ButtonCount * (ButtonSize + ButtonGap);

        /// <summary>Below this much viewport the gizmo is more in the way than it is useful.</summary>
        public static bool FitsIn(Vector2 sceneSize) =>
            sceneSize.X >= GizmoSize + GizmoMargin * 2.0f && sceneSize.Y >= GizmoHeight + GizmoMargin * 2.0f;

        public static UiElement Build(UiTheme theme, CameraComponent camera, Action<UiElement>? openSettings = null)
        {
            var column = (GizmoSize - ButtonSize) * 0.5f;
            var canvas = new Canvas().Width(GizmoSize).Height(GizmoHeight).Children(
                new NavigationGizmo(camera).At(0.0f, 0.0f).Width(GizmoSize).Height(GizmoSize));

            var buttons = new[]
            {
                new GizmoRoundButton(DrawZoom, "Zoom: drag up to go in, down to come out.")
                {
                    Dragged = delta => camera.Dolly(-delta.Y)
                },
                new GizmoRoundButton(DrawPan, "Pan: drag to slide the view.")
                {
                    // The camera measures pans in window pixels; the UI is laid out over the display scale.
                    Dragged = delta => camera.Pan(delta)
                },
                new GizmoRoundButton((list, box, color) => DrawProjection(list, box, color, camera.OrthoGraphic),
                    "Switch between perspective and orthographic.")
                {
                    Clicked = () => camera.SetProjectionMode(!camera.OrthoGraphic),
                    IsOn = () => camera.OrthoGraphic
                }
            };

            for (var i = 0; i < buttons.Length; i++)
            {
                canvas.Children.Add(buttons[i]
                    .At(column, GizmoSize + ButtonGap * 2.0f + i * (ButtonSize + ButtonGap))
                    .Width(ButtonSize).Height(ButtonSize));
            }

            if (openSettings != null)
            {
                GizmoRoundButton? settings = null;
                settings = new GizmoRoundButton(DrawSettings, "Camera settings: projection, zoom, clip planes and navigation.")
                {
                    Clicked = () =>
                    {
                        if (settings != null)
                        {
                            openSettings(settings);
                        }
                    }
                };
                canvas.Children.Add(settings.At(column, GizmoSize + ButtonGap * 2 + 3 * (ButtonSize + ButtonGap))
                    .Width(ButtonSize).Height(ButtonSize));
            }
            return canvas;
        }

        private static void DrawSettings(ManaUiDrawList list, UiRect box, Vector4 color)
        {
            for (var i = 0; i < 3; i++)
            {
                var y = box.Y + box.Height * (0.25f + i * 0.25f);
                var x = box.X + box.Width * (i == 1 ? 0.65f : 0.35f);
                list.DrawLine(new Vector2(box.X + box.Width * 0.15f, y), new Vector2(box.Right - box.Width * 0.15f, y), color, 1.5f);
                list.DrawBox(new UiRect(x - 2, y - 3, 4, 6), color, color, 0, new CornerRadius(1));
            }
        }

        private static Vector2 P(UiRect box, float tx, float ty) =>
            new(box.X + box.Width * tx, box.Y + box.Height * ty);

        /// <summary>A magnifier: a ring and its handle.</summary>
        private static void DrawZoom(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            var centre = P(box, 0.42f, 0.42f);
            var radius = box.Width * 0.26f;
            drawList.DrawBorder(new UiRect(centre.X - radius, centre.Y - radius, radius * 2.0f, radius * 2.0f),
                color, 1.6f, new CornerRadius(radius));
            drawList.DrawLine(P(box, 0.62f, 0.62f), P(box, 0.86f, 0.86f), color, 2.0f);
        }

        /// <summary>Pan: four arrows out from the middle.</summary>
        private static void DrawPan(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            drawList.DrawLine(P(box, 0.5f, 0.12f), P(box, 0.5f, 0.88f), color, 1.6f);
            drawList.DrawLine(P(box, 0.12f, 0.5f), P(box, 0.88f, 0.5f), color, 1.6f);
            foreach (var (tip, back) in new[]
                     {
                         (P(box, 0.5f, 0.12f), new Vector2(0, 1)), (P(box, 0.5f, 0.88f), new Vector2(0, -1)),
                         (P(box, 0.12f, 0.5f), new Vector2(1, 0)), (P(box, 0.88f, 0.5f), new Vector2(-1, 0))
                     })
            {
                var side = new Vector2(-back.Y, back.X) * box.Width * 0.14f;
                var foot = tip + back * box.Width * 0.16f;
                drawList.DrawLine(tip, foot + side, color, 1.6f);
                drawList.DrawLine(tip, foot - side, color, 1.6f);
            }
        }

        /// <summary>The gizmo toggle: a move handle's three arms, struck through while gizmos are hidden.</summary>
        public static void DrawGizmos(ManaUiDrawList drawList, UiRect box, Vector4 color, bool hidden)
        {
            const float thickness = 1.6f;
            var centre = P(box, 0.42f, 0.62f);
            drawList.DrawLine(centre, P(box, 0.42f, 0.14f), color, thickness);
            drawList.DrawLine(centre, P(box, 0.84f, 0.62f), color, thickness);
            drawList.DrawLine(centre, P(box, 0.16f, 0.88f), color, thickness);
            if (hidden)
            {
                drawList.DrawLine(P(box, 0.14f, 0.14f), P(box, 0.86f, 0.90f), color, thickness);
            }
        }

        /// <summary>The room toggle: a floor, with the two back walls standing on it while the room is shown.</summary>
        public static void DrawRoom(ManaUiDrawList drawList, UiRect box, Vector4 color, bool shown)
        {
            const float thickness = 1.6f;
            var left = P(box, 0.14f, 0.80f);
            var corner = P(box, 0.50f, 0.62f);
            var right = P(box, 0.86f, 0.80f);
            var front = P(box, 0.50f, 0.96f);
            drawList.DrawLine(left, corner, color, thickness);
            drawList.DrawLine(corner, right, color, thickness);
            drawList.DrawLine(right, front, color, thickness);
            drawList.DrawLine(front, left, color, thickness);
            if (shown)
            {
                drawList.DrawLine(left, P(box, 0.14f, 0.22f), color, thickness);
                drawList.DrawLine(corner, P(box, 0.50f, 0.04f), color, thickness);
                drawList.DrawLine(right, P(box, 0.86f, 0.22f), color, thickness);
            }
        }

        /// <summary>A frustum for perspective, a box for orthographic -- the pair every 3D tool draws.</summary>
        public static void DrawProjection(ManaUiDrawList drawList, UiRect box, Vector4 color, bool ortho)
        {
            const float thickness = 1.6f;
            var topLeft = ortho ? P(box, 0.22f, 0.22f) : P(box, 0.34f, 0.22f);
            var topRight = ortho ? P(box, 0.78f, 0.22f) : P(box, 0.66f, 0.22f);
            var bottomLeft = P(box, 0.16f, 0.78f);
            var bottomRight = P(box, 0.84f, 0.78f);

            drawList.DrawLine(topLeft, topRight, color, thickness);
            drawList.DrawLine(bottomLeft, bottomRight, color, thickness);
            drawList.DrawLine(topLeft, bottomLeft, color, thickness);
            drawList.DrawLine(topRight, bottomRight, color, thickness);
        }
    }

    /// <summary>
    /// The axis ball: the six world axis ends projected from the camera, nearest drawn last. A click on an end
    /// looks down that axis at the scene (again on the one already faced turns round); a drag orbits.
    /// </summary>
    public sealed class NavigationGizmo : Control
    {
        public const float AxisLength = 34.0f;
        private const float BallRadius = 9.0f;
        private const float DragThreshold = 3.0f;
        private const float OrbitRadiansPerPixel = 0.01f;

        public static readonly Vector4 XColor = new(0.96f, 0.22f, 0.32f, 1.0f);
        public static readonly Vector4 YColor = new(0.45f, 0.75f, 0.10f, 1.0f);
        public static readonly Vector4 ZColor = new(0.17f, 0.52f, 0.96f, 1.0f);

        private static readonly (string Label, Vector3 Axis, Vector4 Color, CameraViewPreset Preset, CameraViewPreset Opposite)[] Ends =
        {
            ("X", Vector3.UnitX, XColor, CameraViewPreset.Right, CameraViewPreset.Left),
            ("-X", -Vector3.UnitX, XColor, CameraViewPreset.Left, CameraViewPreset.Right),
            ("Y", Vector3.UnitY, YColor, CameraViewPreset.Top, CameraViewPreset.Bottom),
            ("-Y", -Vector3.UnitY, YColor, CameraViewPreset.Bottom, CameraViewPreset.Top),
            ("Z", Vector3.UnitZ, ZColor, CameraViewPreset.Front, CameraViewPreset.Back),
            ("-Z", -Vector3.UnitZ, ZColor, CameraViewPreset.Back, CameraViewPreset.Front)
        };

        private readonly CameraComponent _camera;
        private int _hovered = -1;
        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressedAt;
        private Vector2 _lastPointer;

        public NavigationGizmo(CameraComponent camera)
        {
            _camera = camera;
            this.WithTooltip("Click an axis to look along it; drag to orbit.");
        }

        private Vector2 Centre => new(Bounds.X + Bounds.Width * 0.5f, Bounds.Y + Bounds.Height * 0.5f);

        private float Radius => MathF.Min(Bounds.Width, Bounds.Height) * 0.5f;

        protected override Vector2 MeasureOverride(Vector2 availableSize) => new(ViewportCameraGizmoManaView.GizmoSize);

        /// <summary>Round, so the corners of its box still reach the scene.</summary>
        public override UiElement? HitTest(Vector2 point) =>
            Visibility == Visibility.Visible && Vector2.Distance(point, Centre) <= Radius ? this : null;

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var centre = Centre;
            if (IsPointerOver || _pressed)
            {
                var radius = Radius;
                drawList.FillRect(new UiRect(centre.X - radius, centre.Y - radius, radius * 2.0f, radius * 2.0f),
                    theme.Text with { W = _dragging ? 0.22f : 0.14f }, new CornerRadius(radius));
            }

            var ends = Project();
            foreach (var index in Enumerable.Range(0, ends.Length).OrderBy(i => ends[i].Depth))
            {
                var end = ends[index];
                var positive = !end.Label.StartsWith('-');
                var tip = centre + end.Offset;
                var hovered = index == _hovered && !_dragging;

                // Positive ends are solid and joined to the middle; negative ones float, faint, as in Blender.
                if (positive && end.Offset.LengthSquared() > 4.0f)
                {
                    drawList.DrawLine(centre, tip - Vector2.Normalize(end.Offset) * BallRadius * 0.6f, end.Color, 2.2f);
                }

                var ball = new UiRect(tip.X - BallRadius, tip.Y - BallRadius, BallRadius * 2.0f, BallRadius * 2.0f);
                var fill = positive ? end.Color : Vector4.Lerp(theme.Surface, end.Color, 0.45f) with { W = 0.9f };
                drawList.FillRect(ball, fill, new CornerRadius(BallRadius));
                drawList.DrawBorder(ball, hovered ? theme.Text : end.Color, hovered ? 2.0f : 1.2f, new CornerRadius(BallRadius));

                if (positive || hovered)
                {
                    Letter(drawList, theme, end.Label, ball, positive ? new Vector4(0.05f, 0.05f, 0.05f, 1.0f) : theme.Text);
                }
            }
        }

        private void Letter(ManaUiDrawList drawList, UiTheme theme, string text, UiRect ball, Vector4 color)
        {
            if (Root is not { } root)
            {
                return;
            }

            var layout = new TextLayout { Font = root.Fonts.Get(theme.FontFamilyMedium, theme.FontSizeSmall, root.Scale), Text = text };
            layout.Update();
            var size = layout.Size;
            layout.Render(drawList, new UiRect(ball.X + (ball.Width - size.X) * 0.5f, ball.Y + (ball.Height - size.Y) * 0.5f, size.X, size.Y), color);
        }

        private AxisLine[] Project() =>
            Ends.Select(end => Project(end.Label, end.Axis, _camera._ViewMatrix, end.Color)).ToArray();

        /// <summary>The axis end nearest the camera under a point, or -1.</summary>
        private int EndAt(Vector2 point)
        {
            var ends = Project();
            var best = -1;
            for (var i = 0; i < ends.Length; i++)
            {
                if (Vector2.Distance(point, Centre + ends[i].Offset) <= BallRadius + 1.0f
                    && (best < 0 || ends[i].Depth > ends[best].Depth))
                {
                    best = i;
                }
            }

            return best;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_pressed)
            {
                if (!_dragging && Vector2.Distance(e.Position, _pressedAt) > DragThreshold)
                {
                    _dragging = true;
                }

                if (_dragging)
                {
                    var delta = e.Position - _lastPointer;
                    // Dragging right swings the scene right with the pointer: the camera goes round the other way.
                    _camera.Orbit(-delta.X * OrbitRadiansPerPixel, -delta.Y * OrbitRadiansPerPixel);
                }

                _lastPointer = e.Position;
                e.Handled = true;
            }
            else
            {
                _hovered = EndAt(e.Position);
            }

            InvalidateVisual();
        }

        protected override void OnPointerExited(UiPointerEventArgs e)
        {
            _hovered = -1;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            _pressed = true;
            _dragging = false;
            _pressedAt = _lastPointer = e.Position;
            CapturePointer();
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
            if (!_dragging && EndAt(e.Position) is var index and >= 0)
            {
                var end = Ends[index];
                var facing = Project()[index].Depth > 0.98f;
                _camera.SetViewPreset(facing ? end.Opposite : end.Preset);
            }

            _dragging = false;
            _hovered = EndAt(e.Position);
            InvalidateVisual();
            e.Handled = true;
        }

        /// <summary>
        /// A world axis in screen terms: where it points, foreshortened by how much of it is aimed at the camera
        /// (Y negated, screen space growing downward). Unscaled, so looking down an axis leaves a stub at the hub
        /// rather than a spoke the rounding points somewhere definite and wrong.
        /// </summary>
        public static AxisLine Project(string label, Vector3 axis, Matrix4x4 view, Vector4 color)
        {
            var viewAxis = Vector3.TransformNormal(axis, view);
            return new AxisLine(label, new Vector2(viewAxis.X, -viewAxis.Y) * AxisLength, viewAxis.Z, color);
        }

        /// <summary>One axis end. Depth is the view-space Z, which orders them.</summary>
        public readonly record struct AxisLine(string Label, Vector2 Offset, float Depth, Vector4 Color)
        {
            /// <summary>View space looks down -Z, so a positive Z comes out of the screen.</summary>
            public bool PointsAtCamera => Depth >= 0.0f;
        }
    }

    /// <summary>A round button under the axis ball: a click, or a drag reported a step at a time.</summary>
    public sealed class GizmoRoundButton : Control
    {
        private readonly Action<ManaUiDrawList, UiRect, Vector4> _draw;
        private bool _pressed;
        private Vector2 _last;

        public GizmoRoundButton(Action<ManaUiDrawList, UiRect, Vector4> draw, string tooltip)
        {
            _draw = draw;
            this.WithTooltip(tooltip);
        }

        public Action? Clicked { get; init; }
        public Action<Vector2>? Dragged { get; init; }
        public Func<bool>? IsOn { get; init; }

        protected override Vector2 MeasureOverride(Vector2 availableSize) => new(28.0f);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var radius = MathF.Min(Bounds.Width, Bounds.Height) * 0.5f;
            var on = IsOn?.Invoke() == true;
            var fill = on ? theme.Accent
                : _pressed ? theme.Text with { W = 0.30f }
                : IsPointerOver ? theme.Text with { W = 0.20f }
                : theme.Surface with { W = 0.55f };
            drawList.FillRect(Bounds, fill, new CornerRadius(radius));
            _draw(drawList, Bounds.Deflate(new Thickness(radius * 0.45f)), on ? theme.TextOnAccent : theme.Text);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            _pressed = true;
            _last = e.Position;
            CapturePointer();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_pressed && Dragged != null)
            {
                Dragged((e.Position - _last) * (Root?.ScreenScale ?? 1.0f));
                _last = e.Position;
                e.Handled = true;
            }

            InvalidateVisual();
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            ReleasePointerCapture();
            if (Bounds.Contains(e.Position))
            {
                Clicked?.Invoke();
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }
}
