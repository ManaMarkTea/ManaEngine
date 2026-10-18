using System.Numerics;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.UI.ManaUi;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi.Inspectors;
using Button = ManaUI.Button;

namespace ManaTests;

public static class MaterialPickerChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, string.Empty);
        static IEnumerable<UiElement> Walk(UiElement element)
        {
            yield return element;
            foreach (var child in element.VisualChildren)
            {
                foreach (var descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }
        var engine = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        var material = new ManaMaterial(engine);
        var root = new UiRoot(fonts);
        var button = MaterialPicker.Build(material, new InspectorContext(root.Theme, engine));
        root.Content = button;
        root.UpdateLayout(new Vector2(400, 500));
        root.Input.InjectPointerPressed(button.Bounds.Center, PointerButton.Left, 1.0);
        root.Input.InjectPointerReleased(button.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(400, 500));
        var popup = root.Popups.Open.LastOrDefault();
        Check("material button opens its picker", popup?.Child != null);
        if (popup?.Child is not { } body)
        {
            return;
        }
        var options = Walk(body).OfType<Button>().Where(b => b.Tooltip?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true).ToList();
        Check("starter material ZIPs offered", options.Count >= 3);
        Check("local material options have previews", Walk(body).OfType<UiImage>().Count(image => image.HasSource) >= 3);
        var filter = Walk(body).OfType<TextBox>().Single();
        filter.Text = "concrete";
        root.UpdateLayout(new Vector2(400, 500));
        var selected = Walk(body).OfType<Button>().SingleOrDefault(b => b.Tooltip == "Assets/Materials/concrete.zip");
        Check("filter narrows to a material", selected != null && Walk(body).OfType<Button>().Count() == 1);
        if (selected != null)
        {
            root.Input.InjectPointerPressed(selected.Bounds.Center, PointerButton.Left, 2.0);
            root.Input.InjectPointerReleased(selected.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(400, 500));
            Check("choosing assigns the complete material", material.SourceZip == "Assets/Materials/concrete.zip"
                && material.Albedo.TextureValue != null && material.Normal.TextureValue != null && material.Occlusion.TextureValue != null);
            Check("selection closes the popup", root.Popups.Open.Count == 0);
            Check("current material shown on the button", button is Button { Text: "concrete" });
        }
    }
}
