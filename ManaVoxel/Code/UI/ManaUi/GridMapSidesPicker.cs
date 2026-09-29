using ManaUI;
using ManaVoxel.Code.GridMaps;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// Which sides of a cell an auto-tile piece joins on, seen from above with -Z up: a square with an arm per
    /// side, lit when the piece joins there. Clicking near an arm toggles it.
    /// </summary>
    public sealed class GridMapSidesPicker : Control
    {
        private const float Size = 26.0f;

        private GridMapSides _sides;

        public GridMapSides Sides
        {
            get => _sides;
            set => SetVisualProperty(ref _sides, value & GridMapSides.All);
        }

        public event Action<GridMapSides>? SidesChanged;

        protected override Vector2 MeasureOverride(Vector2 availableSize) => new(Size, Size);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var box = new UiRect(Bounds.X, Bounds.Y, Size, Size);
            drawList.FillRect(box, theme.SurfaceAlt, new CornerRadius(3.0f));
            drawList.DrawBorder(box, theme.Border, 1.0f, new CornerRadius(3.0f));

            var centre = new Vector2(box.X + Size * 0.5f, box.Y + Size * 0.5f);
            var hub = Size * 0.16f;
            var on = IsEffectivelyEnabled ? theme.Accent : theme.TextMuted;
            foreach (var (side, x, z) in GridMapAutoTile.Neighbours)
            {
                var joins = _sides.HasFlag(side);
                var end = centre + new Vector2(x, z) * (Size * 0.5f - 2.0f);
                drawList.DrawLine(centre, end, joins ? on : theme.Border, joins ? 3.0f : 1.5f);
            }

            drawList.FillRect(new UiRect(centre.X - hub, centre.Y - hub, hub * 2.0f, hub * 2.0f), _sides == GridMapSides.None ? theme.TextMuted : on, new CornerRadius(1.5f));
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            // The side whose arm the click is nearest: by the larger of the two offsets from the centre.
            var offset = e.GetPosition(this) - new Vector2(Size * 0.5f);
            var side = MathF.Abs(offset.X) >= MathF.Abs(offset.Y)
                ? (offset.X >= 0.0f ? GridMapSides.East : GridMapSides.West)
                : (offset.Y >= 0.0f ? GridMapSides.South : GridMapSides.North);
            Sides = _sides ^ side;
            SidesChanged?.Invoke(_sides);
            e.Handled = true;
        }
    }
}
