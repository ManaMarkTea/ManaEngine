namespace ManaUI
{
    /// <summary>Where a drag would land if released now, and the area it would occupy.</summary>
    public readonly struct DockDropTarget
    {
        public DockDropTarget(DockTabGroup? group, DockSide side, UiRect preview, bool isRootEdge)
        {
            Group = group;
            Side = side;
            Preview = preview;
            IsRootEdge = isRootEdge;
        }

        /// <summary>The group being dropped onto, or null when dropping against the layout's outer edge.</summary>
        public DockTabGroup? Group { get; }

        public DockSide Side { get; }

        /// <summary>The area the panel would occupy, for the drop preview.</summary>
        public UiRect Preview { get; }

        /// <summary>True when the drop splits the whole layout rather than one pane.</summary>
        public bool IsRootEdge { get; }

        public bool IsValid => !Preview.IsEmpty;
    }

    /// <summary>
    /// Works out where a dragged panel would dock.
    /// <para>
    /// Split out from the controller so the geometry -- which is all this really is -- can be tested
    /// directly, without a drag in flight.
    /// </para>
    /// </summary>
    public static class DockDropResolver
    {
        /// <summary>Band along the host's outer edge that docks against the whole layout.</summary>
        private const float RootEdgeBand = 32.0f;

        /// <summary>Fraction of a pane, from each edge, that docks to that side rather than as a tab.</summary>
        private const float EdgeFraction = 0.3f;

        public static DockDropTarget Resolve(DockHost host, Vector2 point)
        {
            var bounds = host.Bounds;
            if (bounds.IsEmpty || !bounds.Contains(point))
            {
                return default;
            }

            // The outer band is checked first: near the edge of the layout the user means "put it down
            // the whole side", not "split whichever pane happens to be under the cursor".
            var rootSide = RootEdgeAt(bounds, point);
            if (rootSide.HasValue)
            {
                return new DockDropTarget(null, rootSide.Value, HalfOf(bounds, rootSide.Value), isRootEdge: true);
            }

            var group = host.GroupAt(point);
            if (group == null)
            {
                // An empty layout takes the whole area.
                return host.RootNode == null
                    ? new DockDropTarget(null, DockSide.Center, bounds, isRootEdge: true)
                    : default;
            }

            var side = SideWithin(group.Bounds, point);
            var preview = side == DockSide.Center ? group.Bounds : HalfOf(group.Bounds, side);
            return new DockDropTarget(group, side, preview, isRootEdge: false);
        }

        private static DockSide? RootEdgeAt(UiRect bounds, Vector2 point)
        {
            var left = point.X - bounds.Left;
            var right = bounds.Right - point.X;
            var top = point.Y - bounds.Top;
            var bottom = bounds.Bottom - point.Y;

            var nearest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
            if (nearest > RootEdgeBand)
            {
                return null;
            }

            if (nearest == left)
            {
                return DockSide.Left;
            }

            if (nearest == right)
            {
                return DockSide.Right;
            }

            return nearest == top ? DockSide.Top : DockSide.Bottom;
        }

        /// <summary>
        /// Which side of a pane a point falls in. The middle of the pane means "add as a tab"; the outer
        /// third on each axis means "split that way".
        /// </summary>
        private static DockSide SideWithin(UiRect bounds, Vector2 point)
        {
            if (bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            {
                return DockSide.Center;
            }

            var u = (point.X - bounds.Left) / bounds.Width;
            var v = (point.Y - bounds.Top) / bounds.Height;

            var horizontal = MathF.Min(u, 1.0f - u);
            var vertical = MathF.Min(v, 1.0f - v);

            if (horizontal > EdgeFraction && vertical > EdgeFraction)
            {
                return DockSide.Center;
            }

            // Whichever edge is proportionally closer wins, so a tall thin pane still splits sensibly.
            if (horizontal < vertical)
            {
                return u < 0.5f ? DockSide.Left : DockSide.Right;
            }

            return v < 0.5f ? DockSide.Top : DockSide.Bottom;
        }

        private static UiRect HalfOf(UiRect bounds, DockSide side)
        {
            return side switch
            {
                DockSide.Left => new UiRect(bounds.X, bounds.Y, bounds.Width * 0.5f, bounds.Height),
                DockSide.Right => new UiRect(bounds.Center.X, bounds.Y, bounds.Width * 0.5f, bounds.Height),
                DockSide.Top => new UiRect(bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.5f),
                DockSide.Bottom => new UiRect(bounds.X, bounds.Center.Y, bounds.Width, bounds.Height * 0.5f),
                _ => bounds
            };
        }
    }
}
