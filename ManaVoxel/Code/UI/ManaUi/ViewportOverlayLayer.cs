using Mana;
using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The things drawn over the scene rather than in a panel: the camera gizmo, the axis tripod, and the
    /// placement readout.
    /// <para>
    /// These are the last of the ImGui surface, and they are last because a docked panel was never the right
    /// shape for them. What they have in common is a dependence on the viewport rectangle -- which is not a
    /// thing the editor stores anywhere, but is exactly what is left of the window once the columns have
    /// taken their share. Working it out here means the overlays follow a column being resized, collapsed or
    /// dragged away without anyone telling them.
    /// </para>
    /// </summary>
    public sealed class ViewportOverlayLayer : Panel
    {
        private readonly UiElement _left;
        private readonly UiElement _right;
        private readonly float _topOffset;

        private readonly UiElement? _gizmo;
        private readonly UiElement? _tripod;

        public ViewportOverlayLayer(
            UiTheme theme,
            UiElement left,
            UiElement right,
            float topOffset,
            Func<CameraComponent?> camera,
            VoxelEditorViewModel? editor)
        {
            _left = left;
            _right = right;
            _topOffset = topOffset;

            // The layer itself is transparent to the pointer; the gizmo's own buttons are not. That is what
            // decides whether a click orbits the camera or presses a preset, and it falls out of hit testing
            // rather than having to be reported back the way the ImGui gizmo did.
            IsHitTestVisible = true;

            if (camera() is { } initial)
            {
                _gizmo = ViewportCameraGizmoManaView.Build(theme, initial);
                _tripod = ViewportCameraGizmoManaView.BuildTripod(theme, initial);

                Children.Add(_gizmo);
                Children.Add(_tripod);
            }

            if (editor != null)
            {
                Children.Add(new VoxelPlacementDepthOverlay(editor, camera));
            }
        }

        /// <summary>What is left of the window once the docked columns have taken their share.</summary>
        public UiRect Viewport
        {
            get
            {
                var left = _left.Visibility == Visibility.Collapsed ? 0.0f : _left.Bounds.Width;
                var right = _right.Visibility == Visibility.Collapsed ? 0.0f : _right.Bounds.Width;

                // A window narrower than its own columns gives a right edge left of the left one. UiRect
                // clamps that to a zero-width rect at the left edge, which is the answer wanted: no
                // viewport, rather than one overlapping the panel that took the space.
                return UiRect.FromEdges(
                    Bounds.X + left,
                    Bounds.Y + _topOffset,
                    Bounds.Right - right,
                    Bounds.Bottom);
            }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var child in Children)
            {
                child.Measure(availableSize);
            }

            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var viewport = Viewport;
            var size = new Vector2(viewport.Width, viewport.Height);

            const float margin = ViewportCameraGizmoManaView.GizmoMargin;

            foreach (var child in Children)
            {
                switch (child)
                {
                    case var _ when ReferenceEquals(child, _gizmo):
                    {
                        // Top-right of the viewport, and hidden rather than clipped when there is not room:
                        // half a gizmo is worse than none, and it would be sitting on top of the scene.
                        if (!ViewportCameraGizmoManaView.FitsIn(size))
                        {
                            child.Arrange(UiRect.Empty);
                            break;
                        }

                        child.Arrange(new UiRect(
                            viewport.Right - ViewportCameraGizmoManaView.GizmoSize - margin,
                            viewport.Y + margin,
                            ViewportCameraGizmoManaView.GizmoSize,
                            ViewportCameraGizmoManaView.GizmoSize));
                        break;
                    }

                    case var _ when ReferenceEquals(child, _tripod):
                    {
                        if (!ViewportCameraGizmoManaView.TripodFitsIn(size))
                        {
                            child.Arrange(UiRect.Empty);
                            break;
                        }

                        child.Arrange(new UiRect(
                            viewport.X + margin,
                            viewport.Bottom - ViewportCameraGizmoManaView.TripodSize - margin,
                            ViewportCameraGizmoManaView.TripodSize,
                            ViewportCameraGizmoManaView.TripodSize));
                        break;
                    }

                    default:
                        // The placement readout draws in projected screen coordinates, so its bounds are the
                        // viewport itself -- which is also what it clips against.
                        child.Arrange(viewport);
                        break;
                }
            }

            return finalSize;
        }
    }
}
