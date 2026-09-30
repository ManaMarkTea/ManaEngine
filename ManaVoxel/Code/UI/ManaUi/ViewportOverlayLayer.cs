using Mana;
using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>Where in the viewport an overlay sits.</summary>
    public enum ViewportSlot
    {
        /// <summary>The whole viewport. For overlays that draw in projected screen coordinates.</summary>
        Fill,

        TopRight,
        RightTools,
        BottomLeft,
        BottomRight
    }

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
        private readonly Func<float> _leftWidth;
        private readonly Func<float> _rightWidth;
        private readonly Func<float> _topHeight;
        private readonly Func<float> _bottomHeight;

        private readonly List<Slot> _slots = new();

        private UiRect _arrangedAgainst;

        public ViewportOverlayLayer(
            UiTheme theme,
            Func<float> leftWidth,
            Func<float> rightWidth,
            Func<float> topHeight,
            Func<float> bottomHeight,
            Func<CameraComponent?> camera,
            VoxelEditorViewModel? editor)
        {
            _leftWidth = leftWidth;
            _rightWidth = rightWidth;
            _topHeight = topHeight;
            _bottomHeight = bottomHeight;

            // The layer itself is transparent to the pointer; the gizmo's own buttons are not. That is what
            // decides whether a click orbits the camera or presses a preset, and it falls out of hit testing
            // rather than having to be reported back the way the ImGui gizmo did.
            IsHitTestVisible = true;

            if (camera() is { } initial)
            {
                Add(
                    ViewportCameraGizmoManaView.Build(theme, initial),
                    ViewportSlot.TopRight,
                    new Vector2(ViewportCameraGizmoManaView.GizmoSize),
                    ViewportCameraGizmoManaView.FitsIn);

                Add(
                    ViewportCameraGizmoManaView.BuildTripod(theme, initial),
                    ViewportSlot.BottomLeft,
                    new Vector2(ViewportCameraGizmoManaView.TripodSize),
                    ViewportCameraGizmoManaView.TripodFitsIn);
            }

            if (editor != null)
            {
                Add(new VoxelPlacementDepthOverlay(editor, camera), ViewportSlot.Fill);
                Add(new SelectionTransformOverlay(editor, camera), ViewportSlot.Fill);
                Add(new ViewportToolsDrawer(theme, editor), ViewportSlot.RightTools,
                    new Vector2(300, 350), size => size.X >= 320 && size.Y >= 180);
            }

            // Needs neither a camera nor an editor context -- it reads ManaAssetDownloadActivity directly,
            // which stays permanently idle (and the widget Collapsed) outside a browser build.
            Add(
                AssetDownloadStatusManaView.Build(theme),
                ViewportSlot.BottomRight,
                new Vector2(AssetDownloadStatusManaView.Width, AssetDownloadStatusManaView.Height));

            // The columns are siblings, and this layer is arranged before them so that they draw over it --
            // which means their bounds are a frame stale, and zero on the first frame. Reading their widths
            // from the values that own them instead, and re-arranging whenever the answer changes, is what
            // makes the overlays follow a resize. Without it the gizmo stays where the very first layout put
            // it: the top-right of the whole window, underneath the right column, invisible.
            this.BindPolled(l => l.ViewportTracker, () => Viewport);
        }

        /// <summary>What is left of the window once the docked columns have taken their share.</summary>
        public UiRect Viewport
        {
            get
            {
                // A window narrower than its own columns gives a right edge left of the left one. UiRect
                // clamps that to a zero-width rect at the left edge, which is the answer wanted: no
                // viewport, rather than one overlapping the panel that took the space.
                return UiRect.FromEdges(
                    Bounds.X + MathF.Max(0.0f, _leftWidth()),
                    Bounds.Y + MathF.Max(0.0f, _topHeight()),
                    Bounds.Right - MathF.Max(0.0f, _rightWidth()),
                    Bounds.Bottom - MathF.Max(0.0f, _bottomHeight()));
            }
        }

        /// <summary>
        /// Binding target only. Its whole job is to notice the viewport moving and invalidate the arrange
        /// that depends on it; nothing reads the value back.
        /// </summary>
        public UiRect ViewportTracker
        {
            get => _arrangedAgainst;
            set
            {
                if (_arrangedAgainst.Equals(value))
                {
                    return;
                }

                _arrangedAgainst = value;
                InvalidateArrange();
            }
        }

        public void Add(UiElement child, ViewportSlot slot, Vector2 size = default, Func<Vector2, bool>? fits = null)
        {
            _slots.Add(new Slot(child, slot, size, fits));
            Children.Add(child);
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
            _arrangedAgainst = viewport;

            var size = new Vector2(viewport.Width, viewport.Height);

            const float margin = ViewportCameraGizmoManaView.GizmoMargin;

            foreach (var slot in _slots)
            {
                // Hidden rather than clipped when there is not room: half a gizmo is worse than none, and it
                // would be sitting on top of the scene.
                if (slot.Fits != null && !slot.Fits(size))
                {
                    slot.Child.Arrange(UiRect.Empty);
                    continue;
                }

                slot.Child.Arrange(slot.Placement switch
                {
                    ViewportSlot.TopRight => new UiRect(
                        viewport.Right - slot.Size.X - margin,
                        viewport.Y + margin,
                        slot.Size.X,
                        slot.Size.Y),

                    ViewportSlot.RightTools => new UiRect(
                        viewport.Right - slot.Size.X - margin,
                        viewport.Y + ViewportCameraGizmoManaView.GizmoSize + margin + 8,
                        slot.Size.X,
                        MathF.Max(0, MathF.Min(slot.Size.Y, viewport.Height - ViewportCameraGizmoManaView.GizmoSize - margin * 2 - 8))),

                    ViewportSlot.BottomLeft => new UiRect(
                        viewport.X + margin,
                        viewport.Bottom - slot.Size.Y - margin,
                        slot.Size.X,
                        slot.Size.Y),

                    ViewportSlot.BottomRight => new UiRect(
                        viewport.Right - slot.Size.X - margin,
                        viewport.Bottom - slot.Size.Y - margin,
                        slot.Size.X,
                        slot.Size.Y),

                    // Fill: an overlay drawing in projected screen coordinates wants the viewport itself,
                    // which is also what it clips against.
                    _ => viewport
                });
            }

            return finalSize;
        }

        private readonly record struct Slot(
            UiElement Child,
            ViewportSlot Placement,
            Vector2 Size,
            Func<Vector2, bool>? Fits);
    }
}
