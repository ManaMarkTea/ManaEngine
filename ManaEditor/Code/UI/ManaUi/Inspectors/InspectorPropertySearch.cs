using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>Filters retained property rows without replacing their editors or bindings.</summary>
    public sealed class InspectorPropertySearch(UiElement content)
    {
        public sealed record PropertyLabel(string Text);
        private readonly Dictionary<UiElement, Visibility> _visibility = new();
        private readonly Dictionary<Expander, bool> _expanded = new();

        public void Apply(string query)
        {
            foreach (var (element, visibility) in _visibility) { element.Visibility = visibility; }
            foreach (var (expander, expanded) in _expanded) { expander.IsExpanded = expanded; }
            _visibility.Clear();
            _expanded.Clear();
            query = query.Trim();
            if (query.Length == 0) { return; }
            Visit(content, false);

            (int Rows, int Matches) Visit(UiElement element, bool sectionMatches)
            {
                if (element is Expander section)
                {
                    sectionMatches |= section.Header.Contains(query, StringComparison.OrdinalIgnoreCase);
                }
                if (element.Tag is PropertyLabel label)
                {
                    var matches = sectionMatches || label.Text.Contains(query, StringComparison.OrdinalIgnoreCase);
                    SetVisible(element, matches);
                    return (1, matches ? 1 : 0);
                }
                var rows = 0;
                var matchesBelow = 0;
                for (var i = 0; i < element.VisualChildCount; i++)
                {
                    var result = Visit(element.GetVisualChild(i), sectionMatches);
                    rows += result.Rows;
                    matchesBelow += result.Matches;
                }
                if (element is Expander expander && rows > 0)
                {
                    _expanded[expander] = expander.IsExpanded;
                    expander.IsExpanded = matchesBelow > 0;
                    SetVisible(expander, matchesBelow > 0);
                }
                return (rows, matchesBelow);
            }
        }

        private void SetVisible(UiElement element, bool visible)
        {
            _visibility[element] = element.Visibility;
            if (!visible) { element.Visibility = Visibility.Collapsed; }
        }
    }
}
