namespace ManaUI
{
    /// <summary>
    /// Builds and opens the popup for a list of <see cref="MenuItem"/>, including submenus.
    /// </summary>
    public static class MenuFlyout
    {
        /// <summary>Opens a menu anchored under an element -- a menu bar heading.</summary>
        public static UiPopup ShowBelow(UiRoot root, UiElement anchor, IReadOnlyList<MenuItem> items)
        {
            return Open(root, items, popup =>
            {
                popup.Placement = PopupPlacement.Bottom;
                root.Popups.Show(popup, anchor);
            });
        }

        /// <summary>Opens a menu at a point -- a context menu.</summary>
        public static UiPopup ShowAt(UiRoot root, Vector2 point, IReadOnlyList<MenuItem> items)
        {
            return Open(root, items, popup => root.Popups.ShowAt(popup, point));
        }

        /// <summary>Opens a submenu beside a parent row.</summary>
        internal static UiPopup ShowSubmenu(UiRoot root, UiElement anchor, IReadOnlyList<MenuItem> items)
        {
            return Open(root, items, popup =>
            {
                popup.Placement = PopupPlacement.Right;
                root.Popups.Show(popup, anchor);
            });
        }

        private static UiPopup Open(UiRoot root, IReadOnlyList<MenuItem> items, Action<UiPopup> show)
        {
            var theme = root.Theme;
            var list = new StackPanel().Vertical();

            var popup = new UiPopup
            {
                Child = new Border()
                    .Fill(theme.SurfaceRaised)
                    .Outline(theme.BorderStrong, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Shadow(16.0f)
                    .PaddingOf(3.0f)
                    .Content(list)
            };

            // Reserved only when something in this menu is checkable, so a menu of plain commands is not
            // left with a permanently empty column.
            var hasCheckColumn = items.Any(item => item.IsCheckable);
            var hasSubmenus = items.Any(item => item.HasSubmenu);

            foreach (var item in items)
            {
                list.Children.Add(item.IsSeparator
                    ? BuildSeparator(theme)
                    : new MenuRow(root, popup, item, hasCheckColumn, hasSubmenus));
            }

            show(popup);
            return popup;
        }

        private static UiElement BuildSeparator(UiTheme theme)
        {
            return new Border()
                .Fill(theme.Divider)
                .Height(1.0f)
                .Margin(6.0f, 4.0f);
        }
    }

    /// <summary>
    /// A single menu row: check column, label, shortcut, submenu arrow.
    /// <para>
    /// Drawn directly rather than composed from child elements. A row is four pieces of text and a tick in
    /// fixed columns, and a menu can have a lot of rows -- building each one out of nested panels would
    /// cost far more than it explains.
    /// </para>
    /// </summary>
    internal sealed class MenuRow : Control
    {
        private const float CheckColumnWidth = 20.0f;
        private const float ArrowColumnWidth = 16.0f;
        private const float ShortcutGap = 24.0f;

        private readonly UiRoot _root;
        private readonly UiPopup _owner;
        private readonly MenuItem _item;
        private readonly bool _hasCheckColumn;
        private readonly bool _hasSubmenuColumn;

        private UiPopup? _submenu;

        public MenuRow(UiRoot root, UiPopup owner, MenuItem item, bool hasCheckColumn, bool hasSubmenuColumn)
        {
            _root = root;
            _owner = owner;
            _item = item;
            _hasCheckColumn = hasCheckColumn;
            _hasSubmenuColumn = hasSubmenuColumn;

            Focusable = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            IsEnabled = item.ResolveEnabled();
        }

        private float LeftInset => _hasCheckColumn ? CheckColumnWidth : 10.0f;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var root = Root ?? _root;
            var theme = root.Theme;
            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);

            var width = LeftInset + font.MeasureWidth(_item.Text) + 12.0f;

            if (!string.IsNullOrEmpty(_item.Shortcut))
            {
                width += ShortcutGap + font.MeasureWidth(_item.Shortcut);
            }

            if (_hasSubmenuColumn)
            {
                width += ArrowColumnWidth;
            }

