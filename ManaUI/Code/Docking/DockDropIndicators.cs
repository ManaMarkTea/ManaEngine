namespace ManaUI
{
    /// <summary>One drop indicator: the side it commits to, and the button you aim at to get it.</summary>
    public readonly struct DockDropIndicator
    {
        public DockDropIndicator(DockSide side, UiRect box, bool isRootEdge)
        {
            Side = side;
            Box = box;
            IsRootEdge = isRootEdge;
        }

        public DockSide Side { get; }

        /// <summary>The button's rectangle, in root coordinates.</summary>
        public UiRect Box { get; }

        /// <summary>True for the four buttons that split the whole layout rather than one pane.</summary>
        public bool IsRootEdge { get; }
    }

    /// <summary>
    /// The aim-able drop targets shown while a panel is being dragged: a five-way cluster over the pane
    /// under the pointer, and four more against the outer edges of the layout.
    /// <para>
    /// Before these, a drop was resolved purely from where the pointer happened to be -- the middle 40% of
    /// a pane meant "tab", the outer band on each axis meant "split that way" -- and none of it was drawn
    /// until you were already over it. That is discoverable only by trial: you find out where a panel is
    /// going by nearly dropping it there. Buttons make the choice visible before the commitment, and give
    /// something to aim at rather than a proportion to estimate.
    /// </para>
    /// <para>
    /// The proportional rule stays as the fallback, so a confident drag that never touches a button still
    /// behaves exactly as it always did.
    /// </para>
    /// </summary>
    public static class DockDropIndicators
    {
        /// <summary>Button size. Large enough to be an easy target mid-drag without covering the pane.</summary>
        public const float Size = 32.0f;

        /// <summary>Clear space between the centre button and the four around it.</summary>
        public const float Gap = 5.0f;

        /// <summary>How far in from the layout's edge the root-edge buttons sit.</summary>
        public const float RootInset = 10.0f;

        /// <summary>
        /// A pane smaller than this gets only its centre button. The full cluster is Size*3 + Gap*2 across,
        /// and drawing it over a pane it does not fit in puts buttons outside the pane they refer to.
        /// </summary>
        public static float MinimumClusterSize => Size * 3.0f + Gap * 2.0f;

        /// <summary>The five buttons over one pane, or just the centre one when the pane is too small.</summary>
        public static List<DockDropIndicator> ForGroup(UiRect bounds)
        {
            var list = new List<DockDropIndicator>(5);
            if (bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            {
                return list;
            }

            var centre = bounds.Center;
            list.Add(new DockDropIndicator(DockSide.Center, BoxAt(centre), isRootEdge: false));

            if (bounds.Width < MinimumClusterSize || bounds.Height < MinimumClusterSize)
            {
                return list;
            }

            var step = Size + Gap;
            list.Add(new DockDropIndicator(DockSide.Left, BoxAt(new Vector2(centre.X - step, centre.Y)), false));
            list.Add(new DockDropIndicator(DockSide.Right, BoxAt(new Vector2(centre.X + step, centre.Y)), false));
            list.Add(new DockDropIndicator(DockSide.Top, BoxAt(new Vector2(centre.X, centre.Y - step)), false));
            list.Add(new DockDropIndicator(DockSide.Bottom, BoxAt(new Vector2(centre.X, centre.Y + step)), false));
            return list;
        }

        /// <summary>The four buttons against the layout's own edges, for docking down a whole side.</summary>
        public static List<DockDropIndicator> ForHost(UiRect bounds, UiRect groupBounds = default)
        {
            var list = new List<DockDropIndicator>(4);
            // With one pane filling the host, both sets split exactly the same area. Keep
            // the central cluster and omit the redundant outer choices. The resolver uses
            // this same list so hidden buttons cannot steal a drop.
            if (!groupBounds.IsEmpty
                && LayoutHelper.AreClose(bounds.Position, groupBounds.Position)
                && LayoutHelper.AreClose(bounds.Size, groupBounds.Size))
            {
                return list;
            }
            if (bounds.Width < Size * 4.0f || bounds.Height < Size * 4.0f)
            {
                return list;
            }

            var centre = bounds.Center;
            var inset = RootInset + Size * 0.5f;

            list.Add(new DockDropIndicator(DockSide.Left, BoxAt(new Vector2(bounds.Left + inset, centre.Y)), true));
            list.Add(new DockDropIndicator(DockSide.Right, BoxAt(new Vector2(bounds.Right - inset, centre.Y)), true));
            list.Add(new DockDropIndicator(DockSide.Top, BoxAt(new Vector2(centre.X, bounds.Top + inset)), true));
            list.Add(new DockDropIndicator(DockSide.Bottom, BoxAt(new Vector2(centre.X, bounds.Bottom - inset)), true));
            return list;
        }

        /// <summary>
        /// The button under a point, or null. The hit area is grown a little past the drawn box: these are
        /// aimed at while dragging something, which is a coarser gesture than clicking.
        /// </summary>
        public static DockDropIndicator? HitTest(IReadOnlyList<DockDropIndicator> indicators, Vector2 point)
        {
            for (var i = 0; i < indicators.Count; i++)
            {
                if (indicators[i].Box.Inflate(new Thickness(Gap * 0.5f)).Contains(point))
                {
                    return indicators[i];
                }
            }

            return null;
        }

        private static UiRect BoxAt(Vector2 centre) =>
            new UiRect(centre.X - Size * 0.5f, centre.Y - Size * 0.5f, Size, Size);
    }
}
