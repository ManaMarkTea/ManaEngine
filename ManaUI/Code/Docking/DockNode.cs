namespace ManaUI
{
    /// <summary>
    /// Base for the two things a dock layout is made of: a split and a tab group.
    /// <para>
    /// Dock nodes are real elements rather than a separate data model mirrored into one. Layout, hit
    /// testing and rendering then come from the existing element machinery instead of being reimplemented,
    /// and the tree itself is the single source of truth -- there is no model/view pair to fall out of sync.
    /// Persistence walks this tree directly.
    /// </para>
    /// </summary>
    public abstract class DockNode : Panel
    {
        /// <summary>The split that contains this node, or null at the root of a dock tree.</summary>
        public DockSplit? ParentSplit => Parent as DockSplit;

        /// <summary>The host this node belongs to, or null while detached.</summary>
        public DockHost? Host
        {
            get
            {
                for (UiElement? current = this; current != null; current = current.Parent)
                {
                    if (current is DockHost host)
                    {
                        return host;
                    }
                }

                return null;
            }
        }

        /// <summary>True when the node holds nothing and should be collapsed out of the tree.</summary>
        public abstract bool IsEmpty { get; }
    }

    /// <summary>
    /// One dockable panel: a title and its content. Not an element itself -- it is the payload a
    /// <see cref="DockTabGroup"/> holds and a tab strip renders, and it survives being moved between groups.
    /// </summary>
    public sealed class DockItem
    {
        public DockItem(string title, UiElement content)
        {
            ArgumentNullException.ThrowIfNull(content);

            Title = title ?? string.Empty;
            Content = content;
        }

        public string Title { get; set; }
        public UiElement Content { get; }
        public bool CanClose { get; set; } = true;

        /// <summary>Stable id used when saving and restoring a layout.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The group currently showing this item, or null while it is being dragged.</summary>
        public DockTabGroup? Owner { get; internal set; }
    }
}