            return new Vector2(width, theme.ControlHeight);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var root = Root ?? _root;
            var theme = root.Theme;
            var enabled = _item.ResolveEnabled();
            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);

            // A row stays highlighted while its own submenu is open, even though the pointer has moved off
            // it and into that submenu. Otherwise the open chain has nothing lit and the menu looks
            // inactive while it is being used.
            var highlighted = enabled && (IsPointerOver || IsSubmenuOpen);

            if (highlighted)
            {
                drawList.FillRect(Bounds, theme.Accent, new CornerRadius(3.0f));
            }

            var foreground = !enabled
                ? theme.TextDisabled
                : highlighted ? theme.TextOnAccent : theme.Text;

            if (_hasCheckColumn && _item.ResolveChecked())
            {
                // Drawn rather than U+2713, which Roboto does not have -- it rendered as a tofu box.
                var tickSize = 10.0f;
                UiIcons.Check(
                    drawList,
                    new UiRect(
                        Bounds.X + (CheckColumnWidth - tickSize) * 0.5f,
                        Bounds.Center.Y - tickSize * 0.5f,
                        tickSize,
                        tickSize),
                    foreground);
            }

            var label = new TextLayout { Font = font, Text = _item.Text };
            label.Render(
                drawList,
                new UiRect(Bounds.X + LeftInset, Bounds.Y, Bounds.Width, Bounds.Height),
                foreground,
                VerticalAlignment.Center);

            if (!string.IsNullOrEmpty(_item.Shortcut))
            {
                var shortcut = new TextLayout
                {
                    Font = font,
                    Text = _item.Shortcut,
                    Alignment = TextAlignment.Right
                };

                var right = Bounds.Right - (_hasSubmenuColumn ? ArrowColumnWidth : 8.0f);
                shortcut.Render(
                    drawList,
                    new UiRect(Bounds.X, Bounds.Y, right - Bounds.X, Bounds.Height),
                    highlighted ? theme.TextOnAccent : theme.TextMuted,
                    VerticalAlignment.Center);
            }

            if (_item.HasSubmenu)
            {
                UiIcons.Triangle(
                    drawList,
                    new UiRect(
                        Bounds.Right - ArrowColumnWidth + 2.0f,
                        Bounds.Center.Y - 4.0f,
                        6.0f,
                        8.0f),
                    UiIconDirection.Right,
                    foreground);
            }
        }

        protected override void OnPointerEntered(UiPointerEventArgs e)
        {
            base.OnPointerEntered(e);

            // Entering any row cancels a pending submenu close. For a row inside the submenu this is the
            // whole point -- the pointer arrived, so the close that was scheduled when it crossed a
            // sibling on the way must not fire.
            _root.Popups.CancelPendingClose();

            if (_item.HasSubmenu && _item.ResolveEnabled())
            {
                // Opening a different submenu replaces the current one immediately; there is nothing to
                // travel towards any more.
                _root.Popups.CloseDeeperThan(_owner);
                _submenu = MenuFlyout.ShowSubmenu(_root, this, _item.Items);
                _submenu.Closed += OnSubmenuClosed;
                InvalidateVisual();
                return;
            }

            // A plain row only schedules the close, because the pointer may simply be passing over it on
            // its way to the submenu opened by a row above.
            _root.Popups.RequestCloseDeeperThan(_owner);
        }

        private void OnSubmenuClosed(object? sender, EventArgs e)
        {
            if (sender is UiPopup popup)
            {
                popup.Closed -= OnSubmenuClosed;
            }

            _submenu = null;
            InvalidateVisual();
        }

        /// <summary>True while this row's own submenu is open.</summary>
        private bool IsSubmenuOpen => _submenu is { IsOpen: true };

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            // A parent row only opens its submenu; it has no action of its own.
            if (_item.HasSubmenu || !_item.ResolveEnabled())
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;

            // Closed before invoking, so an action that opens a dialog or another menu is not immediately
            // dismissed by the tail of its own click.
            _root.Popups.CloseAll();
            _item.Invoke();
        }
    }
}
