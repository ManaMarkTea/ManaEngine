namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Keeps a borderless panel window where it can be grabbed: its tab strip (the top of the window, its only handle)
    /// on a monitor. Dropped half off a screen, left on one that was unplugged, or thrown past an edge in a drag, a panel
    /// window was lost until found again through the taskbar's Move. A window partly off a screen, strip still showing,
    /// is left where it was put; one out of reach comes all the way onto the nearest monitor.
    /// </summary>
    public static class ManaWindowPlacement
    {
        /// <summary>How much of the tab strip must be on a monitor to count as reachable, in pixels.</summary>
        public const int GripWidth = 120;
        public const int GripHeight = 32;

        /// <summary>
        /// Where a window at <paramref name="position"/> of <paramref name="size"/> should be so its tab strip is on a
        /// monitor, or null when it already is. A window out of reach comes wholly onto the nearest monitor, as little moved
        /// as that allows.
        /// </summary>
        public static (int X, int Y)? Reachable(
            (int X, int Y) position, (int Width, int Height) size, IReadOnlyList<(int X, int Y, int Width, int Height)> monitors)
        {
            if (monitors.Count == 0)
            {
                return null;
            }

            var grip = Math.Min(GripWidth, Math.Max(1, size.Width));
            foreach (var monitor in monitors)
            {
                var overlap = Math.Min(position.X + size.Width, monitor.X + monitor.Width) - Math.Max(position.X, monitor.X);
                var stripInside = position.Y >= monitor.Y && position.Y + GripHeight <= monitor.Y + monitor.Height;
                if (overlap >= grip && stripInside)
                {
                    return null;
                }
            }

            // The monitor nearest the window's top middle, and the least move that brings the strip onto it.
            var anchorX = position.X + size.Width / 2.0;
            var anchorY = position.Y + GripHeight / 2.0;
            var nearest = monitors.MinBy(monitor =>
            {
                var dx = Math.Max(Math.Max(monitor.X - anchorX, 0), anchorX - (monitor.X + monitor.Width));
                var dy = Math.Max(Math.Max(monitor.Y - anchorY, 0), anchorY - (monitor.Y + monitor.Height));
                return dx * dx + dy * dy;
            });
            // Rescued, it comes all the way on (as far as it fits): a strip barely showing at an edge is easy to lose again.
            var x = Math.Clamp(position.X, nearest.X, Math.Max(nearest.X, nearest.X + nearest.Width - size.Width));
            var y = Math.Clamp(position.Y, nearest.Y, Math.Max(nearest.Y, nearest.Y + nearest.Height - size.Height));
            return (x, y);
        }
    }
}
