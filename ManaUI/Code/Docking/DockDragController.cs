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
        private readonly Dictionary<IUiDesktopWindow, DockItem> _desktop = new();
        private readonly Dictionary<IUiDesktopWindow, DockHost> _desktopHomes = new();
        public int DesktopHomeIndex(IUiDesktopWindow window) => _hosts.IndexOf(_desktopHomes[window]);
        public IReadOnlyDictionary<IUiDesktopWindow, DockItem> DesktopWindows => _desktop;
        public bool CanPopOut => _root.CreateDesktopWindow != null;

        public IUiDesktopWindow? FindDesktop(string id) =>
            _desktop.FirstOrDefault(pair => pair.Value.Id == id).Key;

        /// <summary>Moves the existing content into a native window. Creation precedes detachment,
        /// so a platform failure leaves the panel usable in its current host.</summary>
        public IUiDesktopWindow? PopOut(DockItem item, int fallbackHomeIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (_root.CreateDesktopWindow == null) return null;
            var existing = _desktop.FirstOrDefault(pair => ReferenceEquals(pair.Value, item)).Key;
            if (existing != null) { existing.Focus(); return existing; }
            var window = _root.CreateDesktopWindow(item.Title);
            var home = _hosts.FirstOrDefault(host => host.AllItems().Contains(item))
                ?? _hosts[Math.Clamp(fallbackHomeIndex, 0, _hosts.Count - 1)];
            _desktop.Add(window, item);
            _desktopHomes.Add(window, home);
            item.Owner?.Remove(item);
            var floating = _floating.FirstOrDefault(pair => ReferenceEquals(pair.Value, item)).Key;
            if (floating != null)
            {
                floating.Child = null;
                Unregister(floating);
                floating.Close();
            }
            foreach (var host in _hosts) host.Prune();
            window.Item = item;
            _root.FocusManager.ValidateFocus();
            _root.Input.ClearKeyboardState();
            _root.Input.ReleasePointerCapture();
            window.DockBack += () =>
            {
                if (!_desktop.Remove(window)) return;
                _desktopHomes.Remove(window);
                window.Item = null;
                window.Dispose();
                home.Dock(item, DockSide.Center, DockHost.FindFirstGroup(home.RootNode));
                _root.InvalidateAll();
                FloatingChanged?.Invoke(this, EventArgs.Empty);
            };
            window.BoundsChanged += () => FloatingChanged?.Invoke(this, EventArgs.Empty);
            FloatingChanged?.Invoke(this, EventArgs.Empty);
            return window;
        }

        /// <summary>Used before a workspace rebuild; callers save the layout first.</summary>
        public void CloseDesktopWindows()
        {
            foreach (var window in _desktop.Keys.ToArray()) window.Dispose();
            _desktop.Clear();
            _desktopHomes.Clear();
        }

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
        public IEnumerable<DockItem> FloatingItems => _floating.Values.Concat(_desktop.Values);

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
                group.TabDoubleClicked -= OnTabDoubleClicked;
                group.TabDoubleClicked += OnTabDoubleClicked;
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

        private void OnTabDoubleClicked(object? sender, DockItem item)
        {
            // Unsupported hosts keep ordinary tab selection. A native creation failure must
            // not escape an input callback or remove the still-docked panel.
            try { PopOut(item); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Could not pop out {item.Title}: {ex}"); }
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

        /// <summary>
        /// Asks for the whole root, because that is the area this paints into.
        /// <para>
        /// This used to report zero and try to claim the root back in <see cref="ArrangeOverride"/>, which
        /// cannot work: Arrange clamps whatever ArrangeOverride returns to the greater of the slot and the
        /// desired size, and both of those were zero. So Bounds stayed 0x0, UiElement.Render skips an
        /// element with empty bounds, and the drop preview was never drawn at all -- which is why dropping
        /// a panel felt like a guess. It was: there was nothing on screen to read.
        /// </para>
        /// </summary>
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Root?.Size ?? Vector2.Zero;
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

            // Drawn after the preview so the buttons sit on top of the wash rather than under it.
            foreach (var indicator in DockDropIndicators.ForHost(_target.HostBounds, _target.GroupBounds))
            {
                RenderIndicator(drawList, theme, indicator);
            }

            foreach (var indicator in DockDropIndicators.ForGroup(_target.GroupBounds))
            {
                RenderIndicator(drawList, theme, indicator);
            }
        }

        /// <summary>
        /// One drop button: a raised chip with a miniature of the layout it would produce inside it. The
        /// miniature is the whole point -- an arrow says "that way", a shaded half says "the panel ends up
        /// here and the pane keeps the rest", which is the thing actually being chosen.
        /// </summary>
        private void RenderIndicator(ManaUiDrawList drawList, UiTheme theme, DockDropIndicator indicator)
        {
            var armed = indicator.Side == _target.Side && indicator.IsRootEdge == _target.IsRootEdge;
            var box = indicator.Box;

            // Lifted off whatever is behind it: these land on top of a viewport or a busy panel, and a flat
            // chip on a light surface has nothing to separate it from the content it is covering.
            drawList.DrawShadow(box, new Vector4(0.0f, 0.0f, 0.0f, 0.35f), 8.0f, new CornerRadius(5.0f));
            drawList.FillRect(box, armed ? theme.Accent : theme.SurfaceRaised, new CornerRadius(5.0f));
            drawList.DrawBorder(
                box, armed ? theme.Accent : theme.BorderStrong, 1.0f, new CornerRadius(5.0f));

            var inner = box.Deflate(new Thickness(6.0f));
            var fill = armed ? theme.TextOnAccent : theme.TextMuted;

            // The pane as it is now, faint, with the half the panel would take drawn solid over it.
            drawList.DrawBorder(inner, fill.WithAlpha(0.55f), 1.0f, new CornerRadius(1.5f));

            var half = indicator.Side switch
            {
                DockSide.Left => new UiRect(inner.X, inner.Y, inner.Width * 0.5f, inner.Height),
                DockSide.Right => new UiRect(inner.Center.X, inner.Y, inner.Width * 0.5f, inner.Height),
                DockSide.Top => new UiRect(inner.X, inner.Y, inner.Width, inner.Height * 0.5f),
                DockSide.Bottom => new UiRect(inner.X, inner.Center.Y, inner.Width, inner.Height * 0.5f),
                _ => inner
            };

            drawList.FillRect(half, fill.WithAlpha(indicator.Side == DockSide.Center ? 0.75f : 0.9f),
                new CornerRadius(1.5f));

            // Centre means "become a tab here", and a solid block alone does not say tab. A stub along the
            // top edge reads as one, at the size a 20px square has to work with.
            if (indicator.Side == DockSide.Center)
            {
                drawList.FillRect(
                    new UiRect(inner.X, inner.Y, inner.Width * 0.45f, 3.0f),
                    armed ? theme.Accent : theme.SurfaceRaised,
                    new CornerRadius(1.0f));
            }
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
