namespace ManaUI
{
    /// <summary>
    /// Turns a tab pulled off a strip into a floating window, and a floating window dropped over the dock
    /// host back into a docked panel.
    /// <para>
    /// This is deliberately the only piece that knows about both docking and floating windows. The nodes
    /// report that a tab left the strip and the window reports that it moved; neither knows what the other
    /// is, which keeps a tear-out from being wired into the dock tree itself.
    /// </para>
    /// </summary>
    public sealed class DockDragController
    {
        private readonly UiRoot _root;
        private readonly DockHost _host;
        private readonly DockDropOverlay _overlay = new();

        private DockItem? _draggingItem;
        private UiWindow? _floatingWindow;
        private DockDropTarget _target;

        public DockDragController(UiRoot root, DockHost host)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(host);

            _root = root;
            _host = host;

            _root.OverlayLayer.Children.Add(_overlay);
            _overlay.Visibility = Visibility.Collapsed;

            AttachTo(host.RootNode);
            host.LayoutChanged += (_, _) => AttachTo(host.RootNode);
        }

        /// <summary>Size a torn-out panel becomes when it starts floating.</summary>
        public Vector2 FloatingSize { get; set; } = new Vector2(360.0f, 280.0f);

        public bool IsDragging => _draggingItem != null;

        /// <summary>Subscribes to every group in the tree so a tear-out from any of them is caught.</summary>
        private void AttachTo(UiElement? node)
        {
            if (node == null)
            {
                return;
            }

            if (node is DockTabGroup group)
            {
                group.TabDragOut -= OnTabDragOut;
                group.TabDragOut += OnTabDragOut;
                return;
            }

            var count = node.VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                AttachTo(node.GetVisualChild(i));
            }
        }

        private void OnTabDragOut(object? sender, DockTabDragEventArgs e)
        {
            BeginFloat(e.Item, e.Position);
        }

        /// <summary>
        /// Pulls an item out of the dock tree into a floating window that continues the drag already under
        /// way, so the gesture is unbroken from the user's point of view.
        /// </summary>
        public UiWindow BeginFloat(DockItem item, Vector2 pointer)
        {
            ArgumentNullException.ThrowIfNull(item);

            item.Owner?.Remove(item);
            _host.Prune();

            var window = new UiWindow(item.Title)
            {
                Width = FloatingSize.X,
                Height = FloatingSize.Y,
                Child = item.Content
            };

            // Positioned so the title bar sits under the cursor, which is where the user grabbed it.
            _root.ShowWindow(window, pointer - new Vector2(FloatingSize.X * 0.5f, 12.0f));

            _draggingItem = item;
            _floatingWindow = window;

            window.Dragging += OnWindowDragging;
            window.DragEnded += OnWindowDragEnded;
            window.Closed += OnWindowClosed;

            _root.UpdateLayout(_root.Size);
            window.BeginExternalDrag(pointer);
            return window;
        }

        private void OnWindowDragging(object? sender, Vector2 pointer)
        {
            if (_draggingItem == null)
            {
                return;
            }

            _target = DockDropResolver.Resolve(_host, pointer);
            _overlay.Show(_target);
        }

        private void OnWindowDragEnded(object? sender, Vector2 pointer)
        {
            if (_draggingItem == null || _floatingWindow == null)
            {
                return;
            }

            var target = DockDropResolver.Resolve(_host, pointer);
            var item = _draggingItem;
            var window = _floatingWindow;

            EndDrag();

            if (!target.IsValid)
            {
                // Dropped somewhere that is not the dock host, so it simply stays floating.
                return;
            }

            // Detached before docking: the content is about to be re-parented into the tree, and the
            // window must not still be holding it.
            window.Child = null;
            window.Closed -= OnWindowClosed;
            window.Close();

            _host.Dock(item, target.Side, target.Group);
            _root.UpdateLayout(_root.Size);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _floatingWindow))
            {
                EndDrag();
            }
        }

        private void EndDrag()
        {
            if (_floatingWindow != null)
            {
                _floatingWindow.Dragging -= OnWindowDragging;
                _floatingWindow.DragEnded -= OnWindowDragEnded;
                _floatingWindow.Closed -= OnWindowClosed;
            }

            _draggingItem = null;
            _floatingWindow = null;
            _target = default;
            _overlay.Hide();
        }

        /// <summary>The drop target as of the last drag update, for tests and diagnostics.</summary>
        public DockDropTarget CurrentTarget => _target;
    }

    /// <summary>
    /// Draws the drop preview. Lives in the root's overlay layer so it sits above both the docked content
    /// and the floating window being dragged.
    /// </summary>
    public sealed class DockDropOverlay : UiElement
    {
        private DockDropTarget _target;

        public DockDropOverlay()
        {
            IsHitTestVisible = false;
        }

        public void Show(DockDropTarget target)
        {
            _target = target;
            Visibility = target.IsValid ? Visibility.Visible : Visibility.Collapsed;
            InvalidateVisual();
        }

        public void Hide()
        {
            _target = default;
            Visibility = Visibility.Collapsed;
            InvalidateVisual();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (!_target.IsValid)
            {
                return;
            }

            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            drawList.FillRect(_target.Preview, theme.Accent.WithAlpha(0.25f), new CornerRadius(4.0f));
            drawList.DrawBorder(_target.Preview, theme.Accent, 2.0f, new CornerRadius(4.0f));
        }

        /// <summary>
        /// The overlay is positioned by its own draw call rather than by layout, so it reports zero size
        /// and paints in absolute coordinates. Rendering is skipped for empty bounds, so it opts out of
        /// that check by claiming the whole root.
        /// </summary>
        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            return Root?.Size ?? finalSize;
        }
    }
}
