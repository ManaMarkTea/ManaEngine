namespace ManaUI
{
    /// <summary>
    /// Turns a tab pulled off a strip into a floating window, and a floating window dropped over a dock
    /// host back into a docked panel.
    /// <para>
    /// This is deliberately the only piece that knows about both docking and floating windows. The nodes
    /// report that a tab left the strip and the window reports that it moved; neither knows what the other
    /// is, which keeps a tear-out from being wired into the dock tree itself.
    /// </para>
    /// <para>
    /// Native windows are docking targets like the editor's own columns. Every drag is followed in screen
    /// coordinates, so a panel pulled out of one window can be dropped into any other.
    /// </para>
    /// </summary>
    public sealed class DockDragController
    {
        /// <summary>Travel before a window drag counts as one, so a click on a strip is not a drop.</summary>
        private const float DragThreshold = 4.0f;

        /// <summary>Where a window being dragged is parked while a drop target is showing, clear of the preview.</summary>
        private static readonly Vector2 ParkedAt = new(-30000.0f, -30000.0f);

        private readonly UiRoot _root;
        private readonly List<DockHost> _hosts = new();

        /// <summary>One drop preview per window being dropped into, created when a drag first reaches it.</summary>
        private readonly Dictionary<UiRoot, DockDropOverlay> _overlays = new();

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

        private readonly List<IUiDesktopWindow> _desktop = new();

        /// <summary>Where each panel that has left the columns goes when it is docked back.</summary>
        private readonly Dictionary<DockItem, DockHost> _itemHomes = new();

        private UiWindow? _activeWindow;
        private WindowDrag? _windowDrag;
        private DockDropTarget _target;

        private sealed class WindowDrag
        {
            public WindowDrag(IUiDesktopWindow window, Vector2 grabOffset, Vector2 start)
            {
                Window = window;
                GrabOffset = grabOffset;
                Start = start;
            }

            public IUiDesktopWindow Window { get; }

            /// <summary>Where in the window the pointer took hold, so the window follows without jumping.</summary>
            public Vector2 GrabOffset { get; }

            public Vector2 Start { get; }
            public bool Moved { get; set; }
            public bool Parked { get; set; }
        }

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
            OverlayFor(_root);

            foreach (var host in _hosts)
            {
                AttachTo(host.RootNode);

                var owner = host;
                owner.LayoutChanged += (_, _) => AttachTo(owner.RootNode);
            }
        }

        /// <summary>Size a torn-out panel becomes when it starts floating.</summary>
        public Vector2 FloatingSize { get; set; } = new Vector2(360.0f, 280.0f);

        /// <summary>True while a dockable window is being dragged.</summary>
        public bool IsDragging => _activeWindow != null || _windowDrag != null;

        /// <summary>Floating windows that can be dropped back into the layout.</summary>
        public IReadOnlyCollection<UiWindow> FloatingWindows => _floating.Keys;

        /// <summary>Windows the layout has popped out into the OS, oldest first.</summary>
        public IReadOnlyList<IUiDesktopWindow> DesktopWindows => _desktop;

        public bool CanPopOut => _root.CreateDesktopWindow != null;

        /// <summary>
        /// The panels currently floating rather than docked. A caller asking whether a panel is open has to
        /// count these, or a panel torn out of the dock reads as closed -- and opening it again makes a
        /// second copy of something already on screen.
        /// </summary>
        public IEnumerable<DockItem> FloatingItems =>
            _floating.Values.Concat(_desktop.SelectMany(window => window.Frame.Dock.AllItems()));

        /// <summary>The panel a floating window holds, or null when the window is not one of ours.</summary>
        public DockItem? ItemFor(UiWindow window) =>
            _floating.TryGetValue(window, out var item) ? item : null;

        /// <summary>Raised when a panel is floated, docked, moved or closed, so a layout can be saved.</summary>
        public event EventHandler? FloatingChanged;

        /// <summary>The drop target as of the last drag update, for tests and diagnostics.</summary>
        public DockDropTarget CurrentTarget => _target;

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

        public IUiDesktopWindow? FindDesktop(string id) =>
            _desktop.FirstOrDefault(window => window.Frame.Dock.AllItems().Any(item => item.Id == id));

        /// <summary>Index of the host a window's first panel returns to, for the saved layout.</summary>
        public int DesktopHomeIndex(IUiDesktopWindow window)
        {
            var item = window.Frame.Dock.AllItems().FirstOrDefault();
            return item == null ? 0 : HomeIndexOf(item);
        }

        public int HomeIndexOf(DockItem item) => Math.Max(0, _hosts.IndexOf(HomeFor(item, 0)));

        // ----- native windows -------------------------------------------------------------------------

        /// <summary>
        /// Moves the existing content into a native window. Creation precedes detachment, so a platform
        /// failure leaves the panel usable in its current host.
        /// </summary>
        public IUiDesktopWindow? PopOut(DockItem item, int fallbackHomeIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (_root.CreateDesktopWindow == null)
            {
                return null;
            }

            if (WindowOf(item) is { } existing)
            {
                existing.Focus();
                return existing;
            }

            var window = _root.CreateDesktopWindow(item.Title);
            var home = HomeFor(item, fallbackHomeIndex);
            Register(window);
            MoveIntoDesktop(item, window, home);
            return window;
        }

        /// <summary>
        /// Opens one native window holding a whole saved arrangement of panels. Null when none of them
        /// could be resolved, in which case no window is left behind.
        /// </summary>
        public IUiDesktopWindow? PopOutLayout(
            DockLayoutNode layout,
            Func<string, DockItem?> resolve,
            Func<string, int> homeIndexOf)
        {
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(resolve);

            if (_root.CreateDesktopWindow == null)
            {
                return null;
            }

            var window = _root.CreateDesktopWindow(string.Empty);
            Register(window);

            var homes = new Dictionary<DockItem, DockHost>();

            DockItem? Resolve(string id)
            {
                var item = resolve(id);
                if (item != null)
                {
                    homes[item] = _hosts[Math.Clamp(homeIndexOf(id), 0, _hosts.Count - 1)];
                }

                return item;
            }

            if (!DockLayout.Apply(window.Frame.Dock, layout, Resolve))
            {
                CloseDesktop(window);
                return null;
            }

            foreach (var pair in homes)
            {
                _itemHomes[pair.Key] = pair.Value;
            }

            OnDesktopLayoutChanged(window);
            return window;
        }

        /// <summary>Used before a workspace rebuild; callers save the layout first.</summary>
        public void CloseDesktopWindows()
        {
            // Silent: this runs ahead of a rebuild that restores from the saved layout, and reporting each
            // close would save the layout with the windows already gone, erasing what is about to be restored.
            foreach (var window in _desktop.ToArray())
            {
                CloseDesktop(window, notify: false);
            }

            _itemHomes.Clear();
        }

        private IUiDesktopWindow? WindowOf(DockItem item) =>
            _desktop.FirstOrDefault(window => window.Frame.Dock.AllItems().Contains(item));

        /// <summary>
        /// The host a panel returns to. Where it is docked right now wins over what was remembered, because
        /// a remembered home goes stale the moment the panel is docked somewhere else by other means.
        /// </summary>
        private DockHost HomeFor(DockItem item, int fallbackIndex)
        {
            return _hosts.FirstOrDefault(host => host.AllItems().Contains(item))
                ?? (_itemHomes.TryGetValue(item, out var remembered) ? remembered : null)
                ?? _hosts[Math.Clamp(fallbackIndex, 0, _hosts.Count - 1)];
        }

        private void MoveIntoDesktop(DockItem item, IUiDesktopWindow window, DockHost home)
        {
            _itemHomes[item] = home;
            DetachFromCurrentPlace(item);
            window.Frame.Dock.Dock(item, DockSide.Center, DockHost.FindFirstGroup(window.Frame.Dock.RootNode));

            _root.FocusManager.ValidateFocus();
            _root.Input.ClearKeyboardState();
            _root.Input.ReleasePointerCapture();
            FloatingChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Takes an item out of whichever dock group or in-app float holds it.</summary>
        private void DetachFromCurrentPlace(DockItem item)
        {
            item.Owner?.Remove(item);

            var floating = _floating.FirstOrDefault(pair => ReferenceEquals(pair.Value, item)).Key;
            if (floating != null)
            {
                floating.Child = null;
                Unregister(floating);
                floating.Close();
            }

            foreach (var host in _hosts)
            {
                host.Prune();
            }
        }

        private void Register(IUiDesktopWindow window)
        {
            _desktop.Add(window);
            window.DockBack += () => DockWindowBack(window);
            window.BoundsChanged += () => FloatingChanged?.Invoke(this, EventArgs.Empty);
            window.Frame.Dock.LayoutChanged += (_, _) => OnDesktopLayoutChanged(window);
            window.Frame.DragBegan += screen => BeginWindowDrag(window, screen);
            window.Frame.DragMoved += UpdateWindowDrag;
            window.Frame.DragEnded += EndWindowDrag;
            window.Frame.ResizeRequested += (origin, size) =>
            {
                window.MoveTo(origin);
                window.Size = size;
            };
        }

        private void OnDesktopLayoutChanged(IUiDesktopWindow window)
        {
            if (!_desktop.Contains(window))
            {
                return;
            }

            var dock = window.Frame.Dock;
            if (dock.RootNode == null)
            {
                // Everything was closed, or moved out to another window.
                CloseDesktop(window);
                return;
            }

            AttachTo(dock.RootNode);

            // A window that is one tab has nothing to reorder or tear out, so dragging that tab moves it.
            if (dock.RootNode is DockTabGroup lone)
            {
                lone.TabsMoveWindow = lone.Items.Count == 1;
            }

            FloatingChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CloseDesktop(IUiDesktopWindow window, bool notify = true)
        {
            if (!_desktop.Remove(window))
            {
                return;
            }

            _overlays.Remove(window.Root);
            if (_windowDrag?.Window == window)
            {
                _windowDrag = null;
            }

            window.Dispose();
            if (notify)
            {
                FloatingChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void DockItemHome(DockItem item)
        {
            var home = _itemHomes.GetValueOrDefault(item) ?? _hosts[0];
            _itemHomes.Remove(item);
            home.Dock(item, DockSide.Center, DockHost.FindFirstGroup(home.RootNode));
        }

        /// <summary>Returns every panel a window holds to where it came from, then closes the window.</summary>
        private void DockWindowBack(IUiDesktopWindow window)
        {
            if (!_desktop.Contains(window))
            {
                return;
            }

            foreach (var item in window.Frame.Dock.AllItems().ToList())
            {
                DockItemHome(item);
            }

            CloseDesktop(window);
            _root.InvalidateAll();
        }

        // ----- tab strips -----------------------------------------------------------------------------

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
            if (WindowOf(e.Item) is { } source)
            {
                TearOutOfDesktop(source, e.Item, e.Position);
                return;
            }

            BeginFloat(e.Item, e.Position);
        }

        private void OnTabDoubleClicked(object? sender, DockItem item)
        {
            // Inside a native window a double-click sends the panel home; in the editor it pops it out.
            if (WindowOf(item) != null)
            {
                DockItemHome(item);
                _root.InvalidateAll();
                return;
            }

            // Unsupported hosts keep ordinary tab selection. A native creation failure must
            // not escape an input callback or remove the still-docked panel.
            try
            {
                PopOut(item);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Could not pop out {item.Title}: {ex}");
            }
        }

        /// <summary>
        /// A tab pulled off a native window's strip becomes a native window of its own, following the pointer.
        /// The source window keeps the pointer capture -- the OS button is still down there -- and feeds the
        /// new window's movement.
        /// </summary>
        private void TearOutOfDesktop(IUiDesktopWindow source, DockItem item, Vector2 pointer)
        {
            var screen = source.Frame.ToScreen(pointer);
            var alone = source.Frame.Dock.AllItems().Count() == 1;

            if (alone || _root.CreateDesktopWindow == null)
            {
                _windowDrag = new WindowDrag(source, screen - source.Position, screen);
                source.Frame.BeginExternalDrag();
                return;
            }

            var window = _root.CreateDesktopWindow(item.Title);
            var home = HomeFor(item, 0);
            var grab = new Vector2(MathF.Min(source.Size.X * 0.5f, 120.0f), 12.0f);

            window.Size = source.Size;
            window.MoveTo(screen - grab);
            Register(window);
            MoveIntoDesktop(item, window, home);

            _windowDrag = new WindowDrag(window, grab, screen) { Moved = true };
            source.Frame.BeginExternalDrag();
        }

        // ----- in-app floating windows ----------------------------------------------------------------

        /// <summary>
        /// Pulls an item out of the dock tree into a floating window that continues the drag already under
        /// way, so the gesture is unbroken from the user's point of view.
        /// </summary>
        public UiWindow BeginFloat(DockItem item, Vector2 pointer)
        {
            ArgumentNullException.ThrowIfNull(item);

            var home = _hosts.FirstOrDefault(host => host.AllItems().Contains(item));
            if (home != null)
            {
                _itemHomes[item] = home;
            }

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
            var resolved = ResolveAtScreen(MainOrigin + pointer * _root.ScreenScale, null);

            // Dragged out past the edge of the editor's window, over nothing it could dock into (a torn-off window there
            // still takes it): it becomes a window of its own there and then, and the rest of the drag moves that,
            // rather than it being clipped at the edge until let go.
            if (CanPopOut && !resolved.Target.IsValid && _floating.TryGetValue(window, out var leaving) && IsOutside(pointer))
            {
                PromoteMidDrag(window, leaving, pointer);
                return;
            }

            ShowTarget(resolved.Root, resolved.Target);
        }

        private void OnWindowDragEnded(object? sender, Vector2 pointer)
        {
            if (sender is not UiWindow window || !_floating.TryGetValue(window, out var item))
            {
                return;
            }

            var (host, target, _) = ResolveAtScreen(MainOrigin + pointer * _root.ScreenScale, null);
            ClearActiveDrag();

            if (!target.IsValid)
            {
                // Dropped away from every dock host: where the OS can host it, it becomes a window of its
                // own where the float was, so a panel that is not docked is never trapped inside the editor.
                if (TryPopOutFloat(window, item))
                {
                    return;
                }

                // Otherwise it stays floating -- and stays registered, so it can be dragged back in later.
                // Reported anyway: it has moved, and where a floating panel sits is part of the layout.
                FloatingChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            Unregister(window);

            // Detached before docking: the content is about to be re-parented into the tree, and the
            // window must not still be holding it.
            window.Child = null;
            window.Close();

            DockInto(host!, item, target);
        }

        /// <summary>How far past the window's edge (logical pixels) the pointer goes before a float is promoted.</summary>
        public const float PromoteMargin = 4.0f;

        private bool IsOutside(Vector2 pointer) =>
            pointer.X < -PromoteMargin || pointer.Y < -PromoteMargin
            || pointer.X > _root.Size.X + PromoteMargin || pointer.Y > _root.Size.Y + PromoteMargin;

        private DockDragCarrier? _carrier;

        /// <summary>
        /// Turns a float being dragged into a native window where it is, held at the same point, and carries the drag on
        /// as that window's: back over a dock target it docks, anywhere else it stays a window.
        /// </summary>
        private void PromoteMidDrag(UiWindow floating, DockItem item, Vector2 pointer)
        {
            var scale = _root.ScreenScale;
            var screen = MainOrigin + pointer * scale;
            var grab = (pointer - floating.Position) * scale;
            var size = floating.Bounds.Size * scale;
            ClearActiveDrag();

            IUiDesktopWindow? native;
            try
            {
                native = PopOut(item);
            }
            catch (Exception ex)
            {
                // The panel is still a float if the platform refused, so nothing is lost.
                System.Diagnostics.Trace.WriteLine($"Could not pop out {item.Title}: {ex}");
                return;
            }
            if (native == null)
            {
                return;
            }

            if (size.X > 0.0f && size.Y > 0.0f)
            {
                native.Size = size;
            }
            native.MoveTo(screen - grab);
            _windowDrag = new WindowDrag(native, grab, screen) { Moved = true };

            if (_carrier == null)
            {
                _carrier = new DockDragCarrier();
                _root.OverlayLayer.Children.Add(_carrier);
            }
            _carrier.Begin(
                moved => UpdateWindowDrag(MainOrigin + moved * _root.ScreenScale),
                released => EndWindowDrag(MainOrigin + released * _root.ScreenScale));
        }

        private bool TryPopOutFloat(UiWindow floating, DockItem item)
        {
            if (!CanPopOut)
            {
                return false;
            }

            var origin = MainOrigin + floating.Position * _root.ScreenScale;
            var size = floating.Bounds.Size;

            try
            {
                if (PopOut(item) is not { } native)
                {
                    return false;
                }

                if (size.X > 0.0f && size.Y > 0.0f)
                {
                    native.Size = size;
                }

                native.MoveTo(origin);
                return true;
            }
            catch (Exception ex)
            {
                // The panel is still a float if the platform refused, so nothing is lost.
                System.Diagnostics.Trace.WriteLine($"Could not pop out {item.Title}: {ex}");
                return false;
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is UiWindow window)
            {
                Unregister(window);
            }
        }

        // ----- native window drags --------------------------------------------------------------------

        private void BeginWindowDrag(IUiDesktopWindow window, Vector2 screen)
        {
            _windowDrag = new WindowDrag(window, screen - window.Position, screen);
        }

        private void UpdateWindowDrag(Vector2 screen)
        {
            if (_windowDrag is not { } drag)
            {
                return;
            }

            if (!drag.Moved)
            {
                if ((screen - drag.Start).Length() < DragThreshold)
                {
                    return;
                }

                drag.Moved = true;
            }

            // Only a window that is one panel can be merged into another; a grouped one just moves.
            var resolved = OnlyItemOf(drag.Window) != null
                ? ResolveAtScreen(screen, drag.Window)
                : default;

            ShowTarget(resolved.Root, resolved.Target);

            if (resolved.Target.IsValid)
            {
                // Out of the way of its own drop preview -- a native window always draws over the window
                // beneath it, so left in place it would cover exactly what it is being dropped onto.
                if (!drag.Parked)
                {
                    drag.Window.MoveTo(ParkedAt);
                    drag.Parked = true;
                }

                return;
            }

            drag.Parked = false;
            drag.Window.MoveTo(screen - drag.GrabOffset);
        }

        private void EndWindowDrag(Vector2 screen)
        {
            if (_windowDrag is not { } drag)
            {
                return;
            }

            _windowDrag = null;

            var item = OnlyItemOf(drag.Window);
            var resolved = drag.Moved && item != null
                ? ResolveAtScreen(screen, drag.Window)
                : default;

            ClearActiveDrag();

            if (!resolved.Target.IsValid)
            {
                if (drag.Parked)
                {
                    drag.Window.MoveTo(screen - drag.GrabOffset);
                }

                if (drag.Moved)
                {
                    FloatingChanged?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            // Emptying the window closes it, so nothing is left to clean up here.
            DockInto(resolved.Host!, item!, resolved.Target);
        }

        private DockItem? OnlyItemOf(IUiDesktopWindow window)
        {
            var items = window.Frame.Dock.AllItems().Take(2).ToList();
            return items.Count == 1 ? items[0] : null;
        }

        /// <summary>Docks an item into whichever host a drop resolved to, bringing a native target forward.</summary>
        private void DockInto(DockHost host, DockItem item, DockDropTarget target)
        {
            if (_hosts.Contains(host))
            {
                _itemHomes.Remove(item);
            }

            host.Dock(item, target.Side, target.Group);

            var targetWindow = _desktop.FirstOrDefault(window => ReferenceEquals(window.Frame.Dock, host));
            if (targetWindow != null)
            {
                targetWindow.Root.UpdateLayout(targetWindow.Root.Size);
                targetWindow.Focus();
            }
            else
            {
                _root.UpdateLayout(_root.Size);
            }
        }

        // ----- drop resolution ------------------------------------------------------------------------

        private Vector2 MainOrigin => _root.ScreenOrigin?.Invoke() ?? Vector2.Zero;

        /// <summary>
        /// Finds the host under a screen position, in whichever window that is. Native windows are tried
        /// first: they are ordinarily in front of the editor, and a window the pointer is over that offers
        /// no valid drop does not fall through to the one behind it.
        /// </summary>
        private (DockHost? Host, DockDropTarget Target, UiRoot? Root) ResolveAtScreen(
            Vector2 screen,
            IUiDesktopWindow? exclude)
        {
            for (var i = _desktop.Count - 1; i >= 0; i--)
            {
                var window = _desktop[i];
                if (ReferenceEquals(window, exclude))
                {
                    continue;
                }

                var origin = window.Position;
                var size = window.Size;
                if (!new UiRect(origin.X, origin.Y, size.X, size.Y).Contains(screen))
                {
                    continue;
                }

                var candidate = DockDropResolver.Resolve(window.Frame.Dock, screen - origin);
                return candidate.IsValid ? (window.Frame.Dock, candidate, window.Root) : (null, default, null);
            }

            var local = (screen - MainOrigin) / MathF.Max(_root.ScreenScale, 0.1f);

            // An emptied column's zone sits over its neighbours' edges; it is the only way back in, so it wins.
            foreach (var host in _hosts.Where(host => host.RootNode == null && host.EmptyDropZone != null)
                         .Concat(_hosts.Where(host => host.RootNode != null || host.EmptyDropZone == null)))
            {
                var candidate = DockDropResolver.Resolve(host, local);
                if (candidate.IsValid)
                {
                    return (host, candidate, _root);
                }
            }

            return (null, default, null);
        }

        private DockDropOverlay OverlayFor(UiRoot root)
        {
            if (_overlays.TryGetValue(root, out var overlay))
            {
                return overlay;
            }

            overlay = new DockDropOverlay { Visibility = Visibility.Collapsed };
            root.OverlayLayer.Children.Add(overlay);
            _overlays[root] = overlay;
            return overlay;
        }

        /// <summary>Shows the preview in the window it belongs to and clears it everywhere else.</summary>
        private void ShowTarget(UiRoot? root, DockDropTarget target)
        {
            _target = target;

            foreach (var pair in _overlays)
            {
                if (target.IsValid && ReferenceEquals(pair.Key, root))
                {
                    continue;
                }

                pair.Value.Hide();
            }

            if (target.IsValid && root != null)
            {
                OverlayFor(root).Show(target);
            }
        }

        private void ClearActiveDrag()
        {
            _activeWindow = null;
            ShowTarget(null, default);
        }
    }
}
