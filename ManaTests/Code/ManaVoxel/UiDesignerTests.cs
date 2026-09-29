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
        Check("the sample layout ships in UI/", sample != null, UiDesignerDocument.ResolveFolder());
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

        root.Input.InjectPointerMoved(play.Bounds.Center);
        root.Input.InjectPointerPressed(play.Bounds.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(play.Bounds.Center, PointerButton.Left);
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
