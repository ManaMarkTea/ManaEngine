using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI.ManaUi;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The ported scene inspector, over a real scene graph. Two things are actually being tested: that a
/// retained tree can follow a selection that changes silently, and that the editors write straight through
/// to nodes the engine owns.
/// </summary>
/// <summary>A subject that deliberately overrides Equals, to prove the switcher tracks identity.</summary>
public sealed class EquatableSubject
{
    public EquatableSubject(string name) => Name = name;

    public string Name { get; }

    public override bool Equals(object? obj) => obj is EquatableSubject other && other.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();
}

public static class InspectorChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world);

        var scene = new ManaScene(context) { Name = "Default Scene" };
        var first = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "First" };
        var second = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Second" };

        var lit = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Lit" };

        // The component constructor records its owner but does not attach itself, so the node's list is
        // populated explicitly -- same as the engine does at every other call site.
        var light = new ManaLightSourceComponent(lit) { Intensity = 2.0f, Enabled = true };
        lit.Components.Add(light);

        scene.Children.Add(first);
        scene.Children.Add(second);
        scene.Children.Add(lit);
        world.Scenes.Add(scene);

        UiElement? view = null;
        try
        {
            view = SceneInspectorManaView.Build(root.Theme, world.Selection);
            Check("inspector constructs over a real world", true);
        }
        catch (Exception ex)
        {
            Check("inspector constructs over a real world", false, ex.Message);
            return;
        }

        root.Content = view;
        root.UpdateLayout(new Vector2(420, 700));

        // ---------------------------------------------------------------- empty state
        Check("nothing selected renders a placeholder", root.Render().InstanceCount > 0,
            $"{root.Render().InstanceCount} instances");
        Check("no editors exist yet", FindAll<VectorBox>(view).Count == 0,
            $"{FindAll<VectorBox>(view).Count} vector rows");

        // ---------------------------------------------------------------- following the selection
        // Nothing announces this. The selection is a plain object shared with the ImGui panels, so the
        // inspector has to notice on its own -- which is the whole reason DynamicContent exists.
        world.Selection.SelectedNode = first;
        root.UpdateLayout(new Vector2(420, 700));

        var vectors = FindAll<VectorBox>(view);
        Check("selecting a node builds the transform editors", vectors.Count == 3,
            $"{vectors.Count} vector rows (position, rotation, scale)");

        var nameBox = FindAll<TextBox>(view).FirstOrDefault(box => box is not NumericBox);
        Check("the name field shows the node's name", nameBox?.Text == "First", nameBox?.Text ?? "null");

        // ---------------------------------------------------------------- writes reach the node
        first.Position = Vector3.Zero;
        root.UpdateLayout(new Vector2(420, 700));

        var position = vectors[0];
        position.Value = new Vector3(3.0f, -2.0f, 0.5f);
        Check("editing position writes to the node", first.Position == new Vector3(3.0f, -2.0f, 0.5f),
            first.Position.ToString());

        var scale = vectors[2];
        scale.Value = new Vector3(2.0f, 2.0f, 2.0f);
        Check("editing scale writes to the node", first.Scale == new Vector3(2.0f, 2.0f, 2.0f),
            first.Scale.ToString());

        nameBox!.Text = "Renamed";
        nameBox.Focus();
        root.Input.InjectKeyDown(UiKey.Enter);
        Check("committing the name writes to the node", first.Name == "Renamed", first.Name ?? "null");
        root.FocusManager.Focus(null);

        // Rotation is stored in radians and shown in degrees, so it is the one that round-trips through a
        // conversion rather than binding straight through.
        var rotation = vectors[1];
        rotation.Value = new Vector3(0.0f, 90.0f, 0.0f);
        Check("editing rotation converts to radians",
            MathF.Abs(first.Rotation.Y - MathF.PI * 0.5f) < 0.001f, $"{first.Rotation.Y:F4} rad");

        // ---------------------------------------------------------------- the other direction
        // A gizmo or an animation moves a node without telling anyone, so the editors poll rather than
        // reading once. An inspector showing a stale transform is worse than one showing none.
        first.Position = new Vector3(9.0f, 9.0f, 9.0f);
        root.UpdateLayout(new Vector2(420, 700));
        Check("a node moved elsewhere updates the editor",
            position.Value == new Vector3(9.0f, 9.0f, 9.0f), position.Value.ToString());

        // ---------------------------------------------------------------- switching selection
        second.Position = new Vector3(-1.0f, -1.0f, -1.0f);
        world.Selection.SelectedNode = second;
        root.UpdateLayout(new Vector2(420, 700));

        var rebuilt = FindAll<VectorBox>(view);
        Check("switching selection rebuilds the editors",
            !ReferenceEquals(rebuilt[0], position), "different instances");
        Check("the new editors show the new node",
            rebuilt[0].Value == new Vector3(-1.0f, -1.0f, -1.0f), rebuilt[0].Value.ToString());

        // The old editors are gone, so writing to the new ones must not touch the old node.
        var beforeFirst = first.Position;
        rebuilt[0].Value = new Vector3(5.0f, 5.0f, 5.0f);
        Check("editing the new node leaves the old one alone", first.Position == beforeFirst,
            $"{first.Position} vs {beforeFirst}");

        // Selecting the same node again must not rebuild -- that would throw away focus and scroll
        // position on every frame the selection merely stayed put.
        var stable = FindAll<VectorBox>(view)[0];
        world.Selection.SelectedNode = second;
        root.UpdateLayout(new Vector2(420, 700));
        Check("re-selecting the same node does not rebuild",
            ReferenceEquals(FindAll<VectorBox>(view)[0], stable), "same instance");

        // ---------------------------------------------------------------- identity, not equality
        // Two distinct sources that compare equal are still two different things to inspect. Polled
        // bindings skip a write when the new value Equals the old, so routing the source object straight
        // through one would silently refuse to switch between these.
        var equalA = new EquatableSubject("Same");
        var equalB = new EquatableSubject("Same");
        var subject = equalA;
        var builds = 0;

        var switcher = new DynamicContent()
            .Showing<EquatableSubject>(
                () => subject,
                value =>
                {
                    builds++;
                    return new TextBlock().Text(value.Name);
                });

        root.Content = null;
        root.Content = switcher;
        root.UpdateLayout(new Vector2(300, 200));

        var afterFirst = builds;
        Check("subjects compare equal by value", equalA.Equals(equalB), "Equals overridden");

        subject = equalB;
        root.UpdateLayout(new Vector2(300, 200));
        Check("an equal-but-different source still rebuilds", builds > afterFirst,
            $"{afterFirst} builds before, {builds} after");

        var afterSwitch = builds;
        root.UpdateLayout(new Vector2(300, 200));
        root.UpdateLayout(new Vector2(300, 200));
        Check("an unchanged source does not rebuild", builds == afterSwitch,
            $"{builds} builds after two idle frames, {afterSwitch} expected");

        root.Content = null;
        root.Content = view;
        root.UpdateLayout(new Vector2(420, 700));

        // ---------------------------------------------------------------- components
        world.Selection.SelectedNode = lit;
        root.UpdateLayout(new Vector2(420, 700));

        var expanders = FindAll<Expander>(view);
        Check("a node with a component gets a third section", expanders.Count == 3,
            string.Join(", ", expanders.Select(e => e.Header)));
        Check("the component section is named", expanders.Any(e => e.Header == "Light Source"),
            string.Join(", ", expanders.Select(e => e.Header)));

        var intensity = FindAll<NumericBox>(view)
            .FirstOrDefault(box => MathF.Abs(box.Value - 2.0f) < 0.001f);
        Check("the light's intensity is shown", intensity != null, $"{intensity?.Value}");

        if (intensity != null)
        {
            intensity.Value = 5.0f;
            Check("editing intensity writes to the component",
                MathF.Abs(light.Intensity - 5.0f) < 0.001f, $"{light.Intensity}");
        }

        var toggle = FindAll<CheckBox>(view).FirstOrDefault();
        if (toggle != null)
        {
            toggle.IsChecked = false;
            Check("toggling the light writes to the component", !light.Enabled, $"{light.Enabled}");
        }

        // ---------------------------------------------------------------- rendering
        Check("the inspector renders", root.Render().InstanceCount > 20,
            $"{root.Render().InstanceCount} instances");
        Check("still one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // Collapsing a section must not lose the editors inside it.
        var transform = expanders.First(e => e.Header == "Transform");
        transform.IsExpanded = false;
        root.UpdateLayout(new Vector2(420, 700));
        Check("collapsing a section keeps its editors alive", FindAll<VectorBox>(view).Count == 4,
            $"{FindAll<VectorBox>(view).Count} vector rows (3 transform + 1 light direction)");

        transform.IsExpanded = true;
        root.UpdateLayout(new Vector2(420, 700));
        Check("re-expanding still renders", root.Render().InstanceCount > 20,
            $"{root.Render().InstanceCount} instances");

        // ---------------------------------------------------------------- material and uniforms
        // A mesh renderer carries a material, and the material carries whatever uniforms its shader
        // declares. Both are ported from the ImGui inspector, so the shapes have to agree.
        var meshNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Meshy" };
        var mesh = new ManaCube(context, Vector4.One, Vector3.One);
        var meshRenderer = new MeshRendererComponent(meshNode, mesh);
        meshNode.Components.Add(meshRenderer);
        scene.Children.Add(meshNode);

        var material = meshRenderer.Material;
        material.Name = "Test Material";
        material.SetShaderUniform("uStrength", 0.5f);
        material.SetShaderUniform("uTintColor", new Vector3(1.0f, 0.0f, 0.0f));
        material.SetShaderUniform("uSteps", 4);
        material.SetShaderUniform("uEnabled", true);

        world.Selection.SelectedNode = meshNode;
        root.UpdateLayout(new Vector2(420, 900));

        var sections = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("the material gets its own section", sections.Contains("Material"),
            string.Join(", ", sections));
        Check("the material is a sibling, not nested in the renderer",
            sections.IndexOf("Material") > sections.IndexOf("Mesh Renderer"),
            string.Join(", ", sections));

        var uniformSection = FindAll<Expander>(view)
            .FirstOrDefault(e => e.Header.StartsWith("Shader Uniforms", StringComparison.Ordinal));
        Check("editable uniforms get a section", uniformSection != null,
            string.Join(", ", sections));
        Check("the section counts them", uniformSection?.Header == "Shader Uniforms (4)",
            uniformSection?.Header ?? "none");

        // One editor per type, and a name that looks like a colour gets a swatch rather than three numbers
        // -- the same heuristic the ImGui inspector uses, so the two panels agree about what a uniform is.
        Check("a bool uniform gets a checkbox", FindAll<CheckBox>(view).Count >= 3,
            $"{FindAll<CheckBox>(view).Count} checkboxes");
        Check("a colour-named uniform gets a swatch", FindAll<ColorField>(view).Count >= 1,
            $"{FindAll<ColorField>(view).Count} colour fields");

        // Editing writes into the uniform the renderer will actually submit.
        var strengthBox = FindAll<NumericBox>(view)
            .FirstOrDefault(box => MathF.Abs(box.Value - 0.5f) < 0.001f);
        Check("a float uniform is shown", strengthBox != null, $"{strengthBox?.Value}");

        if (strengthBox != null)
        {
            strengthBox.Value = 0.8f;
            Check("editing a uniform writes to the material",
                MathF.Abs(material.ShaderUniforms["uStrength"].FloatValue - 0.8f) < 0.001f,
                $"{material.ShaderUniforms["uStrength"].FloatValue}");
        }

        // Reset restores from the snapshot rather than removing keys -- some render paths assign buffer
        // slots by sorting the uniform names, so a dropped key would shift every other uniform.
        var keysBefore = material.ShaderUniforms.Count;
        var reset = FindAll<Button>(view).FirstOrDefault(b => b.Text == "Reset to defaults");
        Check("a reset button is offered", reset != null);

        // Clicked for real rather than raising the event, so the button's own gesture is covered too.
        if (reset != null)
        {
            root.UpdateLayout(new Vector2(420, 900));
            root.Input.InjectPointerMoved(reset.Bounds.Center);
            root.Input.InjectPointerPressed(reset.Bounds.Center, PointerButton.Left, 5.0);
            root.Input.InjectPointerReleased(reset.Bounds.Center, PointerButton.Left);
        }

        Check("reset keeps every key", material.ShaderUniforms.Count == keysBefore,
            $"{material.ShaderUniforms.Count} vs {keysBefore}");

        Check("the material section renders", root.Render().InstanceCount > 40,
            $"{root.Render().InstanceCount} instances");

        // ---------------------------------------------------------------- texture channels
        // A material with no maps must not get an empty section to scroll past.
        Check("a flat material has no textures section",
            !FindAll<Expander>(view).Any(e => e.Header.StartsWith("Textures", StringComparison.Ordinal)),
            string.Join(", ", FindAll<Expander>(view).Select(e => e.Header)));

        // A thumbnail is built from CPU pixels, so one is supplied the way a loader would.
        var map = new ManaTexture(context)
        {
            OriginalFilename = "albedo.png",
            Width = 4,
            Height = 4,
            PixelData = CheckerPixels(4, 4)
        };

        // Probed before the row exists, so the cache is known-good by the time the row asks. If the row
        // still shows nothing after this, the row is the problem rather than the thumbnail.
        var direct = ManaUiTexturePreviews.TryGet(root.Images, map, out var directPreview);
        Check("a thumbnail can be built from CPU pixels", direct,
            direct ? $"{directPreview.Size.X}x{directPreview.Size.Y}" : "TryGet returned false");

        material.Albedo.TextureValue = map;
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 900));
        world.Selection.SelectedNode = meshNode;
        root.UpdateLayout(new Vector2(420, 900));

        // A second pass on purpose. DynamicContent rebuilds the subtree from inside the polled refresh, so
        // the elements it creates are not in the list being refreshed -- their bindings first evaluate on
        // the following frame. Invisible at ninety frames a second; not invisible to a check that looks
        // immediately.
        root.UpdateLayout(new Vector2(420, 900));

        var textureSection = FindAll<Expander>(view)
            .FirstOrDefault(e => e.Header.StartsWith("Textures", StringComparison.Ordinal));
        Check("a material with a map gets a textures section", textureSection != null,
            string.Join(", ", FindAll<Expander>(view).Select(e => e.Header)));
        Check("the section counts the maps", textureSection?.Header == "Textures (1)",
            textureSection?.Header ?? "none");

        var previews = FindAll<UiImage>(view);
        Check("the map gets a preview", previews.Count == 1, $"{previews.Count} images");

        if (previews.Count == 1)
        {
            Check("the preview points at the colour atlas",
                previews[0].HasSource && previews[0].SourceSize.X > 0.0f,
                $"{previews[0].SourceSize} from {previews[0].SourceUv}");

            // The thumbnail is real pixels, not a plausible UV over blank atlas.
            var uv = previews[0].SourceUv;
            var px = (int)MathF.Round(uv.X * root.Images.Width);
            var py = (int)MathF.Round(uv.Y * root.Images.Height);
            var offset = (py * root.Images.Width + px) * 4;
            Check("the atlas holds the thumbnail", root.Images.Pixels[offset + 3] > 0,
                $"alpha {root.Images.Pixels[offset + 3]}");
        }

        // Decoding is expensive, so the result is cached rather than rebuilt on every rebuild of the panel.
        var before = root.Images.Pixels.Length;
        var firstUv = previews.Count == 1 ? previews[0].SourceUv : Vector4.Zero;
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 900));
        world.Selection.SelectedNode = meshNode;
        root.UpdateLayout(new Vector2(420, 900));

        // Again the extra pass: the rebuild happens inside the refresh, so the elements it creates
        // evaluate on the following one.
        root.UpdateLayout(new Vector2(420, 900));

        var rebuiltPreview = FindAll<UiImage>(view).FirstOrDefault();
        Check("rebuilding reuses the cached thumbnail",
            rebuiltPreview != null && rebuiltPreview.SourceUv == firstUv,
            "same atlas region");
        Check("and packs nothing new", root.Images.Pixels.Length == before, "atlas unchanged");

        material.Albedo.TextureValue = null;

        // ---------------------------------------------------------------- rebuilt mid-drag
        // The failure this reproduces: drag a position field, something clears the selection, the panel
        // rebuilds, and the element holding pointer capture is torn out of the tree mid-gesture. The
        // release then has nowhere to go, so the capture never clears -- and because the router keeps
        // reporting that the UI owns the pointer, every layer underneath stays suppressed and the whole
        // application stops responding to the mouse.
        world.Selection.SelectedNode = first;
        root.UpdateLayout(new Vector2(420, 700));

        var live = FindAll<VectorBox>(view)[0].Field(0);
        var strip = live.DragStripRect.Center;

        root.Input.InjectPointerMoved(strip);
        root.Input.InjectPointerPressed(strip, PointerButton.Left, 1.0);
        root.Input.InjectPointerMoved(strip + new Vector2(20.0f, 0.0f));
        Check("the field captured the pointer", root.PointerCaptureTarget == live,
            root.PointerCaptureTarget?.GetType().Name ?? "null");

        // Pulled out from under the drag.
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 700));

        Check("a destroyed capture target releases the pointer", root.PointerCaptureTarget == null,
            root.PointerCaptureTarget?.GetType().Name ?? "null");
        Check("and the UI stops claiming the pointer", !root.Input.IsPointerOverUi,
            $"hovered {root.Input.HoveredElement?.GetType().Name ?? "null"}");

        // The gesture must also be able to end without throwing, and leave the router usable.
        root.Input.InjectPointerReleased(strip + new Vector2(20.0f, 0.0f), PointerButton.Left);
        root.Input.InjectPointerMoved(strip);
        Check("input still works afterwards", root.PointerCaptureTarget == null, "recovered");

        // ---------------------------------------------------------------- deselecting
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 700));
        Check("deselecting returns to the placeholder", FindAll<VectorBox>(view).Count == 0,
            $"{FindAll<VectorBox>(view).Count} vector rows");
        Check("the placeholder still renders", root.Render().InstanceCount > 0,
            $"{root.Render().InstanceCount} instances");
    }

    /// <summary>Every element of a type beneath a root, in visual order.</summary>
    /// <summary>A tiny RGBA checkerboard, so a thumbnail has something recognisable in it.</summary>
    private static byte[] CheckerPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var light = (x + y) % 2 == 0;

                pixels[offset] = light ? (byte)220 : (byte)40;
                pixels[offset + 1] = light ? (byte)120 : (byte)60;
                pixels[offset + 2] = light ? (byte)40 : (byte)200;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static List<T> FindAll<T>(UiElement? element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;
    }

    private static void Walk<T>(UiElement? element, List<T> found) where T : UiElement
    {
        if (element == null)
        {
            return;
        }

        if (element is T match)
        {
            found.Add(match);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Walk(element.GetVisualChild(i), found);
        }
    }
}
