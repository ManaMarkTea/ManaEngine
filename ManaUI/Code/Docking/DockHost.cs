namespace ManaUI
{
    /// <summary>Where a panel lands relative to an existing node.</summary>
    public enum DockSide
    {
        Left,
        Right,
        Top,
        Bottom,

        /// <summary>Added as another tab in the target group rather than splitting it.</summary>
        Center
    }

    /// <summary>
    /// Root of a dock layout. Owns the node tree and performs every structural change to it, so the
    /// invariants -- no empty nodes, no split with a single child, no orphaned content -- live in one place
    /// rather than being re-derived at each call site.
    /// </summary>
    public class DockHost : Decorator
    {
        public DockHost()
        {
            ClipToBounds = true;
        }

        /// <summary>Raised after any structural change, for hosts that persist the layout.</summary>
        public event EventHandler? LayoutChanged;

        /// <summary>The tree root, or null when the layout is empty.</summary>
        public DockNode? RootNode
        {
            get => Child as DockNode;
            set
            {
                Child = value;
                AttachEmptyHandlers(value);
            }
        }

        /// <summary>
        /// Docks an item relative to <paramref name="target"/>, or into the root when target is null.
        /// </summary>
        public DockTabGroup Dock(DockItem item, DockSide side, DockNode? target = null)
        {
            ArgumentNullException.ThrowIfNull(item);

            // Detach from wherever it was first: the same item docked twice must move, not duplicate.
            item.Owner?.Remove(item);

            if (RootNode == null)
            {
                var group = new DockTabGroup(item);
                RootNode = group;
                OnLayoutChanged();
                return group;
            }

            target ??= FindFirstGroup(RootNode) ?? RootNode;

            if (side == DockSide.Center && target is DockTabGroup existing)
            {
                existing.Add(item);
                OnLayoutChanged();
                return existing;
            }

            var created = new DockTabGroup(item);
            SplitInto(target, created, side);
            OnLayoutChanged();
            return created;
        }

        /// <summary>
        /// Replaces <paramref name="target"/> with a split containing it and the new node.
        /// <para>
        /// When the target's parent is already a split along the same axis, the new node joins it as a
        /// sibling instead of nesting another split inside it -- without that, repeatedly docking to one
        /// side builds a deep chain of two-child splits that behaves subtly differently from a flat row.
        /// </para>
        /// </summary>
        private void SplitInto(DockNode target, DockNode inserted, DockSide side)
        {
            var orientation = side is DockSide.Left or DockSide.Right
                ? Orientation.Horizontal
                : Orientation.Vertical;

            var before = side is DockSide.Left or DockSide.Top;
            var parent = target.ParentSplit;

            if (parent != null && parent.Orientation == orientation)
            {
                var index = parent.Children.IndexOf(target);
                var weight = index >= 0 && index < parent.Weights.Count ? parent.Weights[index] : 1.0f;

                // The pair share the space the target had, so the rest of the row keeps its proportions.
                parent.SetWeight(index, weight * 0.5f);
                parent.Insert(before ? index : index + 1, inserted, weight * 0.5f);
                AttachEmptyHandlers(inserted);
                return;
            }

            var split = new DockSplit(orientation);
            var host = target.Parent;

            // Swap the target out for the split, then put the target inside it.
            if (ReferenceEquals(host, this))
            {
                Child = null;
                RootNode = split;
            }
            else if (host is DockSplit hostSplit)
            {
                var index = hostSplit.Children.IndexOf(target);
                var weight = index >= 0 && index < hostSplit.Weights.Count ? hostSplit.Weights[index] : 1.0f;
                hostSplit.Remove(target);
                hostSplit.Insert(index, split, weight);
            }

            if (before)
            {
                split.Add(inserted, 0.5f);
                split.Add(target, 0.5f);
            }
            else
            {
                split.Add(target, 0.5f);
                split.Add(inserted, 0.5f);
            }

            AttachEmptyHandlers(split);
        }

        /// <summary>
        /// Removes empty groups and collapses splits that no longer need to exist. Runs after any removal,
        /// because a layout containing an empty pane or a one-child split is not a valid layout.
        /// </summary>
        public void Prune()
        {
            if (RootNode == null)
            {
                return;
            }

            var pruned = PruneNode(RootNode);
            if (!ReferenceEquals(pruned, RootNode))
            {
                // Detached from the split it was promoted out of before it can be re-hosted; an element
                // cannot have two parents, and the old root is still holding it at this point.
                if (pruned?.Parent is DockSplit promotedFrom)
                {
                    promotedFrom.Remove(pruned);
                }

                Child = null;
                RootNode = pruned;
            }

            OnLayoutChanged();
        }

        /// <summary>Returns what this node should be replaced by: itself, its only child, or null.</summary>
        private static DockNode? PruneNode(DockNode node)
        {
            if (node is not DockSplit split)
            {
                return node.IsEmpty ? null : node;
            }

            for (var i = split.Children.Count - 1; i >= 0; i--)
            {
                if (split.Children[i] is not DockNode child)
                {
                    continue;
                }

                var replacement = PruneNode(child);
                if (replacement == null)
                {
                    split.Remove(child);
                }
                else if (!ReferenceEquals(replacement, child))
                {
                    var weight = i < split.Weights.Count ? split.Weights[i] : 1.0f;
                    split.Remove(child);

                    // Detached before re-parenting; an element cannot have two parents.
                    (replacement.Parent as DockSplit)?.Remove(replacement);
                    split.Insert(i, replacement, weight);
                }
            }

            return split.Children.Count switch
            {
                0 => null,

                // A split with one pane is just that pane wearing a splitter that can never move.
                1 => split.Children[0] as DockNode,
                _ => split
            };
        }

        /// <summary>Depth-first search for a tab group, used as the default docking target.</summary>
        public static DockTabGroup? FindFirstGroup(UiElement? node)
        {
            if (node is DockTabGroup group)
            {
                return group;
            }

            if (node == null)
            {
                return null;
            }

            var count = node.VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                var found = FindFirstGroup(node.GetVisualChild(i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>The tab group under a point, or null.</summary>
        public DockTabGroup? GroupAt(Vector2 point)
        {
            return FindGroupAt(RootNode, point);
        }

        private static DockTabGroup? FindGroupAt(UiElement? node, Vector2 point)
        {
            if (node == null || !node.Bounds.Contains(point))
            {
                return null;
            }

            if (node is DockTabGroup group)
            {
                return group;
            }

            var count = node.VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                var found = FindGroupAt(node.GetVisualChild(i), point);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Every item in the layout, in tree order.</summary>
        public IEnumerable<DockItem> AllItems()
        {
            return Collect(RootNode);

            static IEnumerable<DockItem> Collect(UiElement? node)
            {
                if (node == null)
                {
                    yield break;
                }

                if (node is DockTabGroup group)
                {
                    foreach (var item in group.Items)
                    {
                        yield return item;
                    }

                    yield break;
                }

                var count = node.VisualChildCount;
                for (var i = 0; i < count; i++)
                {
                    foreach (var item in Collect(node.GetVisualChild(i)))
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>Subscribes to Emptied on every group beneath a node so removals self-heal.</summary>
        private void AttachEmptyHandlers(UiElement? node)
        {
            if (node == null)
            {
                return;
            }

            if (node is DockTabGroup group)
            {
                group.Emptied -= OnGroupEmptied;
                group.Emptied += OnGroupEmptied;
                return;
            }

            var count = node.VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                AttachEmptyHandlers(node.GetVisualChild(i));
            }
        }

        private void OnGroupEmptied(object? sender, EventArgs e)
        {
            Prune();
        }

        protected void OnLayoutChanged()
        {
            AttachEmptyHandlers(RootNode);
            InvalidateMeasure();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
