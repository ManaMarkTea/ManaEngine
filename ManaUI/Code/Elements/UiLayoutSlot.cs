namespace ManaUI
{
    /// <summary>
    /// The stand-in for WPF attached properties: per-child hints that only the containing panel reads
    /// (Grid.Row, DockPanel.Dock, Canvas.Left...). Allocated lazily, so an element in a StackPanel never
    /// pays for it.
    /// </summary>
    public sealed class UiLayoutSlot
    {
        public int GridRow;
        public int GridColumn;
        public int GridRowSpan = 1;
        public int GridColumnSpan = 1;

        public Dock Dock = Dock.Left;

        public float CanvasLeft = float.NaN;
        public float CanvasTop = float.NaN;
        public float CanvasRight = float.NaN;
        public float CanvasBottom = float.NaN;

        /// <summary>Z-order within the parent. Higher draws later; ties keep declaration order.</summary>
        public int ZIndex;
    }

    public static class UiLayoutSlotExtensions
    {
        /// <summary>Creates the slot on demand and marks the parent's layout stale.</summary>
        public static UiLayoutSlot EnsureLayoutSlot(this UiElement element)
        {
            var slot = element.LayoutSlotOrNull;
            if (slot == null)
            {
                slot = new UiLayoutSlot();
                element.LayoutSlotOrNull = slot;
            }

            element.Parent?.InvalidateMeasure();
            return slot;
        }
    }
}
