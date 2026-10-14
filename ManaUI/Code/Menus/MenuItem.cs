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

        /// <summary>
        /// A control living in the menu rather than a row that closes it -- a slider for a value you want
        /// to nudge while watching the result, which is a menu's job as much as a list of commands is.
        /// </summary>
        public UiElement? Content { get; set; }

        public bool HasContent => Content != null;

        /// <summary>A row that hosts a control instead of behaving like a command.</summary>
        public static MenuItem Custom(UiElement content) => new() { Content = content };

        /// <summary>
        /// A line of text that does nothing: a count, a hint, the state of something. Not the same as a
        /// disabled command, which looks like something you could have clicked at a better moment.
        /// </summary>
        public static MenuItem Label(string text) => new(text) { IsLabel = true };

        public bool IsLabel { get; set; }

        /// <summary>
        /// Text shown when the pointer rests on the row. For the reason behind a command rather than its
        /// name, which a menu has no room for.
        /// </summary>
        public string? Tooltip { get; set; }

        public MenuItem WithTooltip(string tooltip)
        {
            Tooltip = tooltip;
            return this;
        }

        /// <summary>
        /// A live right-hand column. Shortcuts are the usual occupant, but it is equally the place for a
        /// word about the row's state -- "Loading..." beside a thing being fetched.
        /// </summary>
        public Func<string>? ShortcutProvider { get; set; }

        public MenuItem ShortcutFrom(Func<string> shortcut)
        {
            ShortcutProvider = shortcut;
            return this;
        }

        public string ResolveShortcut() => ShortcutProvider?.Invoke() ?? Shortcut ?? string.Empty;

        /// <summary>A live label, for a row that says something about the state the menu is changing.</summary>
        public Func<string>? TextProvider { get; set; }

        public MenuItem TextFrom(Func<string> text)
        {
            TextProvider = text;
            return this;
        }

        public string ResolveText() => TextProvider?.Invoke() ?? Text;

        /// <summary>
        /// Clicking runs the item and leaves the menu open -- a choice among several (a tick list) made while
        /// looking at the rest of the menu, rather than a command that is done once clicked.
        /// </summary>
        public bool StaysOpen { get; set; }

        public MenuItem KeepsMenuOpen()
        {
            StaysOpen = true;
            return this;
        }

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

        /// <summary>
        /// Whether the row is available right now, from its provider if it has one. Public because it is
        /// what the row actually means -- a host testing or driving a menu needs the same answer the
        /// renderer uses, not the value that happened to be set when the menu was built.
        /// </summary>
        public bool ResolveEnabled()
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

        /// <summary>Whether the row's tick is showing right now, from its provider if it has one.</summary>
        public bool ResolveChecked()
        {
            return IsCheckedProvider?.Invoke() ?? IsChecked;
        }

        /// <summary>
        /// Runs the row's action and command. Public for the same reason as the resolvers: a host driving
        /// a menu from a shortcut or a test should go through exactly what a click does.
        /// </summary>
        public void Invoke()
        {
            Invoked?.Invoke();

            if (Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
            }
        }
    }
}
