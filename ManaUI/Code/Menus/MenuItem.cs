using System.Windows.Input;

namespace ManaUI
{
    /// <summary>
    /// One entry in a menu.
    /// <para>
    /// Menus are described as data rather than assembled from elements. A menu is naturally a tree of
    /// definitions, most of which is never shown -- submenus are built only when opened -- and describing
    /// it as data means an application can declare a whole menu bar without touching a UI type.
    /// </para>
    /// </summary>
    public sealed class MenuItem
    {
        public MenuItem()
        {
        }

        public MenuItem(string text, Action? invoked = null)
        {
            Text = text;
            Invoked = invoked;
        }

        public string Text { get; set; } = string.Empty;

        /// <summary>Shortcut text shown right-aligned. Display only -- menus do not register hotkeys.</summary>
        public string? Shortcut { get; set; }

        public Action? Invoked { get; set; }

        public ICommand? Command { get; set; }

        public object? CommandParameter { get; set; }

        public bool IsSeparator { get; set; }

        /// <summary>Draws a check column. Set for toggles so their rows align with each other.</summary>
        public bool IsCheckable { get; set; }

        public bool IsChecked { get; set; }

        /// <summary>Evaluated when the menu opens, so state does not have to be pushed in advance.</summary>
        public Func<bool>? IsCheckedProvider { get; set; }

        public Func<bool>? IsEnabledProvider { get; set; }

        public bool IsEnabled { get; set; } = true;

        /// <summary>Child entries. A non-empty list makes this a submenu rather than a command.</summary>
        public List<MenuItem> Items { get; } = new();

        public bool HasSubmenu => Items.Count > 0;

        public static MenuItem Separator() => new() { IsSeparator = true };

        /// <summary>Fluent helper for building a submenu inline.</summary>
        public MenuItem WithItems(params MenuItem[] items)
        {
            Items.AddRange(items);
            return this;
        }

        public MenuItem WithShortcut(string shortcut)
        {
            Shortcut = shortcut;
            return this;
        }

        public MenuItem Checkable(Func<bool> isChecked)
        {
            IsCheckable = true;
            IsCheckedProvider = isChecked;
            return this;
        }

        public MenuItem EnabledWhen(Func<bool> isEnabled)
        {
            IsEnabledProvider = isEnabled;
            return this;
        }

        internal bool ResolveEnabled()
        {
            if (IsEnabledProvider != null)
            {
                return IsEnabledProvider();
            }

            if (Command != null)
            {
                return Command.CanExecute(CommandParameter);
            }

            return IsEnabled;
        }

        internal bool ResolveChecked()
        {
            return IsCheckedProvider?.Invoke() ?? IsChecked;
        }

        internal void Invoke()
        {
            Invoked?.Invoke();

            if (Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
            }
        }
    }
}
