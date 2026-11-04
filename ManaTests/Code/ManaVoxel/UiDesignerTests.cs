using System.Text.Json.Nodes;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The UI Designer: the sample layout loads clean, a click in the preview selects what it lands on, and
/// adding, moving and deleting elements edit the JSON the preview is built from.
/// </summary>
public static class UiDesignerChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var sample = UiDesignerDocument.List().FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == "Main Menu");
        Check("the sample layout ships in Content/UI/", sample != null, UiDesignerDocument.ResolveFolder());
        if (sample == null || !UiDesignerDocument.TryOpen(sample, out var error))
        {
            return;
        }

        var loaded = UiMarkup.Load(File.ReadAllText(sample));
        Check("and loads with nothing it could not apply", loaded.Root != null && loaded.Problems.Count == 0, string.Join("; ", loaded.Problems));

        var root = new UiRoot(fonts);
        root.Content = UiDesignerManaView.Build(root.Theme);
        root.UpdateLayout(new Vector2(1200, 700));
        root.UpdateLayout(new Vector2(1200, 700));
        var preview = Find<UiDesignerPreview>(root.Content).FirstOrDefault();
        var play = preview == null ? null : Find<Button>(preview).FirstOrDefault(button => button.Name == "play");
        Check("the designer builds the layout into its preview", play != null && play.Bounds.Width > 0.0f);
        if (play == null)
        {
            return;
        }

        var playAt = preview!.ToPanel(play.Bounds).Center;
        root.Input.InjectPointerMoved(playAt);
        root.Input.InjectPointerPressed(playAt, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(playAt, PointerButton.Left);
        Check("a click in the preview selects the element under it", UiDesignerDocument.Selected?["name"]?.GetValue<string>() == "play",
            UiDesignerDocument.Selected?.ToJsonString() ?? "nothing");

        var stack = UiDesignerDocument.ParentOf(UiDesignerDocument.Selected!)!;
        var before = UiDesignerDocument.ChildrenOf(stack).Count;
        var added = UiDesignerDocument.Add("TextBlock");
        Check("adding beside a button puts the new element after it, in the same panel",
            UiDesignerDocument.ChildrenOf(stack).Count == before + 1 && UiDesignerDocument.ChildrenOf(stack).IndexOf(added) == 3);

        UiDesignerDocument.Move(added, -1);
        Check("moving it up swaps it with the one before", UiDesignerDocument.ChildrenOf(stack).IndexOf(added) == 2);

        UiDesignerDocument.Set(added, "text", JsonValue.Create("Hello"));
        root.UpdateLayout(new Vector2(1200, 700));
        Check("an edit shows in the preview", Find<TextBlock>(preview!).Any(text => text.Text == "Hello"));

        UiDesignerDocument.Delete(added);
        Check("and deleting it takes it out again, selecting its parent",
            UiDesignerDocument.ChildrenOf(stack).Count == before && ReferenceEquals(UiDesignerDocument.Selected, stack));

        var border = UiDesignerDocument.Root;
        UiDesignerDocument.Select(border);
        var intoBorder = UiDesignerDocument.Add("TextBlock");
        Check("a border that already has its child takes nothing more", !ReferenceEquals(border["child"], intoBorder) && border["child"] is JsonObject);

        var drawn = root.Render().InstanceCount;
        Check("the designer lays out and draws", drawn > 60, $"{drawn} instances");

        Gestures(root, preview!, Check);
        Files(Check);
        Bindings(Check);
        Picker(root, Check);
        EveryProperty(Check);
        BindButton(root, Check);
    }

    private sealed class EveryTypeViewModel
    {
        public string Words { get; set; } = "1";
        public float Number { get; set; } = 3.0f;
        public int Whole { get; set; } = 1;
        public bool Flag { get; set; } = true;
        public List<string> Names { get; set; } = ["a", "b"];
        public System.Windows.Input.ICommand Go { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        public string Fixed => "read only";
    }

    // Every control's every property: a view model can actually drive it, and the designer offers it something that fits.
    private static void EveryProperty(Action<string, bool, string> check)
    {
        var model = new EveryTypeViewModel();
        var failed = new List<string>();
        var driven = 0;
        foreach (var type in UiMarkup.Types.Values)
        {
            foreach (var property in UiMarkup.BindableProperties(type))
            {
                var key = UiMarkup.KeyFor(property.Name);
                var source = typeof(EveryTypeViewModel).GetProperties().FirstOrDefault(item => item.Name != "Fixed" && UiMarkup.CanBind(item.PropertyType, property.PropertyType));
                if (source == null)
                {
                    continue;
                }

                try
                {
                    var bound = UiMarkup.Load($$"""{ "type": "{{type.Name}}", "bind": { "{{key}}": "{{source.Name}}" } }""", new UiTheme(), model);
                    if (bound.Root == null || bound.Problems.Count > 0)
                    {
                        failed.Add($"{type.Name}.{key}: {string.Join("; ", bound.Problems)}");
                    }

                    driven++;
                }
                catch (Exception exception)
                {
                    failed.Add($"{type.Name}.{key} from {source.Name}: {(exception.InnerException ?? exception).Message}");
                }
            }
        }

        check("every property of every control takes a binding", failed.Count == 0 && driven > 200, $"{driven} bound; {string.Join(" | ", failed.Take(12))}");
        check("a button's text is one of them", UiDesignerBindings.Targets(typeof(Button)).Contains("text"), "");
        check("so are a button's command and its parameter", UiDesignerBindings.Targets(typeof(Button)).Contains("commandParameter"), "");
        check("but not what only code can supply", !UiDesignerBindings.Targets(typeof(Button)).Contains("icon"), "");

        var text = UiMarkup.Load("""{ "type": "Button", "bind": { "text": "Words", "isEnabled": "Flag", "margin": "Number" } }""", new UiTheme(), model);
        var button = text.Root as Button;
        check("a bound button's text follows the view model", button?.Text == "1" && button.IsEnabled && button.Margin.Left == 3.0f, string.Join("; ", text.Problems));

        var placed = UiMarkup.Load("""{ "type": "Canvas", "children": [ { "type": "Border", "name": "one", "bind": { "canvas.left": "Number", "grid.row": "Whole", "zIndex": "Whole", "dock": "Words" } } ] }""", new UiTheme(), new EveryTypeViewModel { Words = "Top" });
        var slot = placed.Named["one"].LayoutSlotOrNull;
        check("a place in the parent can be bound too", slot is { CanvasLeft: 3.0f, GridRow: 1, ZIndex: 1, Dock: Dock.Top }, string.Join("; ", placed.Problems));

        var grid = UiMarkup.Load("""{ "type": "Grid", "bind": { "columns": "Words" } }""", new UiTheme(), new EveryTypeViewModel { Words = "*, *" });
        check("and a grid's columns", grid.Root is Grid { ColumnDefinitions.Count: 2 }, string.Join("; ", grid.Problems));

        var combo = UiMarkup.Load("""{ "type": "ComboBox", "bind": { "selectedIndex": "Whole", "itemsSource": "Names" } }""", new UiTheme(), model);
        check("a list fills a ComboBox, and its selection waits for the items", combo.Root is ComboBox { SelectedIndex: 1, Items.Count: 2 }, string.Join("; ", combo.Problems));

        var bad = UiMarkup.Load("""{ "type": "Border", "bind": { "opacity": "Words" } }""", new UiTheme(), model);
        check("text cannot drive a number, and the layout says so", bad.Problems.Count == 1 && bad.Problems[0].Contains("cannot drive"), string.Join("; ", bad.Problems));
        var readOnly = UiMarkup.Load("""{ "type": "TextBox", "bind": { "text": { "path": "Fixed", "mode": "TwoWay" } } }""", new UiTheme(), model);
        check("nor can an edit write back to what has no setter", readOnly.Problems.Count == 1 && readOnly.Problems[0].Contains("no setter"), string.Join("; ", readOnly.Problems));
        check("a control says which targets flow back", UiMarkup.CanWriteBack(typeof(SliderBox), "value") && UiMarkup.CanWriteBack(typeof(NumericBox), "text") && !UiMarkup.CanWriteBack(typeof(TextBlock), "text"), "");

        var opacity = UiDesignerBindings.Groups(typeof(EveryTypeViewModel), UiDesignerPathKind.Property, typeof(float)).SelectMany(g => g.Members).Select(m => m.Name).ToList();
        check("the picker lists only what fits the property", opacity.Contains("Number") && opacity.Contains("Whole") && !opacity.Contains("Words") && !opacity.Contains("Flag"), string.Join(", ", opacity));
        var visible = UiDesignerBindings.Groups(typeof(EveryTypeViewModel), UiDesignerPathKind.Property, typeof(Visibility)).SelectMany(g => g.Members).Select(m => m.Name).ToList();
        check("visibility takes a flag", visible.Contains("Flag") && !visible.Contains("Number"), string.Join(", ", visible));
        var hud = UiDesignerBindings.Groups(typeof(Contour.View.ContourHudViewModel), UiDesignerPathKind.Property, typeof(System.Collections.IEnumerable)).SelectMany(g => g.Members).Select(m => m.Name).ToList();
        check("a list of view models is offered to what takes a list", hud.Contains("Weapons"), string.Join(", ", hud));

        var inverted = UiDesignerBindings.Write(UiDesignerBindings.Read(JsonValue.Create("Flag")) with { Invert = true });
        check("invert is written in the long form", inverted["invert"]?.GetValue<bool>() == true, inverted.ToJsonString());
        check("and read back, and removed", UiDesignerBindings.Read(inverted).Invert && UiDesignerBindings.Write(UiDesignerBindings.Read(inverted) with { Invert = false }) is JsonValue, "");
    }

    // The whole gesture: the row's own button, its picker, the row turned into a binding, and back.
    private static void BindButton(UiRoot root, Action<string, bool, string> check)
    {
        var size = new Vector2(1200, 900);
        void Layout()
        {
            root.UpdateLayout(size);
            root.UpdateLayout(size);
        }

        void Click(UiElement element)
        {
            root.Input.InjectPointerMoved(element.Bounds.Center);
            root.Input.InjectPointerPressed(element.Bounds.Center, PointerButton.Left, 0.0);
            root.Input.InjectPointerReleased(element.Bounds.Center, PointerButton.Left);
        }

        UiDesignerDocument.Replace((JsonObject)JsonNode.Parse("""
            { "format": "mana-ui", "id": "cc", "viewModel": "Contour", "root": { "type": "Grid", "children": [ { "type": "Button", "name": "go", "text": "Play" }, { "type": "TextBlock", "name": "blank", "bind": { "text": "Credits" } } ] } }
            """)!, null);
        check("the view model comes from the layout's game mode", UiDesignerDocument.ViewModelType == typeof(Contour.View.ContourHudViewModel), $"{UiDesignerDocument.ViewModelType}");
        UiDesignerDocument.Select(UiDesignerDocument.FindNamed("go"));
        Layout();
        var preview = Find<UiDesignerPreview>(root.Content!)[0];
        check("bound text with no value of its own shows its path in the preview", Find<TextBlock>(preview).Any(text => text.Text == "{Credits}"), "");

        var names = Find<Button>(root.Content!).Select(button => button.Name ?? string.Empty).Where(name => name.StartsWith("bind:")).ToList();
        check("every row has a bind button: text, placement and all", names.Contains("bind:text") && names.Contains("bind:grid.row") && names.Contains("bind:zIndex") && names.Contains("bind:visibility") && names.Contains("bind:command"), string.Join(", ", names));

        var bind = Find<Button>(root.Content!).First(button => button.Name == "bind:text");
        Click(bind);
        Layout();
        var rows = root.Popups.Open.SelectMany(Find<UiDesignerMemberRow>).ToList();
        var credits = rows.FirstOrDefault(row => row.Label == "Credits");
        check("the button opens the picker, and nothing is bound yet", credits != null && UiDesignerDocument.Selected?["bind"] == null, string.Join(", ", rows.Select(row => row.Label)));
        if (credits == null)
        {
            return;
        }

        Click(credits);
        Layout();
        var selected = UiDesignerDocument.Selected!;
        check("picking binds it", selected["bind"]?["text"]?.GetValue<string>() == "Credits", selected.ToJsonString());
        check("and leaves the file's text as the design value", selected["text"]?.GetValue<string>() == "Play", "");
        check("the row no longer offers to bind", !Find<Button>(root.Content!).Any(button => button.Name == "bind:text"), "");

        var remove = Find<Button>(root.Content!).First(button => button.Text == "x");
        Click(remove);
        Layout();
        check("removing it brings the plain row back", selected["bind"] == null && selected["text"]?.GetValue<string>() == "Play" && Find<Button>(root.Content!).Any(button => button.Name == "bind:text"), selected.ToJsonString());
    }

    private const string Sample = """
        { "format": "mana-ui", "version": 1, "id": "aa", "name": "Test", "root": {
          "type": "Grid", "name": "screen", "children": [
            { "type": "Border", "name": "box", "width": 200, "height": 100, "horizontalAlignment": "Left", "verticalAlignment": "Top", "margin": [100, 100, 0, 0] },
            { "type": "StackPanel", "name": "stack", "horizontalAlignment": "Right", "children": [ { "type": "TextBlock", "name": "line", "text": "x" } ] },
            { "type": "Border", "name": "card", "visibility": "Collapsed", "width": 300, "height": 200, "child": { "type": "TextBlock", "name": "inside", "text": "y" } }
          ] } }
        """;

    private static JsonObject Named(JsonObject node, string name)
    {
        if (node["name"]?.GetValue<string>() == name)
        {
            return node;
        }

        foreach (var child in UiDesignerDocument.Nested(node))
        {
            if (child["name"]?.GetValue<string>() == name || UiDesignerDocument.Nested(child).Any())
            {
                try
                {
                    return Named(child, name);
                }
                catch (KeyNotFoundException)
                {
                }
            }
        }

        throw new KeyNotFoundException(name);
    }

    private static void Gestures(UiRoot root, UiDesignerPreview preview, Action<string, bool, string> check)
    {
        var size = new Vector2(1200, 700);
        void Layout()
        {
            root.UpdateLayout(size);
            root.UpdateLayout(size);
        }

        void Drag(Vector2 from, Vector2 to)
        {
            root.Input.InjectPointerMoved(from);
            root.Input.InjectPointerPressed(from, PointerButton.Left, 0.0);
            root.Input.InjectPointerMoved((from + to) * 0.5f);
            Layout();
            root.Input.InjectPointerMoved(to);
            Layout();
            root.Input.InjectPointerReleased(to, PointerButton.Left);
            Layout();
        }

        UiElement Built(string name) => preview.Built?.Find<UiElement>(name) ?? throw new KeyNotFoundException(name);

        UiDesignerDocument.Replace((JsonObject)JsonNode.Parse(Sample)!, null);
        Layout();
        check("a file's wrapper is not an element: the outline starts at its root", UiDesignerDocument.Root["name"]?.GetValue<string>() == "screen", "");
        check("and an opened file has nothing to save or undo", !UiDesignerDocument.Dirty && !UiDesignerDocument.CanUndo, "");
        check("the screen is fitted into the panel", preview.FitScale is > 0.1f and < 1.0f, $"{preview.FitScale}");

        var box = Named(UiDesignerDocument.Root, "box");
        var start = Built("box").Bounds;
        var startPanel = preview.ToPanel(start);
        check("a point of the panel picks through the scale", ReferenceEquals(preview.PickAt(startPanel.Center), box), "");

        Drag(startPanel.Center, startPanel.Center + new Vector2(50, 30));
        var moved = Built("box").Bounds;
        var scale = preview.FitScale;
        check("dragging moves it, by the pointer's travel in the layout's units",
            MathF.Abs(moved.X - start.X - 50 / scale) < 1.5f && MathF.Abs(moved.Y - start.Y - 30 / scale) < 1.5f, $"{start} to {moved}");
        check("moving keeps its size", moved.Width == 200 && moved.Height == 100, $"{moved}");
        check("and marks the file changed", UiDesignerDocument.Dirty, "");

        // Handles are at the outline's corners; the last is bottom right.
        var movedPanel = preview.ToPanel(moved);
        var corner = new Vector2(movedPanel.Right, movedPanel.Bottom);
        Drag(corner, corner + new Vector2(20, 10));
        var resized = Built("box").Bounds;
        check("a corner handle resizes, leaving the far corner where it was",
            MathF.Abs(resized.Width - 200 - 20 / scale) < 1.5f && MathF.Abs(resized.Height - 100 - 10 / scale) < 1.5f
            && MathF.Abs(resized.X - moved.X) < 1.0f && MathF.Abs(resized.Y - moved.Y) < 1.0f, $"{moved} to {resized}");

        root.Input.InjectKeyDown(UiKey.Z, UiModifiers.Control);
        Layout();
        check("Ctrl+Z takes back the whole resize as one step", Built("box").Bounds.Width == 200 && UiDesignerDocument.CanRedo, $"{Built("box").Bounds}");
        UiDesignerDocument.Undo();
        Layout();
        check("and the next takes back the move", MathF.Abs(Built("box").Bounds.X - start.X) < 0.5f && !UiDesignerDocument.CanUndo, $"{Built("box").Bounds}");
        UiDesignerDocument.Redo();
        Layout();
        check("redo puts the move back", MathF.Abs(Built("box").Bounds.X - moved.X) < 0.5f, $"{Built("box").Bounds}");
        check("undo keeps the selection on the same element", UiDesignerDocument.Selected?["name"]?.GetValue<string>() == "box", "");

        var before = Built("box").Bounds;
        root.Input.InjectKeyDown(UiKey.Right, UiModifiers.Shift);
        Layout();
        check("Shift+arrow nudges ten", MathF.Abs(Built("box").Bounds.X - before.X - 10) < 0.5f, "");

        root.Input.InjectKeyDown(UiKey.D, UiModifiers.Control);
        Layout();
        var copy = UiDesignerDocument.Selected;
        check("Ctrl+D duplicates beside it and selects the copy", copy != null && !ReferenceEquals(copy, Named(UiDesignerDocument.Root, "box"))
            && UiDesignerDocument.ChildrenOf(UiDesignerDocument.Root).Count == 4, "");
        root.Input.InjectKeyDown(UiKey.Delete);
        Layout();
        check("Delete takes it out again", UiDesignerDocument.ChildrenOf(UiDesignerDocument.Root).Count == 3, "");

        // A stack places its children, so dragging one reports that rather than writing a margin that does nothing.
        var line = Named(UiDesignerDocument.Root, "line");
        UiDesignerDocument.Select(line);
        Layout();
        var lineAt = preview.ToPanel(Built("line").Bounds).Center;
        var lineJson = line.ToJsonString();
        Drag(lineAt, lineAt + new Vector2(0, 40));
        check("what a stack places is not moved by dragging", Named(UiDesignerDocument.Root, "line").ToJsonString() == lineJson && preview.Status.Contains("stack"), preview.Status);

        var card = Named(UiDesignerDocument.Root, "inside");
        UiDesignerDocument.Select(card);
        Layout();
        check("selecting what the file hides shows it, in what holds it", !Built("card").Bounds.IsEmpty && Built("card").Visibility == Visibility.Visible, $"{Built("card").Bounds}");
        check("while the file still hides it", Named(UiDesignerDocument.Root, "card")["visibility"]?.GetValue<string>() == "Collapsed", "");
        UiDesignerDocument.Select(Named(UiDesignerDocument.Root, "box"));
        Layout();
        check("and it goes again when another is selected", Built("card").Visibility == Visibility.Collapsed, "");

        var stack = Named(UiDesignerDocument.Root, "stack");
        check("nothing goes inside itself", !UiDesignerDocument.Reparent(stack, Named(UiDesignerDocument.Root, "line"), 0) && !UiDesignerDocument.Reparent(stack, stack, 0), "");

        var rows = Find<UiDesignerOutlineRow>(root.Content!);
        var boxRow = rows.First(row => row.Node["name"]?.GetValue<string>() == "box");
        var stackRow = rows.First(row => row.Node["name"]?.GetValue<string>() == "stack");
        Drag(boxRow.Bounds.Center, stackRow.Bounds.Center);
        check("dragging a row onto a panel in the outline moves it in", UiDesignerDocument.ChildrenOf(Named(UiDesignerDocument.Root, "stack")).Count == 2
            && UiDesignerDocument.ChildrenOf(UiDesignerDocument.Root).Count == 2, UiDesignerDocument.Root.ToJsonString());
        Layout();
        check("the outline follows", Find<UiDesignerOutlineRow>(root.Content!).Count(row => row.Node["name"]?.GetValue<string>() == "box") == 1, "");
    }

    private static void Files(Action<string, bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ManaUiDesignerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "Test.manaui");
            File.WriteAllText(file, Sample);
            UiDesignerDocument.TryOpen(file, out _);
            UiDesignerDocument.Set(UiDesignerDocument.Root, "opacity", JsonValue.Create(0.5));
            check("an edit is unsaved until saved", UiDesignerDocument.Dirty, "");
            UiDesignerDocument.Save();
            check("saving clears that", !UiDesignerDocument.Dirty, "");
            var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            check("the file keeps its wrapper, its id and the edit", saved["id"]?.GetValue<string>() == "aa" && saved["root"]?["opacity"]?.GetValue<double>() == 0.5, saved.ToJsonString());

            var copy = UiDesignerDocument.Save("Other");
            check("Save As writes a second asset with an id of its own", File.Exists(copy) && Path.GetDirectoryName(copy) == folder
                && UiDesignerDocument.Document["id"]?.GetValue<string>() != "aa" && File.Exists(file), copy);

            // The Project panel's New: the kind's blank, saved the way every asset kind is.
            var blank = Path.Combine(folder, "Blank.manaui");
            Mana.Code.Content.ManaContentAssets.Save(ManaVoxel.Code.Assets.ManaContentKinds.UiLayout.Blank!("Blank"), blank);
            check("a new layout from the Project panel opens in the designer", UiDesignerDocument.TryOpen(blank, out _)
                && UiDesignerDocument.TypeOf(UiDesignerDocument.Root) != null && UiDesignerDocument.Document["id"] != null, File.ReadAllText(blank));

            var bare = Path.Combine(folder, "Bare.manaui");
            File.WriteAllText(bare, """{ "type": "TextBlock", "name": "only", "text": "hi" }""");
            UiDesignerDocument.TryOpen(bare, out _);
            check("a bare element file opens as itself", UiDesignerDocument.Root["name"]?.GetValue<string>() == "only", "");
            UiDesignerDocument.Save();
            var wrapped = (JsonObject)JsonNode.Parse(File.ReadAllText(bare))!;
            check("and is saved as a proper asset, findable by id", wrapped["format"] != null && wrapped["id"] != null && wrapped["root"]?["name"]?.GetValue<string>() == "only", wrapped.ToJsonString());
            check("loading what was saved gives the same element", UiMarkup.Load(File.ReadAllText(bare)).Find<TextBlock>("only")?.Text == "hi", "");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Bindings(Action<string, bool, string> check)
    {
        check("a plain path stays the short form", UiDesignerBindings.Write(UiDesignerBindings.Read(JsonValue.Create("Score"))) is JsonValue, "");
        var twoWay = UiDesignerBindings.Read(JsonValue.Create("Volume")) with { Mode = BindingMode.TwoWay };
        var written = UiDesignerBindings.Write(twoWay);
        check("a mode needs the long form", written is JsonObject full && full["path"]?.GetValue<string>() == "Volume" && full["mode"]?.GetValue<string>() == "TwoWay", written.ToJsonString());
        var kept = UiDesignerBindings.Read(JsonNode.Parse("""{ "path": "A", "invert": true }"""));
        check("what the panel does not edit survives it", UiDesignerBindings.Write(kept with { Path = "B" })["invert"]?.GetValue<bool>() == true, "");
        check("a Button offers its command", UiDesignerBindings.Targets(typeof(Button)).Contains("command"), "");
        check("and its click", UiDesignerBindings.Events(typeof(Button)).Contains("click"), string.Join(", ", UiDesignerBindings.Events(typeof(Button))));

        var hud = typeof(Contour.View.ContourHudViewModel);
        var weapon = typeof(Contour.View.ContourWeaponViewModel);
        var groups = UiDesignerBindings.Groups(hud, UiDesignerPathKind.Property);
        check("properties are grouped by the view model they are on, its own first", groups.Select(g => g.ViewModel).SequenceEqual([hud.Name, nameof(Contour.View.ContourMenu), nameof(Contour.View.ContourMenuRow), weapon.Name]), string.Join(", ", groups.Select(g => g.ViewModel)));
        check("a group's members carry the path that reaches them", groups[0].Members.Any(m => m.Path == "CameraZoom") && groups[3].Members.Any(m => m.Path == "Weapons[0].Name" && m.Name == "Name"), "");
        check("a list of view models is stepped into, not offered as a value", groups[0].Members.All(m => m.Name != "Weapons"), "");
        var commands = UiDesignerBindings.Groups(hud, UiDesignerPathKind.Command);
        check("commands are offered apart from properties", commands[0].Members.Any(m => m.Path == "ResumeCommand") && groups.SelectMany(g => g.Members).All(m => m.Name != "ResumeCommand"), "");
        var contexts = UiDesignerBindings.Groups(hud, UiDesignerPathKind.Context).SelectMany(g => g.Members).ToList();
        check("a context offers only what can be stepped into", contexts.Select(m => m.Path).Order().SequenceEqual(["Menu", "Menu.Rows[0]", "Weapons[0]"]), string.Join(", ", contexts.Select(m => m.Path)));

        UiDesignerDocument.Replace((JsonObject)JsonNode.Parse("""
            { "format": "mana-ui", "id": "bb", "root": { "type": "StackPanel", "context": "Weapons[1]", "children": [ { "type": "TextBlock", "name": "inner" }, { "type": "Border", "name": "lost", "context": "AbandonLabel" } ] } }
            """)!, null);
        var inner = UiDesignerDocument.FindNamed("inner")!;
        check("under a context, paths are relative to what it holds", UiDesignerBindings.ScopeOf(hud, inner, includeOwn: true) == weapon, "");
        check("a context's own path starts from what is above it", UiDesignerBindings.ScopeOf(hud, UiDesignerDocument.Root, includeOwn: false) == hud, "");
        check("a context that leads nowhere is known to", UiDesignerBindings.ScopeOf(hud, UiDesignerDocument.FindNamed("lost")!, includeOwn: true) == null, "");

        var unset = UiMarkup.Load("""{ "type": "Button", "bind": { "text": "" }, "on": { "click": "" } }""", new UiTheme(), null, hud);
        check("a binding with no path chosen yet is no problem", unset.Problems.Count == 0, string.Join("; ", unset.Problems));
    }

    private static void Picker(UiRoot root, Action<string, bool, string> check)
    {
        var size = new Vector2(1200, 700);
        string? picked = null;
        var anchor = Find<UiDesignerPreview>(root.Content!)[0];
        var popup = UiDesignerPathPicker.Show(root, anchor, typeof(Contour.View.ContourHudViewModel), UiDesignerPathKind.Property, "CameraZoom", path => picked = path);
        root.UpdateLayout(size);
        root.UpdateLayout(size);
        var rows = Find<UiDesignerMemberRow>(popup);
        check("the picker is wide enough to read, whatever opened it", popup.Bounds.Width >= 300.0f, $"{popup.Bounds.Width}");
        check("and heads each view model's members with its name", Find<TextBlock>(popup).Any(text => text.Text == "ContourWeaponViewModel"), "");

        var filter = Find<TextBox>(popup)[0];
        filter.Text = "credits";
        root.UpdateLayout(size);
        root.UpdateLayout(size);
        rows = Find<UiDesignerMemberRow>(popup);
        check("typing filters it", rows.Count == 1 && rows[0].Label == "Credits", string.Join(", ", rows.Select(row => row.Label)));
        if (rows.Count > 0)
        {
            root.Input.InjectPointerMoved(rows[0].Bounds.Center);
            root.Input.InjectPointerPressed(rows[0].Bounds.Center, PointerButton.Left, 0.0);
            root.Input.InjectPointerReleased(rows[0].Bounds.Center, PointerButton.Left);
        }

        check("clicking a row picks its path and closes", picked == "Credits" && !popup.IsOpen, $"{picked}");
    }

    private static List<T> Find<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        void Walk(UiElement current)
        {
            if (current is T typed)
            {
                found.Add(typed);
            }

            foreach (var child in current.VisualChildren)
            {
                Walk(child);
            }
        }

        Walk(element);
        return found;
    }
}
