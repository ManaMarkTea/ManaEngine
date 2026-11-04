using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManaUI;

namespace ManaTests;

public sealed partial class MenuChild : ObservableObject
{
    [ObservableProperty]
    private string _label = "first";
}

public sealed partial class MenuModel : ObservableObject
{
    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private float _volume;

    [ObservableProperty]
    private float? _cooling;

    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private MenuChild _child = new();

    public MenuChild[] Items { get; } = [new MenuChild { Label = "a" }, new MenuChild { Label = "b" }];

    /// <summary>A plain property on an observable class: it raises nothing, so it updates only on a re-read.</summary>
    public string Unannounced { get; set; } = "plain";

    public bool CanPlay { get; set; } = true;

    public int Played { get; private set; }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => Played++;
}

public sealed class PlainSource
{
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// <see cref="UiMarkup"/>: a tree from JSON, with properties by name, theme colours, grid placement, bindings
/// to a view model and commands -- and problems reported rather than thrown, so a typo in a file does not blank a screen.
/// </summary>
public static class UiMarkupChecks
{
    private const string Menu = """
        {
          "type": "Grid", "name": "menu", "columns": "Auto, *", "rows": "Auto, Auto, Auto", "padding": [8, 4],
          "children": [
            { "type": "TextBlock", "name": "title", "text": "Mana", "fontSize": 24, "foreground": "@Accent", "grid": { "row": 0, "column": 0, "columnSpan": 2 } },
            { "type": "TextBlock", "name": "score", "text": "0", "bind": { "text": "Score" }, "grid": { "row": 1, "column": 0 } },
            { "type": "Button", "name": "play", "text": "Play", "on": { "click": "PlayCommand" }, "grid": { "row": 1, "column": 1 }, "horizontalAlignment": "Right" },
            { "type": "Border", "background": "#20304080", "child": { "type": "ComboBox", "name": "mode", "options": ["Easy", "Hard"] }, "grid": { "row": 2, "column": 1 } }
          ]
        }
        """;

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var theme = new UiTheme();
        var model = new MenuModel();
        var result = UiMarkup.Load(Menu, theme, model);

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
        root.UpdateLayout(new Vector2(400, 200));
        model.Score = 42;
        Check("a change the view model announces reaches the element, with no frame in between",
            result.Find<TextBlock>("score")?.Text == "42", result.Find<TextBlock>("score")?.Text ?? "");

