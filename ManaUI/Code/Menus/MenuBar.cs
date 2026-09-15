namespace ManaUI
{
    /// <summary>
    /// A horizontal bar of top-level menus, as an application's main menu.
    /// <para>
    /// Once one menu is open, hovering another heading switches to it without a second click -- the
    /// behaviour every desktop menu bar has, and the reason the bar tracks which heading is open rather
    /// than each heading acting alone.
    /// </para>
    /// </summary>
    public class MenuBar : Panel
    {
        private readonly List<MenuItem> _menus = new();
        private MenuBarHeading? _openHeading;

        public MenuBar()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;
        }

        public IReadOnlyList<MenuItem> Menus => _menus;

        public bool IsAnyOpen => _openHeading != null;

        public MenuBar Add(MenuItem menu)
        {
            ArgumentNullException.ThrowIfNull(menu);

            _menus.Add(menu);
            Children.Add(new MenuBarHeading(this, menu));
            return this;
        }

        public MenuBar AddRange(params MenuItem[] menus)
        {
            foreach (var menu in menus)
            {
                Add(menu);
            }

            return this;
        }

        internal void OpenMenu(MenuBarHeading heading)
        {
            var root = Root;
            if (root == null || ReferenceEquals(_openHeading, heading))
            {
                return;
            }

            root.Popups.CloseAll();

            _openHeading = heading;

            var popup = MenuFlyout.ShowBelow(root, heading, heading.Menu.Items);
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_openHeading, heading))
                {
                    _openHeading = null;
                    InvalidateVisual();
                }
            };

            InvalidateVisual();
        }

        internal void CloseMenu()
        {
            Root?.Popups.CloseAll();
            _openHeading = null;
            InvalidateVisual();
        }

        internal bool IsOpen(MenuBarHeading heading) => ReferenceEquals(_openHeading, heading);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var height = 0.0f;
            var width = 0.0f;

            foreach (var child in Children)
            {
                child.Measure(new Vector2(LayoutHelper.Infinity, availableSize.Y));
                width += child.DesiredSize.X;
                height = MathF.Max(height, child.DesiredSize.Y);
            }

            return new Vector2(
                float.IsInfinity(availableSize.X) ? width : availableSize.X,
                MathF.Max(height, Root?.Theme.ControlHeight ?? 26.0f));
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var offset = 0.0f;

            foreach (var child in Children)
            {
                var width = child.DesiredSize.X;
                child.Arrange(new UiRect(Bounds.X + offset, Bounds.Y, width, finalSize.Y));
                offset += width;
            }

            return finalSize;
        }
    }

    /// <summary>One top-level heading in a menu bar.</summary>
    public sealed class MenuBarHeading : Control
    {
        private readonly MenuBar _bar;

        public MenuBarHeading(MenuBar bar, MenuItem menu)
        {
            _bar = bar;
            Menu = menu;
            Focusable = false;
        }

        public MenuItem Menu { get; }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var root = Root;
            if (root == null)
            {
                return new Vector2(60.0f, 26.0f);
            }

            var theme = root.Theme;
            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);

            // theme.ControlHeight alone assumes every font's glyphs fit inside it at the theme's own
            // FontSize -- true for the default typeface, but a condensed display face like LCARS's Antonio
            // can carry taller baked-in ascent/descent/line-gap metrics at the same nominal size, and the
            // heading would clip or crowd the text against a flat ControlHeight. Falling back to whichever
            // is taller keeps the bar exactly as compact as before for themes where they already agree.
            var height = MathF.Max(theme.ControlHeight, font.LineHeight);
            return new Vector2(font.MeasureWidth(Menu.Text) + 20.0f, height);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var root = Root;
            if (root == null)
            {
                return;
            }

            var theme = root.Theme;
            var open = _bar.IsOpen(this);

            if (open)
            {
                drawList.FillRect(Bounds, theme.Accent, CornerRadius.Top(3.0f));
            }
            else if (IsPointerOver)
            {
                drawList.FillRect(Bounds, theme.Hover, CornerRadius.Top(3.0f));
            }

            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);
            var layout = new TextLayout { Font = font, Text = Menu.Text, Alignment = TextAlignment.Center };

            layout.Render(drawList, Bounds, open ? theme.TextOnAccent : theme.Text, VerticalAlignment.Center);
        }

        protected override void OnPointerEntered(UiPointerEventArgs e)
        {
            base.OnPointerEntered(e);

            // Only track the pointer once a menu is already open; otherwise merely crossing the bar would
            // start opening menus.
            if (_bar.IsAnyOpen)
            {
                _bar.OpenMenu(this);
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            if (_bar.IsOpen(this))
            {
                _bar.CloseMenu();
            }
            else
            {
                _bar.OpenMenu(this);
            }

            e.Handled = true;
        }
    }
}
