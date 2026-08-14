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
        private readonly List<DockHost> _hosts = new();

        private readonly DockDropOverlay _overlay = new();

        /// <summary>
        /// Every floating window that came out of the dock tree, with the item it carries.
        /// <para>
        /// Registration lasts for the window's lifetime, not for one gesture. Tying it to the tear-out
        /// drag meant a window could only ever be docked during the single motion that created it --
        /// letting go and dragging it back later did nothing, because the handlers had already been
        /// removed.
        /// </para>
        /// </summary>
        private readonly Dictionary<UiWindow, DockItem> _floating = new();

        private UiWindow? _activeWindow;
        private DockDropTarget _target;

        public DockDragController(UiRoot root, DockHost host)
            : this(root, new[] { host })
        {
        }

        /// <summary>
        /// One controller over several hosts, which is what lets a panel be dragged from one column into
        /// another. A controller per host cannot do it: each only resolves drops inside its own tree, so a
        /// tab torn out of the left column had nowhere to land but the left column.
        /// </summary>
        public DockDragController(UiRoot root, IReadOnlyList<DockHost> hosts)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(hosts);

            if (hosts.Count == 0)
            {
                throw new ArgumentException("A dock drag controller needs at least one host.", nameof(hosts));
            }

            _root = root;
            _hosts.AddRange(hosts);

            _root.OverlayLayer.Children.Add(_overlay);
            _overlay.Visibility = Visibility.Collapsed;

            foreach (var host in _hosts)
            {
                AttachTo(host.RootNode);

                var owner = host;
                owner.LayoutChanged += (_, _) => AttachTo(owner.RootNode);
            }
        }

        /// <summary>
        /// The first host that claims the pointer. Hosts do not overlap, so at most one can, and a pointer
        /// over none of them means the panel stays floating.
        /// </summary>
        private (DockHost? Host, DockDropTarget Target) ResolveAcrossHosts(Vector2 pointer)
        {
            foreach (var host in _hosts)
            {
                var candidate = DockDropResolver.Resolve(host, pointer);
                if (candidate.IsValid)
                {
                    return (host, candidate);
                }
            }

            return (null, default);
        }

        /// <summary>Size a torn-out panel becomes when it starts floating.</summary>
        public Vector2 FloatingSize { get; set; } = new Vector2(360.0f, 280.0f);

        /// <summary>True while a dockable floating window is being dragged.</summary>
        public bool IsDragging => _activeWindow != null;

        /// <summary>Floating windows that can be dropped back into the layout.</summary>
        public IReadOnlyCollection<UiWindow> FloatingWindows => _floating.Keys;

        /// <summary>
        /// The panels currently floating rather than docked. A caller asking whether a panel is open has to
        /// count these, or a panel torn out of the dock reads as closed -- and opening it again makes a
        /// second copy of something already on screen.
        /// </summary>
        public IEnumerable<DockItem> FloatingItems => _floating.Values;

        /// <summary>The panel a floating window holds, or null when the window is not one of ours.</summary>
        public DockItem? ItemFor(UiWindow window) =>
            _floating.TryGetValue(window, out var item) ? item : null;

        /// <summary>Raised when a panel is floated, docked, moved or closed, so a layout can be saved.</summary>
        public event EventHandler? FloatingChanged;

        /// <summary>The window holding a floating panel, or null when it is docked or not open at all.</summary>
        public UiWindow? FindFloating(string id)
        {
            foreach (var pair in _floating)
            {
                if (pair.Value.Id == id)
                {
                    return pair.Key;
                }
            }

            return null;
        }

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

            // Every host, because the item could have come out of any of them.
            foreach (var host in _hosts)
            {
                host.Prune();
            }

            var window = new UiWindow(item.Title)
            {
                Width = FloatingSize.X,
                Height = FloatingSize.Y,
                Child = item.Content
            };

            // Positioned so the title bar sits under the cursor, which is where the user grabbed it.
            _root.ShowWindow(window, pointer - new Vector2(FloatingSize.X * 0.5f, 12.0f));

            RegisterFloating(window, item);

            _root.UpdateLayout(_root.Size);
            window.BeginExternalDrag(pointer);
            return window;
        }

        /// <summary>
        /// Marks a floating window as dockable, so dragging it over the host docks it. Applies for as long
        /// as the window exists, however many separate drags that takes.
        /// </summary>
        public void RegisterFloating(UiWindow window, DockItem item)
        {
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(item);

            if (_floating.ContainsKey(window))
            {
                return;
            }

            _floating[window] = item;
            FloatingChanged?.Invoke(this, EventArgs.Empty);

            // DragStarted as well as Dragging: a drag is under way from the moment the title bar is
            // grabbed, not from the first movement, and a tear-out is already mid-gesture when it arrives.
            window.DragStarted += OnWindowDragStarted;
            window.Dragging += OnWindowDragging;
            window.DragEnded += OnWindowDragEnded;
            window.Closed += OnWindowClosed;
        }

        private void Unregister(UiWindow window)
        {
            if (!_floating.Remove(window))
            {
                return;
            }

            window.DragStarted -= OnWindowDragStarted;
            window.Dragging -= OnWindowDragging;
            window.DragEnded -= OnWindowDragEnded;
            window.Closed -= OnWindowClosed;
            FloatingChanged?.Invoke(this, EventArgs.Empty);

            if (ReferenceEquals(_activeWindow, window))
            {
                ClearActiveDrag();
            }
        }

        private void OnWindowDragStarted(object? sender, Vector2 pointer)
        {
            if (sender is UiWindow window && _floating.ContainsKey(window))
            {
                _activeWindow = window;
            }
        }

        private void OnWindowDragging(object? sender, Vector2 pointer)
        {
            if (sender is not UiWindow window || !_floating.ContainsKey(window))
            {
                return;
            }

            _activeWindow = window;

            // The host is resolved again on drop rather than remembered: the pointer can leave one column
            // and enter another between the last move and the release.
            _target = ResolveAcrossHosts(pointer).Target;
            _overlay.Show(_target);
        }

        private void OnWindowDragEnded(object? sender, Vector2 pointer)
        {
            if (sender is not UiWindow window || !_floating.TryGetValue(window, out var item))
            {
                return;
            }

            var (host, target) = ResolveAcrossHosts(pointer);
            ClearActiveDrag();

            if (!target.IsValid)
            {
                // Dropped away from the dock host, so it stays floating -- and stays registered, so it can
                // be dragged back in later. Reported anyway: it has moved, and where a floating panel sits
                // is part of the layout.
                FloatingChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            Unregister(window);

            // Detached before docking: the content is about to be re-parented into the tree, and the
            // window must not still be holding it.
            window.Child = null;
            window.Close();

            host!.Dock(item, target.Side, target.Group);
            _root.UpdateLayout(_root.Size);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is UiWindow window)
            {
                Unregister(window);
            }
        }

        private void ClearActiveDrag()
        {
            _activeWindow = null;
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