        var play = result.Find<Button>("play")!;
        root.Input.InjectPointerMoved(play.Bounds.Center);
        root.Input.InjectPointerPressed(play.Bounds.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(play.Bounds.Center, PointerButton.Left);
        Check("an event runs its command", model.Played == 1, $"{model.Played}");

        var listed = UiMarkup.Load("""
            { "type": "ItemsControl", "name": "list", "bind": { "itemsSource": "Items" },
              "itemTemplate": { "type": "TextBlock", "bind": { "text": "Label" } } }
            """, theme, model);
        var list = listed.Find<ItemsControl>("list");
        Check("an ItemsControl builds a row from its template for each item",
            list is { Children.Count: 2 } && list.Children[0] is TextBlock { Text: "a" } && list.Children[1] is TextBlock { Text: "b" }
            && listed.Problems.Count == 0, string.Join("; ", listed.Problems));
        model.Items[1].Label = "c";
        Check("and each row follows its own item", list?.Children[1] is TextBlock { Text: "c" });
        Check("an itemTemplate anywhere else is reported",
            UiMarkup.Load("""{ "type": "Border", "itemTemplate": { "type": "TextBlock" } }""").Problems.Count == 1);

        var slotModel = new MenuModel { Cooling = 0.5f };
        var slotLayout = UiMarkup.Load("""{ "type": "ItemSlot", "name": "cannon", "bind": { "progress": "Cooling" } }""", theme, slotModel);
        var slotRoot = new UiRoot(fonts) { Content = slotLayout.Root };
        slotRoot.UpdateLayout(new Vector2(100, 100));
        Check("a nullable bound value shows weapon cooldown", slotLayout.Find<ItemSlot>("cannon")?.Progress == 0.5f);
        slotModel.Cooling = null;
        Check("a null binding clears the finished cooldown", slotLayout.Find<ItemSlot>("cannon")?.Progress == null);

        var bound = new MenuModel();
        var form = UiMarkup.Load("""
            { "type": "StackPanel", "children": [
              { "type": "Slider", "name": "volume", "minimum": 0, "maximum": 100, "bind": { "value": { "path": "Volume", "mode": "TwoWay" } } },
              { "type": "Slider", "name": "oneWay", "minimum": 0, "maximum": 100, "bind": { "value": "Volume" } },
              { "type": "Slider", "name": "once", "minimum": 0, "maximum": 100, "bind": { "value": { "path": "Volume", "mode": "OneTime" } } },
              { "type": "TextBlock", "name": "formatted", "bind": { "text": { "path": "Score", "format": "{0} pts" } } },
              { "type": "Button", "name": "go", "bind": { "command": "PlayCommand" } },
              { "type": "Button", "name": "shown", "bind": { "visibility": { "path": "IsHidden", "invert": true } } },
              { "type": "TextBlock", "name": "nested", "bind": { "text": "Child.Label" } },
              { "type": "StackPanel", "context": "Items[1]", "children": [ { "type": "TextBlock", "name": "scoped", "bind": { "text": "Label" } } ] },
              { "type": "TextBlock", "name": "plain", "bind": { "text": "Unannounced" } }
            ] }
            """, theme, bound);
        Check("a layout of every binding kind loads clean", form.Problems.Count == 0, string.Join("; ", form.Problems));
        var formRoot = new UiRoot(fonts) { Content = form.Root };
        formRoot.UpdateLayout(new Vector2(400, 400));

        bound.Volume = 30;
        Check("one way: the view model drives the control", form.Find<Slider>("oneWay")?.Value == 30.0f, $"{form.Find<Slider>("oneWay")?.Value}");
        Check("one time: the control keeps what it first read", form.Find<Slider>("once")?.Value == 0.0f, $"{form.Find<Slider>("once")?.Value}");
        form.Find<Slider>("volume")!.Value = 70;
        Check("two way: an edit writes back to the view model", bound.Volume == 70.0f, $"{bound.Volume}");
        Check("and from there reaches everything else bound to it", form.Find<Slider>("oneWay")?.Value == 70.0f);
        form.Find<Slider>("oneWay")!.Value = 10;
        Check("one way: an edit does not write back", bound.Volume == 70.0f, $"{bound.Volume}");

        bound.Score = 7;
        Check("a format shapes the text", form.Find<TextBlock>("formatted")?.Text == "7 pts", form.Find<TextBlock>("formatted")?.Text ?? "");
        Check("a command binding reaches the button", form.Find<Button>("go")?.Command == bound.PlayCommand);
        bound.CanPlay = false;
        bound.PlayCommand.NotifyCanExecuteChanged();
        Check("and CanExecute decides whether it is enabled", form.Find<Button>("go")?.IsEnabled == false);
        Check("a bool drives visibility, inverted here", form.Find<Button>("shown")?.Visibility == Visibility.Visible);
        bound.IsHidden = true;
        Check("and follows its changes", form.Find<Button>("shown")?.Visibility == Visibility.Collapsed);

        Check("a path reads through objects", form.Find<TextBlock>("nested")?.Text == "first", form.Find<TextBlock>("nested")?.Text ?? "");
        bound.Child.Label = "second";
        Check("a change at the end of a path is heard", form.Find<TextBlock>("nested")?.Text == "second");
        var old = bound.Child;
        bound.Child = new MenuChild { Label = "third" };
        Check("so is one in the middle", form.Find<TextBlock>("nested")?.Text == "third");
        old.Label = "stale";
        Check("and the object it replaced is no longer listened to", form.Find<TextBlock>("nested")?.Text == "third");
        Check("a context makes paths below it relative, through an index", form.Find<TextBlock>("scoped")?.Text == "b", form.Find<TextBlock>("scoped")?.Text ?? "");

        bound.Unannounced = "polled";
        Check("a property that cannot announce waits for the frame", form.Find<TextBlock>("plain")?.Text != "polled");
        var source = new PlainSource { Label = "x" };
        var polled = UiMarkup.Load("""{ "type": "TextBlock", "name": "t", "bind": { "text": "Label" } }""", theme, source);
        var polledRoot = new UiRoot(fonts) { Content = polled.Root };
        polledRoot.UpdateLayout(new Vector2(100, 100));
        source.Label = "y";
        polledRoot.UpdateLayout(new Vector2(100, 100));
        Check("a source with no change notification is read each frame, as a fallback", polled.Find<TextBlock>("t")?.Text == "y");

        var wrong = UiMarkup.Load("""
            { "type": "StackPanel", "children": [
              { "type": "TextBlock", "bind": { "text": "Nope" } },
              { "type": "Button", "on": { "click": "NopeCommand" } },
              { "type": "TextBlock", "bind": { "text": { "path": "Score", "mode": "TwoWay" } } }
            ] }
            """, theme, viewModelType: typeof(MenuModel));
        Check("a path the view model does not have is reported, from its type alone, and an edit that cannot write back", wrong.Problems.Count == 3, string.Join("; ", wrong.Problems));

        var typed = UiMarkup.Load("""{ "type": "Slider", "name": "s", "bind": { "value": { "path": "Volume", "mode": "TwoWay" } } }""");
        Check("with no view model at all, bindings wait", typed.Problems.Count == 0);

        // Values an editor made hold their CLR type, unlike parsed ones; the preview must read them the same.
        var edited = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "Canvas",
            ["children"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "Border",
                ["name"] = "box",
                ["width"] = 40.0,
                ["margin"] = new System.Text.Json.Nodes.JsonArray(1.0, 2.0),
                ["canvas"] = new System.Text.Json.Nodes.JsonObject { ["left"] = 12.0, ["top"] = 34.5 }
            })
        };
        var fromEditor = UiMarkup.Load(edited);
        Check("values made in memory load like values parsed from a file",
            fromEditor.Problems.Count == 0 && fromEditor.Find<Border>("box") is { Width: 40.0f, LayoutSlotOrNull: { CanvasLeft: 12.0f, CanvasTop: 34.5f } },
            string.Join("; ", fromEditor.Problems));

        var broken = UiMarkup.Load("""{ "type": "StackPanel", "children": [ { "type": "Nope" }, { "type": "TextBlock", "colour": "red", "fontSize": "big" } ] }""");
        Check("what cannot be applied is reported, and the rest still builds",
            broken.Root is StackPanel { Children.Count: 1 } && broken.Problems.Count == 3, string.Join("; ", broken.Problems));
        Check("and a file that is not JSON says so", UiMarkup.Load("{ nope").Problems.Count == 1);

        var unbound = UiMarkup.Load(Menu, theme);
        Check("a layout loads with nothing behind it, showing what it was designed with",
            unbound.Problems.Count == 0 && unbound.Find<TextBlock>("score")?.Text == "0");
    }
}
