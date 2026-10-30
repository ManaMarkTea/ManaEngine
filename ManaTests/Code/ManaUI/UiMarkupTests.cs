using ManaUI;

namespace ManaTests;

/// <summary>
/// <see cref="UiMarkup"/>: a tree from JSON, with properties by name, theme colours, grid placement, polled
/// values and actions -- and problems reported rather than thrown, so a typo in a file does not blank a screen.
/// </summary>
public static class UiMarkupChecks
{
    private const string Menu = """
        {
          "type": "Grid", "name": "menu", "columns": "Auto, *", "rows": "Auto, Auto, Auto", "padding": [8, 4],
          "children": [
            { "type": "TextBlock", "name": "title", "text": "Mana", "fontSize": 24, "foreground": "@Accent", "grid": { "row": 0, "column": 0, "columnSpan": 2 } },
            { "type": "TextBlock", "name": "score", "text": "0", "bind": { "text": "score" }, "grid": { "row": 1, "column": 0 } },
            { "type": "Button", "name": "play", "text": "Play", "on": { "click": "play" }, "grid": { "row": 1, "column": 1 }, "horizontalAlignment": "Right" },
            { "type": "Border", "background": "#20304080", "child": { "type": "ComboBox", "name": "mode", "options": ["Easy", "Hard"] }, "grid": { "row": 2, "column": 1 } }
          ]
        }
        """;

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var score = 0;
        var played = 0;
        var theme = new UiTheme();
        var data = new UiMarkupData().Value("score", () => score).Action("play", () => played++);
        var result = UiMarkup.Load(Menu, theme, data);

        Check("a document builds its tree", result.Root is Grid { Children.Count: 4 }, $"{result.Root?.GetType().Name}");
        Check("with nothing it could not apply", result.Problems.Count == 0, string.Join("; ", result.Problems));

        var title = result.Find<TextBlock>("title");
        Check("properties are set by name", title is { Text: "Mana", FontSize: 24.0f });
        Check("an @ colour is the theme's", title?.Foreground == theme.Accent);
        Check("and a child sits where its grid entry says",
            title?.LayoutSlotOrNull is { GridRow: 0, GridColumn: 0, GridColumnSpan: 2 }
            && result.Find<Button>("play")?.LayoutSlotOrNull is { GridRow: 1, GridColumn: 1 });
        Check("a grid takes its columns and rows", result.Root is Grid { ColumnDefinitions.Count: 2, RowDefinitions.Count: 3 });
        Check("a thickness reads from [horizontal, vertical]", result.Root is Panel { Padding: { Left: 8.0f, Top: 4.0f } });
        Check("a border takes a child, and a combo box its options", result.Find<ComboBox>("mode") is { Items.Count: 2 });

        var root = new UiRoot(fonts) { Content = result.Root };
        score = 42;
        root.UpdateLayout(new Vector2(400, 200));
        Check("a bound value is read each frame", result.Find<TextBlock>("score")?.Text == "42", result.Find<TextBlock>("score")?.Text ?? "");

        var play = result.Find<Button>("play")!;
        root.Input.InjectPointerMoved(play.Bounds.Center);
        root.Input.InjectPointerPressed(play.Bounds.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(play.Bounds.Center, PointerButton.Left);
        Check("an event runs its action", played == 1, $"{played}");

        float? cooling = 0.5f;
        var slotLayout = UiMarkup.Load("""{ "type": "ItemSlot", "name": "cannon", "bind": { "progress": "cooling" } }""",
            theme, new UiMarkupData().Value("cooling", () => cooling));
        var slotRoot = new UiRoot(fonts) { Content = slotLayout.Root };
        slotRoot.UpdateLayout(new Vector2(100, 100));
        Check("a nullable bound value shows weapon cooldown", slotLayout.Find<ItemSlot>("cannon")?.Progress == 0.5f);
        cooling = null;
        slotRoot.UpdateLayout(new Vector2(100, 100));
        Check("a null binding clears the finished cooldown", slotLayout.Find<ItemSlot>("cannon")?.Progress == null);

        var broken = UiMarkup.Load("""{ "type": "StackPanel", "children": [ { "type": "Nope" }, { "type": "TextBlock", "colour": "red", "fontSize": "big" } ] }""");
        Check("what cannot be applied is reported, and the rest still builds",
            broken.Root is StackPanel { Children.Count: 1 } && broken.Problems.Count == 3, string.Join("; ", broken.Problems));
        Check("and a file that is not JSON says so", UiMarkup.Load("{ nope").Problems.Count == 1);

        var unbound = UiMarkup.Load(Menu, theme);
        Check("a layout loads with nothing behind it, showing what it was designed with",
            unbound.Problems.Count == 0 && unbound.Find<TextBlock>("score")?.Text == "0");
    }
}
