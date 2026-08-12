namespace ManaUI
{
    /// <summary>
    /// Absolute positioning. Children are measured unconstrained and placed at their Canvas offsets; setting
    /// both Left and Right (or Top and Bottom) stretches the child between them instead.
    /// <para>
    /// This is the substrate the floating-window and popup layers sit on.
    /// </para>
    /// </summary>
    public class Canvas : Panel
    {
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    child.Measure(LayoutHelper.InfiniteSize);
                }
            }

            // A Canvas never reports its children's size -- it takes whatever its own parent gives it.
            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var slot = child.LayoutSlotOrNull;
                var desired = child.DesiredSize;

                var left = slot?.CanvasLeft ?? float.NaN;
                var top = slot?.CanvasTop ?? float.NaN;
                var right = slot?.CanvasRight ?? float.NaN;
                var bottom = slot?.CanvasBottom ?? float.NaN;

                var (x, width) = ResolveAxis(content.Left, content.Width, left, right, desired.X);
                var (y, height) = ResolveAxis(content.Top, content.Height, top, bottom, desired.Y);

                child.Arrange(new UiRect(x, y, width, height));
            }

            return finalSize;
        }

        /// <summary>
        /// Resolves one axis. Both edges pinned stretches; one edge pins that side; neither anchors to the
        /// content origin.
        /// </summary>
        private static (float Offset, float Length) ResolveAxis(
            float contentOrigin,
            float contentLength,
            float nearEdge,
            float farEdge,
            float desiredLength)
        {
            var hasNear = !float.IsNaN(nearEdge);
            var hasFar = !float.IsNaN(farEdge);

            if (hasNear && hasFar)
            {
                return (contentOrigin + nearEdge, MathF.Max(0.0f, contentLength - nearEdge - farEdge));
            }

            if (hasFar)
            {
                return (contentOrigin + contentLength - farEdge - desiredLength, desiredLength);
            }

            return (contentOrigin + (hasNear ? nearEdge : 0.0f), desiredLength);
        }
    }
}
