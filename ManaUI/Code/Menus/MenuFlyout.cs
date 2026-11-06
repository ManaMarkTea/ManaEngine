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
                Child = new Border { SkinName = "panel" }
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
                if (item.IsSeparator)
                {
                    list.Children.Add(BuildSeparator(theme));
                    continue;
                }

                // A hosted control keeps the menu open and handles its own input, so it is added as
                // itself rather than wrapped in a row that would swallow the click.
                if (item.HasContent)
                {
                    list.Children.Add(BuildContentRow(theme, item));
                    continue;
                }

                list.Children.Add(new MenuRow(root, popup, item, hasCheckColumn, hasSubmenus));
            }

            LimitHeight(root, popup, list);
            show(popup);
            return popup;
        }

        /// <summary>
        /// A menu taller than the window (a long list of examples) scrolls instead of running off the screen,
        /// where its last rows could not be clicked. Wrapped only when needed: a scroll viewer takes all the
        /// room it is offered, so wrapping every menu would stretch the short ones.
        /// </summary>
        private static void LimitHeight(UiRoot root, UiPopup popup, StackPanel list)
        {
            // Border outline and padding take 8, and a margin is left for the bar the menu hangs from.
            var limit = root.Size.Y - 60.0f;
            if (limit < 80.0f)
            {
                return;
            }

            list.Measure(new Vector2(LayoutHelper.Infinity, LayoutHelper.Infinity));
            if (list.DesiredSize.Y <= limit)
            {
                return;
            }

            var theme = root.Theme;
            var gutter = theme.ScrollBarThickness + theme.ScrollBarGap;
            if (popup.Child is not Border border)
            {
                return;
            }

            var scroller = new ScrollViewer
            {
                HorizontalScrollBar = ScrollBarVisibility.Disabled,
                WheelStep = 40.0f
            };

            border.Child = null;
            scroller.Content(list);
            scroller.Width = list.DesiredSize.X + gutter;
            scroller.Height = limit;
            border.Content(scroller);
        }

        /// <summary>
        /// A hosted control, indented to the same place a label starts so it lines up with the rows
        /// around it.
        /// </summary>
        private static UiElement BuildContentRow(UiTheme theme, MenuItem item)
        {
            var content = item.Content!;

            // A flyout is built fresh every time the menu opens, but the hosted control is one element
            // owned by the MenuItem -- it has to leave the popup that held it last time. Without this the
            // second open of a menu with a hosted control throws, which is exactly what happened the first
            // time anyone opened the View menu twice.
            content.RemoveFromParent();

            if (string.IsNullOrEmpty(item.Text))
            {
                return content.Margin(MenuRow.LabelIndent, 2.0f, 8.0f, 2.0f);
            }

            return new StackPanel()
                .Vertical(2.0f)
                .Margin(MenuRow.LabelIndent, 3.0f, 8.0f, 3.0f)
                .Children(
                    new TextBlock().Text(item.Text).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall),
                    content);
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
        /// <summary>Where a row's text starts, so hosted content can line up with it.</summary>
        internal const float LabelIndent = 26.0f;

        private const float CheckColumnWidth = 20.0f;
        private const float ArrowColumnWidth = 16.0f;
        private const float ShortcutGap = 24.0f;

        private readonly UiRoot _root;
        private readonly UiPopup _owner;
        private readonly MenuItem _item;
        private readonly bool _hasCheckColumn;
        private readonly bool _hasSubmenuColumn;

        private UiPopup? _submenu;
        private string _measuredText = string.Empty;

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
            Tooltip = item.Tooltip;
        }

        private float LeftInset => _hasCheckColumn ? CheckColumnWidth : 10.0f;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var root = Root ?? _root;
            var theme = root.Theme;
            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);

            _measuredText = _item.ResolveText();
            var width = LeftInset + font.MeasureWidth(_measuredText) + 12.0f;

            var shortcut = _item.ResolveShortcut();
            if (!string.IsNullOrEmpty(shortcut))
            {
                width += ShortcutGap + font.MeasureWidth(shortcut);
            }

            if (_hasSubmenuColumn)
            {
                width += ArrowColumnWidth;
            }

            // See MenuBarHeading's matching comment: a condensed display font's baked-in line metrics can
            // exceed theme.ControlHeight at the same nominal FontSize, so fall back to whichever is taller.
            var height = MathF.Max(theme.ControlHeight, font.LineHeight);
            return new Vector2(width, height);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var root = Root ?? _root;
            var theme = root.Theme;
            var enabled = _item.ResolveEnabled() && !_item.IsLabel;
            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);

            // A row stays highlighted while its own submenu is open, even though the pointer has moved off
            // it and into that submenu. Otherwise the open chain has nothing lit and the menu looks
            // inactive while it is being used.
            var highlighted = enabled && (IsPointerOver || IsSubmenuOpen);

            if (highlighted)
            {
                if (!drawList.DrawSkin(theme, "primary", Bounds, UiColor.White, lift: false))
                {
                    drawList.FillRect(Bounds, theme.Accent, new CornerRadius(3.0f));
                }
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
                    foreground,
                    font);
            }

            var text = _item.ResolveText();
            if (text != _measuredText)
            {
                // A live label that changed (TextFrom) may need a wider row.
                InvalidateMeasure();
            }

            var label = new TextLayout { Font = font, Text = text };
            label.Render(
                drawList,
                new UiRect(Bounds.X + LeftInset, Bounds.Y, Bounds.Width, Bounds.Height),
                foreground,
                VerticalAlignment.Center);

            var shortcutText = _item.ResolveShortcut();
            if (!string.IsNullOrEmpty(shortcutText))
            {
                var shortcut = new TextLayout
                {
                    Font = font,
                    Text = shortcutText,
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
                    foreground,
                    font);
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

            // A label is text, not a command: it must not highlight, close the menu, or do anything.
            // A parent row only opens its submenu; it has no action of its own.
            if (_item.IsLabel || _item.HasSubmenu || !_item.ResolveEnabled())
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;

            if (_item.StaysOpen)
            {
                _item.Invoke();
                InvalidateVisual();
                return;
            }

            // Closed before invoking, so an action that opens a dialog or another menu is not immediately
            // dismissed by the tail of its own click.
            _root.Popups.CloseAll();
            _item.Invoke();
        }
    }
}
