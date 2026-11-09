using ManaUI;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>Type part of a panel's name, press Enter or click to open it (Unreal's "find a menu").</summary>
    public static class PanelSearchManaView
    {
        /// <summary>Case-insensitive match of every typed word against the title, best (prefix) matches first.</summary>
        public static IReadOnlyList<(string Id, string Title)> Filter(IEnumerable<(string Id, string Title)> panels, string query)
        {
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return panels
                .Where(p => words.All(w => p.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => words.Length > 0 && p.Title.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Show(UiRoot root, IReadOnlyList<(string Id, string Title)> panels, Action<string> open)
        {
            var theme = root.Theme;
            var popup = new UiPopup { StaysOpen = true };
            popup.BindPolled(p => p.Width, () => root.Size.X);
            popup.BindPolled(p => p.Height, () => root.Size.Y);

            var query = new TextBox { Placeholder = "Find a panel…" };
            var list = new StackPanel().Vertical(theme.Spacing);
            var current = Filter(panels, string.Empty);

            void Choose(string id)
            {
                root.Popups.Close(popup);
                open(id);
            }

            void Rebuild()
            {
                current = Filter(panels, query.Text ?? string.Empty);
                list.Children.Clear();
                foreach (var (id, title) in current)
                {
                    var panelId = id;
                    list.Children.Add(new Button().Label(title).OnClick(() => Choose(panelId)));
                }
            }

            query.TextChanged += (_, _) => Rebuild();
            query.TextCommitted += (_, _) =>
            {
                if (current.Count > 0)
                {
                    Choose(current[0].Id);
                }
            };
            Rebuild();

            var dialog = new Grid().Rows("Auto, Auto, *", theme.Spacing).Columns("*").Padding(12.0f);
            dialog.Background = theme.Background;
            dialog.BindPolled(d => d.Width, () => MathF.Min(420.0f, root.Size.X));
            dialog.BindPolled(d => d.Height, () => MathF.Min(480.0f, root.Size.Y));
            dialog.Children.Add(new Grid().Columns("*, Auto").Rows("Auto").Children(
                new TextBlock().Text("Find Panel").CenterV().GridColumn(0),
                new Button().Label("Close").OnClick(() => root.Popups.Close(popup)).GridColumn(1)).GridRow(0));
            dialog.Children.Add(query.GridRow(1));
            dialog.Children.Add(list.Scrollable().GridRow(2));

            var overlay = new Grid().Rows("*").Columns("*").Children(dialog.CenterH().CenterV());
            overlay.Background = new Vector4(0, 0, 0, 0.55f);
            popup.Child = overlay;
            var contentEnabled = root.ContentLayer.IsEnabled;
            var floatingEnabled = root.FloatingLayer.IsEnabled;
            root.ContentLayer.IsEnabled = false;
            root.FloatingLayer.IsEnabled = false;
            popup.Closed += (_, _) =>
            {
                root.ContentLayer.IsEnabled = contentEnabled;
                root.FloatingLayer.IsEnabled = floatingEnabled;
            };
            root.Popups.ShowAt(popup, Vector2.Zero);
        }
    }
}
