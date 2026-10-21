namespace ManaUI
{
    /// <summary>
    /// Slots in rows of a fixed width, filled left to right. Cells with no slot are still drawn as empty wells, up to
    /// <see cref="MinimumRows"/>, so an inventory reads as a grid with room left in it rather than a short list.
    /// </summary>
    public class SlotGrid : Panel
    {
        private int _columns = 10;
        private float _slotSize = 40;
        private float _spacing = 2;
        private int _minimumRows;
        private SlotStyle _style = SlotStyle.Industrial;

        public int Columns
        {
            get => _columns;
            set => SetLayout(ref _columns, Math.Max(1, value));
        }

        /// <summary>The side of every slot; slots added to the grid are sized to it.</summary>
        public float SlotSize
        {
            get => _slotSize;
            set => SetLayout(ref _slotSize, value);
        }

        public float Spacing
        {
            get => _spacing;
            set => SetLayout(ref _spacing, value);
        }

        public int MinimumRows
        {
            get => _minimumRows;
            set => SetLayout(ref _minimumRows, Math.Max(0, value));
        }

        public SlotStyle Style
        {
            get => _style;
            set => SetVisualProperty(ref _style, value);
        }

        /// <summary>Rows shown: enough for every visible slot, and at least <see cref="MinimumRows"/>.</summary>
        public int Rows => Math.Max(_minimumRows, (Shown().Count() + _columns - 1) / _columns);

        public ItemSlot Add(UiElement? icon = null)
        {
            var slot = new ItemSlot { Icon = icon, Size = _slotSize, Style = _style };
            Children.Add(slot);
            return slot;
        }

        /// <summary>The cell rectangle of row and column, in the grid's own space.</summary>
        public UiRect CellRect(int row, int column) =>
            new(Bounds.X + Padding.Left + column * (_slotSize + _spacing), Bounds.Y + Padding.Top + row * (_slotSize + _spacing), _slotSize, _slotSize);

        private IEnumerable<UiElement> Shown() => Children.Where(c => c.Visibility != Visibility.Collapsed);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var child in Shown())
            {
                if (child is ItemSlot slot)
                {
                    slot.Size = _slotSize;
                }
                child.Measure(new Vector2(_slotSize, _slotSize));
            }
            var rows = Rows;
            return new Vector2(
                Padding.Left + Padding.Right + _columns * _slotSize + (_columns - 1) * _spacing,
                Padding.Top + Padding.Bottom + rows * _slotSize + Math.Max(0, rows - 1) * _spacing);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var index = 0;
            foreach (var child in Shown())
            {
                child.Arrange(CellRect(index / _columns, index % _columns));
                index++;
            }
            return finalSize;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            base.OnRender(drawList);
            // Empty wells for the cells no slot fills.
            var filled = Shown().Count();
            for (var cell = filled; cell < Rows * _columns; cell++)
            {
                _style.DrawWell(drawList, CellRect(cell / _columns, cell % _columns), _style.Slot);
            }
        }

        private void SetLayout<T>(ref T field, T value)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// A quick bar: rows of slots, each led by its row number, one row active at a time -- the strip of tools and
    /// buildings along the foot of a factory game's screen.
    /// </summary>
    public class Hotbar : StackPanel
    {
        private readonly List<(ItemSlot Number, SlotGrid Slots)> _rows = new();
        private int _activeRow;

        public Hotbar(int rows, int columns, float slotSize = 40)
        {
            Orientation = Orientation.Vertical;
            Spacing = 4;
            for (var r = 0; r < rows; r++)
            {
                var row = r;
                var number = new ItemSlot { Size = slotSize, CountText = null };
                number.Icon = new TextBlock().Text((r + 1).ToString()).FontSize(slotSize * 0.4f).CenterH().CenterV();
                number.Clicked += (_, _) => ActiveRow = row;
                var slots = new SlotGrid { Columns = columns, SlotSize = slotSize, MinimumRows = 1 };
                for (var c = 0; c < columns; c++)
                {
                    slots.Add();
                }
                Children.Add(new StackPanel().Horizontal(8).Children(number, slots));
                _rows.Add((number, slots));
            }
            ActiveRow = 0;
        }

        public event Action<int>? ActiveRowChanged;

        public int RowCount => _rows.Count;

        public int ColumnCount => _rows.Count > 0 ? _rows[0].Slots.Columns : 0;

        public ItemSlot this[int row, int column] => (ItemSlot)_rows[row].Slots.Children[column];

        /// <summary>The numbered button at the start of a row, for a game that gives it another job (a page switch).</summary>
        public ItemSlot RowButton(int row) => _rows[row].Number;

        public int ActiveRow
        {
            get => _activeRow;
            set
            {
                value = Math.Clamp(value, 0, Math.Max(0, _rows.Count - 1));
                var changed = value != _activeRow;
                _activeRow = value;
                for (var r = 0; r < _rows.Count; r++)
                {
                    _rows[r].Number.IsSelected = r == value;
                }
                if (changed)
                {
                    ActiveRowChanged?.Invoke(value);
                }
            }
        }
    }

    /// <summary>
    /// A titled panel in the slot style: a raised frame with its title and a ridged grip on the bar, and the content
    /// sitting in a darker inset well (an inventory, a crafting list).
    /// </summary>
    public class PanelFrame : Decorator
    {
        private string _title = string.Empty;
        private SlotStyle _style = SlotStyle.Industrial;

        public PanelFrame()
        {
            Padding = PaddingFor(_title);
        }

        protected override bool IsSelfHitTestVisible => true;

        /// <summary>The bar's text; with none the frame is a plain border (a hotbar's).</summary>
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    Padding = PaddingFor(value);
                    InvalidateMeasure();
                }
            }
        }

        private float TitleBar => _title.Length > 0 ? 30 : 4;

        private static Thickness PaddingFor(string title) => new(10, (title.Length > 0 ? 30 : 4) + 6, 10, 10);

        public SlotStyle Style
        {
            get => _style;
            set => SetVisualProperty(ref _style, value);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            drawList.DrawShadow(Bounds, new Vector4(0, 0, 0, 0.5f), 10, new CornerRadius(3));
            drawList.FillRect(Bounds, _style.FrameEdge, new CornerRadius(3));
            drawList.FillRect(Bounds.Deflate(new Thickness(2)), _style.Frame, new CornerRadius(2));
            var titleWidth = 0f;
            if (Root is { } root && _title.Length > 0)
            {
                titleWidth = SlotStyle.DrawText(drawList, root, _title, new Vector2(Bounds.X + 12, Bounds.Y + 6), _style.TitleSize, _style.Title);
            }
            // The grip: ridges from the end of the title to the edge, as on a window you can drag.
            var gripLeft = Bounds.X + 12 + titleWidth + (titleWidth > 0 ? 10 : 0);
            var gripWidth = Bounds.Right - 10 - gripLeft;
            if (gripWidth > 8 && _title.Length > 0)
            {
                for (var i = 0; i < 4; i++)
                {
                    var y = Bounds.Y + 10 + i * 4;
                    drawList.FillRect(new UiRect(gripLeft, y, gripWidth, 1), _style.Groove);
                    drawList.FillRect(new UiRect(gripLeft, y + 1, gripWidth, 1), _style.SlotLight.WithAlpha(0.35f));
                }
            }
            var inset = new UiRect(Bounds.X + 8, Bounds.Y + TitleBar + 2, Bounds.Width - 16, Bounds.Height - TitleBar - 10);
            drawList.FillRect(inset, _style.SlotShade);
            drawList.FillRect(new UiRect(inset.X + 1, inset.Y + 1, inset.Width - 1, inset.Height - 1), _style.Inset);
        }
    }
}
