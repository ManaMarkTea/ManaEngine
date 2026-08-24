using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.UI;
using Mana.Code.UI.ManaUi;
using ManaUI;
using Mana.Code.Materials;
using ManaCore.Graphics;
using ManaVoxel.Code.Gameplay.Trains;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI.ManaUi;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

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

/// <summary>
/// A component nothing has written a widget for, which is what the reflection fallback is for. Deliberately
/// a mix: the types it should find, and one it should not.
/// </summary>
public sealed class SampleComponent : ManaComponent
{
    public SampleComponent(ManaSceneNode owner) : base(owner)
    {
    }

    public enum Mode
    {
        Off,
        Gentle,
        Violent
    }

    public float Strength { get; set; } = 0.25f;
    public bool Loops { get; set; } = true;
    public int Repeats { get; set; } = 3;
    public string Caption { get; set; } = "hello";
    public Vector3 Offset { get; set; } = new(1.0f, 2.0f, 3.0f);
    public Vector4 TintColor { get; set; } = new(1.0f, 0.0f, 0.0f, 1.0f);
    public Mode Behaviour { get; set; } = Mode.Gentle;

    /// <summary>Read-only, so there is nothing to edit and it must not be offered.</summary>
    public float Computed => Strength * Repeats;

    /// <summary>Not a type the fallback knows how to edit.</summary>
    public object? Payload { get; set; }
}

/// <summary>Counts its ticks, to catch a component being updated more than once a frame.</summary>
public sealed class CountingComponent : ManaComponent
{
    private readonly Action _onTick;

    public CountingComponent(ManaSceneNode owner, Action onTick) : base(owner)
    {
        _onTick = onTick;
    }

    public override void UpdateComponent()
    {
        _onTick();
    }
}

/// <summary>A widget an application might register to replace one of the defaults.</summary>
public sealed class StubTransformWidget : InspectorWidget<ManaSceneNode>
{
    protected override UiElement Build(ManaSceneNode subject, InspectorContext context)
    {
        return new TextBlock().Text("stub");
    }
}

public static class InspectorChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var world = new ManaWorld();
        // Uploads deferred, so shader construction stops short of touching a GPU that is not there.
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        context.Physics = new ManaPhysics(context);

        var scene = new ManaScene(context)
        {
            Name = "Default Scene",
            LevelPath = "C:/levels/default.manalevel"
        };
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
        scene.SavedLevelContentJson = ManaLevelCapture.CaptureSceneContentJson(world, scene, scene.Name);

        UiElement? view = null;
        try
        {
            view = SceneInspectorManaView.Build(root.Theme, world.Selection, context);
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

        // ---------------------------------------------------------------- scene metadata
        world.Selection.SelectedScene = scene;
        root.UpdateLayout(new Vector2(420, 700));

        var sceneText = string.Join(" | ", FindAll<TextBlock>(view).Select(t => t.Text));
        var sceneNameBox = FindAll<TextBox>(view).FirstOrDefault(box => box is not NumericBox);
        Check("selecting a scene builds scene metadata",
            sceneNameBox?.Text == "Default Scene" && sceneText.Contains("C:/levels/default.manalevel"),
            $"{sceneNameBox?.Text ?? "null"} | {sceneText}");
        Check("the scene inspector reports saved state",
            sceneText.Contains("Saved", StringComparison.OrdinalIgnoreCase),
            sceneText);

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

        // ---------------------------------------------------------------- the camera
        // A camera used to be a separate ImGui window, which made it the one thing in the scene you could
        // not simply select and look at. It is a widget now, so it appears when its node is selected.
        //
        // Only its registration is checked here: a CameraComponent subscribes to the window's resize event
        // in its constructor, so one cannot be built without a window and this suite has none. The widget
        // itself is exercised the same way every other one is -- by being in the registry.
        Check("a camera has a widget registered for it",
            InspectorWidgetFactory.CreateDefault().Widgets.Any(w => w is CameraWidget),
            string.Join(", ", InspectorWidgetFactory.CreateDefault().Widgets.Select(w => w.GetType().Name)));

        // ---------------------------------------------------------------- reflected fallback
        // A component nobody has written a widget for used to get "no inspector for this component yet",
        // which is a dead end. Reflection over its own properties beats that: most components are a handful
        // of floats and flags, and a hand-written widget can still replace this whenever one is worth it.
        var oddNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Odd" };
        var sample = new SampleComponent(oddNode) { Strength = 0.75f };
        oddNode.Components.Add(sample);
        scene.Children.Add(oddNode);

        world.Selection.SelectedNode = oddNode;
        root.UpdateLayout(new Vector2(420, 700));

        var reflected = FindAll<Expander>(view)
            .FirstOrDefault(e => e.Header.Contains("SampleComponent", StringComparison.Ordinal));

        Check("an unknown component is reflected rather than refused", reflected != null,
            string.Join(", ", FindAll<Expander>(view).Select(e => e.Header)));
        Check("and says so in its header", reflected?.Header.Contains("reflected") == true,
            reflected?.Header ?? "none");

        Check("no placeholder is shown", !FindAll<TextBlock>(view)
            .Any(t => t.Text.Contains("No inspector", StringComparison.Ordinal)), "reflection covered it");

        // Every editable type, and nothing it cannot edit. Getting this wrong in either direction is the
        // failure mode of a reflected inspector: a missing row, or a row that does nothing.
        var labels = FindAll<TextBlock>(reflected).Select(t => t.Text).ToList();
        foreach (var expected in new[] { "Strength", "Loops", "Repeats", "Caption", "Offset", "Behaviour" })
        {
            var humanised = expected.ToLowerInvariant();
            Check($"{expected} is offered",
                labels.Any(l => l.Replace(" ", string.Empty).Equals(humanised, StringComparison.OrdinalIgnoreCase)),
                string.Join(", ", labels));
        }

        Check("a read-only property is not offered",
            !labels.Any(l => l.Contains("omputed", StringComparison.Ordinal)), string.Join(", ", labels));
        Check("nor one of a type it cannot edit",
            !labels.Any(l => l.Contains("ayload", StringComparison.Ordinal)), string.Join(", ", labels));

        // Inherited plumbing is skipped: every component has an Owner and a Position from the base type,
        // and listing those buries the two or three properties that make this component what it is.
        Check("inherited plumbing is skipped",
            !labels.Any(l => l.Contains("wner", StringComparison.Ordinal)), string.Join(", ", labels));

        // A reflected float gets the same slider-and-number editor a hand-written widget uses. Two
        // different editors for the same kind of value is the sort of seam that makes a panel feel
        // assembled rather than designed.
        Check("a reflected float uses the standard scalar editor",
            FindAll<SliderBox>(reflected).Count >= 1,
            $"{FindAll<SliderBox>(reflected).Count} sliders");

        // The rows are live in both directions, like every other editor here.
        var strength = FindAll<SliderBox>(reflected)
            .FirstOrDefault(b => MathF.Abs(b.Value - 0.75f) < 0.001f);
        Check("a reflected value is shown", strength != null,
            string.Join(", ", FindAll<SliderBox>(reflected).Select(b => b.Value.ToString("F2"))));

        if (strength != null)
        {
            strength.Value = 0.5f;
            Check("editing a reflected row writes to the component",
                MathF.Abs(sample.Strength - 0.5f) < 0.001f, sample.Strength.ToString("F3"));

            sample.Strength = 0.125f;
            root.UpdateLayout(new Vector2(420, 700));
            Check("and a change made elsewhere shows up",
                MathF.Abs(strength.Value - 0.125f) < 0.001f, strength.Value.ToString("F3"));
        }

        var caption = FindAll<TextBox>(reflected).FirstOrDefault(b => b is not NumericBox);
        Check("a string property gets a text box", caption?.Text == "hello", caption?.Text ?? "none");

        // A Vector4 named like a colour gets a swatch, matching what the shader-uniform editor does.
        Check("a colour-named vector gets a swatch", FindAll<ColorField>(reflected).Count > 0,
            $"{FindAll<ColorField>(reflected).Count} swatches");

        Check("an enum gets a drop down", FindAll<ComboBox>(reflected).Count == 1,
            $"{FindAll<ComboBox>(reflected).Count} combos");

        var behaviour = FindAll<ComboBox>(reflected).FirstOrDefault();
        if (behaviour != null)
        {
            Check("the drop down starts on the current value",
                behaviour.SelectedItem as string == "Gentle", behaviour.SelectedItem?.ToString() ?? "none");

            behaviour.SelectedIndex = 2;
            Check("and choosing writes the enum back",
                sample.Behaviour == SampleComponent.Mode.Violent, sample.Behaviour.ToString());
        }

        // ---------------------------------------------------------------- names and explanations
        // The label column is fixed, so narrowing the dock takes width from the editor rather than from
        // the name. A long name still ellipsises, so the full text is always on hover -- an inspector
        // where you cannot read what you are editing is no use.
        var labelBlocks = FindAll<TextBlock>(reflected).Where(t => t.Tooltip != null).ToList();
        Check("every property name carries its full text on hover", labelBlocks.Count >= 6,
            $"{labelBlocks.Count} labelled rows");

        Check("and the tooltip starts with the name itself",
            labelBlocks.All(t => t.Tooltip!.StartsWith(t.Text, StringComparison.Ordinal)),
            string.Join(" | ", labelBlocks.Take(3).Select(t => t.Tooltip!.Split('\n')[0])));

        // Straight from the source, so this stays honest if the comment is reworded.
        var documented = typeof(ManaPickupMotionComponent).GetProperty(nameof(ManaPickupMotionComponent.PhaseSeconds));
        Check("the doc reader finds a summary",
            documented != null && XmlDocSummaries.For(documented)?.Length > 0,
            XmlDocSummaries.For(documented!) ?? "none");

        // Multi-paragraph comments are the norm in this codebase, and reading Value alone would run the
        // sentences together and drop every <see cref> reference.
        var multiline = typeof(ManaSceneNode).GetProperty(nameof(ManaSceneNode.Rotation));
        var rotationDoc = multiline == null ? null : XmlDocSummaries.For(multiline);
        Check("a multi-paragraph comment keeps its paragraphs",
            rotationDoc != null && rotationDoc.Contains('\n'), rotationDoc?.Replace("\n", " / ") ?? "none");
        Check("without stacking up blank lines between them",
            rotationDoc?.Contains("\n\n", StringComparison.Ordinal) != true,
            rotationDoc?.Replace("\n", " / ") ?? "none");

        Check("and a cref is reduced to the name it points at",
            rotationDoc?.Contains("ManaPawnNode", StringComparison.Ordinal) == true
                && rotationDoc?.Contains("T:Mana", StringComparison.Ordinal) != true,
            rotationDoc?.Replace("\n", " / ") ?? "none");

        // An undocumented property is not an error -- it just gets the name and nothing more.
        var undocumented = typeof(SampleComponent).GetProperty(nameof(SampleComponent.Repeats));
        Check("an undocumented property simply has none",
            undocumented != null && XmlDocSummaries.For(undocumented) == null,
            XmlDocSummaries.For(undocumented!) ?? "none");

        // The whole point of a fallback is that it is replaceable.
        var noFallback = InspectorWidgetFactory.CreateDefault();
        noFallback.Fallback = null;
        Check("a factory with no fallback shows nothing for it",
            noFallback.BuildAll(sample, new InspectorContext(root.Theme, context, noFallback)).Count == 0,
            "opt out is available");

        // ---------------------------------------------------------------- the real thing
        // The pickup component the voxel editor puts on a donut. No widget exists for it and none should:
        // it is the standing proof that a component written without a thought for the UI still gets a
        // working editor. Checked here so that stays true as the component grows properties.
        var donut = new ManaSceneNode(context, Matrix4x4.CreateTranslation(new Vector3(60.0f, 1.5f, 60.0f)))
        {
            Name = "Pickup Donut"
        };

        var motion = new ManaPickupMotionComponent(donut);
        donut.Components.Add(motion);
        scene.Children.Add(donut);

        var registry = InspectorWidgetFactory.CreateDefault();
        Check("no hand-written widget claims the pickup",
            !registry.AllFor(motion).Any(),
            string.Join(", ", registry.AllFor(motion).Select(w => w.GetType().Name)));

        world.Selection.SelectedNode = donut;
        root.UpdateLayout(new Vector2(420, 700));

        var pickupSection = FindAll<Expander>(view)
            .FirstOrDefault(e => e.Header.Contains(nameof(ManaPickupMotionComponent), StringComparison.Ordinal));

        Check("but it still gets an inspector", pickupSection != null,
            string.Join(", ", FindAll<Expander>(view).Select(e => e.Header)));

        var pickupLabels = FindAll<TextBlock>(pickupSection).Select(t => t.Text).ToList();
        Check("with its spin and bob settings",
            pickupLabels.Any(l => l.Contains("Degrees per second", StringComparison.OrdinalIgnoreCase))
                && pickupLabels.Any(l => l.Contains("Bob height", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", pickupLabels));

        // A property's own doc comment becomes its tooltip, the way Unreal shows a UPROPERTY tooltip.
        // Read from the XML the compiler emits beside the assembly, so nothing is annotated twice.
        var bobHeight = FindAll<TextBlock>(pickupSection)
            .FirstOrDefault(t => t.Tooltip != null
                && t.Text.Contains("Bob height", StringComparison.OrdinalIgnoreCase));

        Check("a documented property explains itself on hover",
            bobHeight?.Tooltip?.Contains("rest position", StringComparison.OrdinalIgnoreCase) == true,
            bobHeight?.Tooltip?.Replace("\n", " / ") ?? "no tooltip");

        var degrees = FindAll<SliderBox>(pickupSection)
            .FirstOrDefault(b => MathF.Abs(b.Value - 90.0f) < 0.001f);

        Check("showing the live value", degrees != null, $"{degrees?.Value}");

        // The range is guessed from the value and rounded outwards, so 90 degrees per second gets a
        // sensible 0..100 track rather than the 0..1 a default would give.
        Check("with a range guessed from the value",
            degrees != null && MathF.Abs(degrees.Maximum - 100.0f) < 0.001f,
            $"0..{degrees?.Maximum}");

        if (degrees != null)
        {
            degrees.Value = 45.0f;
            Check("and editing it changes how fast the donut spins",
                MathF.Abs(motion.DegreesPerSecond - 45.0f) < 0.001f, motion.DegreesPerSecond.ToString("F1"));
        }

        // Bobbing moves the node, and the node is also editable in the Transform section above. Whichever
        // moved it last wins, so dragging a pickup somewhere else has to stick rather than being yanked
        // back to wherever it was first dropped.
        motion.Bobs = true;
        motion.UpdateComponent();
        donut.Position = new Vector3(10.0f, 5.0f, 10.0f);
        motion.UpdateComponent();
        motion.UpdateComponent();

        Check("moving a bobbing node re-homes it rather than snapping back",
            MathF.Abs(donut.Position.X - 10.0f) < 0.001f
                && MathF.Abs(donut.Position.Z - 10.0f) < 0.001f
                && MathF.Abs(donut.Position.Y - 5.0f) <= motion.BobHeight + 0.001f,
            donut.Position.ToString());

        // ---------------------------------------------------------------- no write-back from a poll
        // The bug this covers: select a node that is moving on its own and it moves at double speed.
        // The editors poll, so a self-animating node sets their value every frame; setting a control's
        // value raises the same event a keystroke does; and the transform handlers write back and call
        // UpdateTransform, which ticks every component on the node a second time. It also quantised the
        // node's position to the box's display precision just for being selected.
        var spun = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Spun" };
        var spin = new ManaPickupMotionComponent(spun) { Bobs = false, DegreesPerSecond = 90.0f };
        spun.Components.Add(spin);
        scene.Children.Add(spun);

        world.Selection.SelectedNode = spun;
        root.UpdateLayout(new Vector2(420, 700));

        // An awkward value on purpose: three decimals is what the position editor displays, so a
        // round-trip through it would round this off.
        spun.Position = new Vector3(1.23456f, 0.0f, 0.0f);
        root.UpdateLayout(new Vector2(420, 700));
        root.UpdateLayout(new Vector2(420, 700));

        Check("polling an editor does not write back through it",
            MathF.Abs(spun.Position.X - 1.23456f) < 0.000001f, spun.Position.X.ToString("F6"));

        var spinCalls = 0;
        var counter = new CountingComponent(spun, () => spinCalls++);
        spun.Components.Add(counter);

        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 700));
        world.Selection.SelectedNode = spun;
        root.UpdateLayout(new Vector2(420, 700));

        // A real frame's worth of time, so the pickup actually turns. With a zero delta nothing moves,
        // the polled bindings never fire, and this would pass whether or not the loop was closed.
        var clock = ManaTime.GlobalTime.DeltaTime;
        ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(1.0 / 60.0);

        try
        {
            // One frame: the scene ticks, then the UI lays out. The tick turns the node, so the rotation
            // editor's polled binding fires -- and an unguarded handler writes back and calls
            // UpdateTransform, which runs every component on the node a second time.
            spinCalls = 0;
            spun.UpdateTransform();
            root.UpdateLayout(new Vector2(420, 700));

            Check("a selected node's components tick once per frame", spinCalls == 1,
                $"{spinCalls} ticks in one frame");

            // Two frames of turning should advance by two frames, not four.
            var startAngle = spun.Rotation.Y;
            spun.UpdateTransform();
            root.UpdateLayout(new Vector2(420, 700));
            spun.UpdateTransform();
            root.UpdateLayout(new Vector2(420, 700));

            var perFrame = spin.DegreesPerSecond.ToRadians() / 60.0f;
            var turned = spun.Rotation.Y - startAngle;

            Check("and a selected pickup turns at the same rate as an unselected one",
                MathF.Abs(turned - perFrame * 2.0f) < perFrame * 0.25f,
                $"{turned.ToDegrees():F2} degrees over two frames, expected {(perFrame * 2.0f).ToDegrees():F2}");
        }
        finally
        {
            ManaTime.GlobalTime.DeltaTime = clock;
        }

        // ---------------------------------------------------------------- rotation stays a heading
        // Anything turning a node a little every frame used to accumulate without bound, until a float
        // had no precision left for a degree. The node folds it now, so no animator has to remember to.
        spun.Rotation = new Vector3(0.0f, 400.0f.ToRadians(), 0.0f);
        Check("a rotation past a full turn wraps",
            MathF.Abs(spun.RotationDegrees.Y - 40.0f) < 0.01f, $"{spun.RotationDegrees.Y:F1} degrees");

        spun.Rotation = new Vector3((-6.0f).ToRadians(), 0.0f, 0.0f);
        Check("and a small negative angle stays negative rather than becoming 354",
            MathF.Abs(spun.RotationDegrees.X + 6.0f) < 0.01f, $"{spun.RotationDegrees.X:F1} degrees");

        // ---------------------------------------------------------------- selecting a component
        // The panel keyed only on the selected node, so choosing a different component on the same node
        // changed nothing at all -- and which sections you got depended on whatever was selected the last
        // time the node itself changed, which is why the pickup's section appeared only sometimes.
        var twoPart = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Two Part" };
        var twoPartLight = new ManaLightSourceComponent(twoPart) { Intensity = 1.0f };
        var twoPartMotion = new ManaPickupMotionComponent(twoPart);
        twoPart.Components.Add(twoPartLight);
        twoPart.Components.Add(twoPartMotion);
        scene.Children.Add(twoPart);

        world.Selection.SelectedComponent = null;
        world.Selection.SelectedNode = twoPart;
        root.UpdateLayout(new Vector2(420, 700));

        var bothHeaders = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("a node with no component selected shows all of them",
            bothHeaders.Any(h => h == "Light Source")
                && bothHeaders.Any(h => h.Contains(nameof(ManaPickupMotionComponent), StringComparison.Ordinal)),
            string.Join(", ", bothHeaders));

        // The node does not change here -- only the component does. That used to be invisible.
        // Selecting one brings it to the top; it does not hide the rest of the node. Clicking a mesh in
        // the scene selects its renderer, and narrowing to that made every other component on the node
        // disappear -- which is how the pickup's settings became impossible to reach.
        world.Selection.SelectedComponent = twoPartMotion;
        root.UpdateLayout(new Vector2(420, 700));

        var motionFirst = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("selecting a component brings it to the top",
            motionFirst.FindIndex(h => h.Contains(nameof(ManaPickupMotionComponent), StringComparison.Ordinal))
                < motionFirst.IndexOf("Light Source"),
            string.Join(", ", motionFirst));

        Check("without hiding the rest of the node",
            motionFirst.Any(h => h == "Light Source"), string.Join(", ", motionFirst));

        world.Selection.SelectedComponent = twoPartLight;
        root.UpdateLayout(new Vector2(420, 700));

        var lightFirst = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("and switching to a sibling reorders rather than filtering",
            lightFirst.IndexOf("Light Source")
                < lightFirst.FindIndex(h => h.Contains(nameof(ManaPickupMotionComponent), StringComparison.Ordinal))
                && lightFirst.Any(h => h.Contains(nameof(ManaPickupMotionComponent), StringComparison.Ordinal)),
            string.Join(", ", lightFirst));

        // Clicking the donut in the scene selects its renderer, which is the exact sequence that hid the
        // pickup. Both must be there.
        var sceneClickNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Clicked" };
        var clickedRenderer = new MeshRendererComponent(sceneClickNode, new ManaCube(context, Vector4.One, Vector3.One));
        var clickedMotion = new ManaPickupMotionComponent(sceneClickNode);
        sceneClickNode.Components.Add(clickedRenderer);
        sceneClickNode.Components.Add(clickedMotion);
        scene.Children.Add(sceneClickNode);

        world.Selection.SelectedNode = sceneClickNode;
        world.Selection.SelectedComponent = clickedRenderer;
        root.UpdateLayout(new Vector2(420, 700));

        var clickedHeaders = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("clicking a mesh in the scene still shows the node's other components",
            clickedHeaders.Any(h => h.Contains(nameof(ManaPickupMotionComponent), StringComparison.Ordinal)),
            string.Join(", ", clickedHeaders));

        // A procedural mesh's material renders as PBR whether or not it says so, so it should say so --
        // otherwise a plain cube offers no channels and nowhere to assign a texture.
        Check("a procedural mesh gets a shader it can be inspected through",
            clickedHeaders.Any(h => h == "PBR Channels"), string.Join(", ", clickedHeaders));

        // Standing still must not rebuild, or a half-typed field is thrown away every frame.
        var held = FindAll<Expander>(view).FirstOrDefault();
        root.UpdateLayout(new Vector2(420, 700));
        root.UpdateLayout(new Vector2(420, 700));
        Check("an unchanged selection does not rebuild",
            ReferenceEquals(FindAll<Expander>(view).FirstOrDefault(), held), "same instance");

        world.Selection.SelectedComponent = null;

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

        // ---------------------------------------------------------------- add/remove components and delete nodes
        world.Selection.SelectedComponent = null;
        world.Selection.SelectedNode = first;
        root.UpdateLayout(new Vector2(420, 700));

        var addComponent = FindAll<ComboBox>(view)
            .FirstOrDefault(combo => combo.Placeholder == "Add component");
        var trainChoice = addComponent?.Items
            .Select((item, index) => (Label: item.ToString(), Index: index))
            .FirstOrDefault(item => item.Label == "Train Car");

        Check("the inspector offers component creation",
            addComponent != null && trainChoice?.Label == "Train Car",
            string.Join(", ", addComponent?.Items.Select(item => item.ToString()) ?? Array.Empty<string>()));

        if (addComponent != null && trainChoice?.Label == "Train Car")
        {
            addComponent.SelectedIndex = trainChoice.Value.Index;
            root.UpdateLayout(new Vector2(420, 700));
        }

        var trainComponent = first.Components.OfType<TrainCarComponent>().FirstOrDefault();
        Check("adding a train component attaches it to the selected node",
            trainComponent != null && ReferenceEquals(world.Selection.SelectedComponent, trainComponent),
            world.Selection.SelectedComponent?.GetType().Name ?? "none");

        var removeTrain = FindAll<Button>(view).FirstOrDefault(button => button.Text == "Remove");
        Check("component sections offer removal", removeTrain != null);
        if (removeTrain != null)
        {
            root.Input.InjectPointerMoved(removeTrain.Bounds.Center);
            root.Input.InjectPointerPressed(removeTrain.Bounds.Center, PointerButton.Left, 7.0);
            root.Input.InjectPointerReleased(removeTrain.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(420, 700));
        }

        Check("removing a component detaches it and keeps the node selected",
            first.Components.OfType<TrainCarComponent>().Count() == 0
            && ReferenceEquals(world.Selection.SelectedNode, first)
            && world.Selection.SelectedComponent == null,
            $"{first.Components.OfType<TrainCarComponent>().Count()} train components");

        var doomed = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Doomed" };
        var doomedChild = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Doomed Child" };
        doomed.AddChild(doomedChild);
        scene.Children.Add(doomed);
        var doomedCollision = context.Physics.AddBoxBody(doomed, Vector3.One, Vector3.Zero);
        world.Selection.SelectedNode = doomedChild;

        var deleted = SceneEditorOperations.DeleteNode(world, world.Selection, doomed);
        Check("deleting a node removes it from the scene graph",
            deleted && !scene.Children.Contains(doomed) && doomed.Parent == null);
        Check("deleting a node cleans up physics bodies",
            !context.Physics.PhysicsMap.ContainsKey(doomedCollision.Body)
            && !context.Physics.Simulation.Bodies.BodyExists(doomedCollision.Body));
        Check("deleting the selected subtree clears to its scene",
            ReferenceEquals(world.Selection.SelectedScene, scene)
            && world.Selection.SelectedNode == null
            && world.Selection.SelectedComponent == null);

        world.Selection.SelectedScene = null;
        world.Selection.SelectedNode = lit;
        root.UpdateLayout(new Vector2(420, 700));
        expanders = FindAll<Expander>(view);

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

        // The PBR channels are only offered for the shader that reads them, so the fixture needs one.
        try
        {
            material.Shader = new Mana.Code.ManaPbrShader(context);
        }
        catch (Exception ex)
        {
            Check("a PBR shader can be constructed headlessly", false, ex.Message);
        }
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

        // ---------------------------------------------------------------- the widget registry
        // The panel itself knows nothing about transforms or materials -- it asks the factory what shows
        // the selected thing. That is what makes adding a component a new file rather than another branch.
        var factory = InspectorWidgetFactory.CreateDefault();
        var inspectorContext = new InspectorContext(root.Theme, context, factory);

        Check("a node is claimed by more than one widget",
            factory.AllFor(first).Count() >= 2,
            string.Join(", ", factory.AllFor(first).Select(w => w.GetType().Name)));

        Check("nothing claims an unrelated object", factory.For("just a string") == null,
            factory.For("just a string")?.GetType().Name ?? "none");

        // Acceptance is per instance, not per type: the PBR channels apply to a material only when its
        // shader is the one that reads them.
        var pbr = factory.AllFor(material).Select(w => w.GetType().Name).ToList();
        Check("a PBR material gets its channel widget", pbr.Contains(nameof(PbrShaderWidget)),
            string.Join(", ", pbr));

        var plainMaterial = new ManaMaterial(context) { Name = "No Shader" };
        var plain = factory.AllFor(plainMaterial).Select(w => w.GetType().Name).ToList();
        Check("a material whose shader ignores them does not",
            !plain.Contains(nameof(PbrShaderWidget)), string.Join(", ", plain));
        Check("but it still gets the common material widget", plain.Contains(nameof(MaterialWidget)),
            string.Join(", ", plain));

        // An application can replace a built-in without editing it: the newest registration wins.
        var overridden = InspectorWidgetFactory.CreateDefault().Register(new StubTransformWidget());
        Check("the newest registration wins",
            overridden.For(first) is StubTransformWidget,
            overridden.For(first)?.GetType().Name ?? "none");

        Check("and the built-in is still there for BuildAll",
            overridden.AllFor(first).Any(w => w is TransformWidget),
            "registration order preserved");

        var stub = overridden.Build(first, inspectorContext);
        Check("a registered widget is what gets built", stub is TextBlock { Text: "stub" },
            stub?.GetType().Name ?? "null");

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
        // Scoped to the uniforms section rather than searched by value across the whole panel: the PBR
        // section has editors of its own that happen to sit at 0.5, and picking one of those up meant this
        // check quietly edited the wrong control.
        var strengthBox = FindAll<NumericBox>(uniformSection)
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

        // ---------------------------------------------------------------- level-authored collision
        // "Level JSON" is a save recipe for this node, not a prefab instance. Collision gets its own
        // node section so selecting imported mesh children can still expose the collider that owns them.
        var levelNode = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Level Mesh",
            LevelSource = ManaLevelSource.ForMesh("assets/platform.glb")
        };
        scene.Children.Add(levelNode);
        var collision = context.Physics.AddBoxBody(levelNode, new Vector3(2.0f, 1.0f, 3.0f), new Vector3(0.25f, 0.5f, 0.75f));

        world.Selection.SelectedNode = levelNode;
        root.UpdateLayout(new Vector2(420, 900));

        var levelSections = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("level-authored nodes call the save recipe Level JSON",
            levelSections.Contains("Level JSON") && !levelSections.Contains("Level Source"),
            string.Join(", ", levelSections));
        Check("nodes with box collision get a collision section",
            levelSections.Contains("Collision"),
            string.Join(", ", levelSections));
        Check("collision components do not fall through to reflection",
            !levelSections.Any(header => header.Contains(nameof(ManaCollisionComponent), StringComparison.Ordinal)),
            string.Join(", ", levelSections));

        var showCollider = FindAll<CheckBox>(view).FirstOrDefault(box => box.Text == "Show collider");
        Check("the collision section can pin collider debug", showCollider != null);
        if (showCollider != null)
        {
            showCollider.IsChecked = true;
            Check("pinning collider debug writes to the collision component",
                collision.ShowDebug,
                $"{collision.ShowDebug}");
        }

        var cubeNode = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Platform Cube",
            LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, new Vector3(6.0f, 2.0f, 10.0f))
        };
        cubeNode.Components.Add(new MeshRendererComponent(cubeNode, new ManaCube(context, Vector4.One, new Vector3(6.0f, 2.0f, 10.0f))));
        scene.Children.Add(cubeNode);
        context.Physics.AddBoxBody(cubeNode, new Vector3(6.0f, 2.0f, 10.0f), Vector3.Zero);

        world.Selection.SelectedNode = cubeNode;
        root.UpdateLayout(new Vector2(420, 900));

        var cubeText = string.Join(" | ", FindAll<TextBlock>(view).Select(t => t.Text));
        Check("cube level source labels the editable value as full size",
            cubeText.Contains("Size", StringComparison.Ordinal) && !cubeText.Contains("Half size", StringComparison.Ordinal),
            cubeText);

        var cubeSize = FindAll<VectorBox>(view)
            .FirstOrDefault(box => box.Value == new Vector3(6.0f, 2.0f, 10.0f));
        Check("cube level source presents full visual size",
            cubeSize != null,
            string.Join(", ", FindAll<VectorBox>(view).Select(box => box.Value.ToString())));

        if (cubeSize != null)
        {
            cubeSize.Value = new Vector3(8.0f, 1.0f, 4.0f);
            var resizedCollision = cubeNode.GetComponents<ManaCollisionComponent>()
                .FirstOrDefault(component => component.Shape is Box);
            Box? resizedBox = resizedCollision?.Shape is Box box ? box : null;

            Check("editing cube full size stores the visual size in the level recipe",
                cubeNode.LevelSource?.Size == new Vector3(8.0f, 1.0f, 4.0f),
                cubeNode.LevelSource?.Size.ToString() ?? "null");
            Check("editing cube full size resizes the box collider to full extents",
                resizedBox is { Width: 8.0f, Height: 1.0f, Length: 4.0f },
                resizedBox == null ? "missing" : $"{resizedBox.Value.Width}, {resizedBox.Value.Height}, {resizedBox.Value.Length}");
        }

        var levelChild = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Level Mesh Child" };
        levelNode.AddChild(levelChild);
        world.Selection.SelectedNode = levelChild;
        root.UpdateLayout(new Vector2(420, 900));

        var childSections = FindAll<Expander>(view).Select(e => e.Header).ToList();
        Check("selecting a mesh child still shows its owning collision",
            childSections.Contains("Collision"),
            string.Join(", ", childSections));

        var childCollisionText = string.Join(" | ", FindAll<TextBlock>(view).Select(t => t.Text));
        Check("the inherited collision reports its owner",
            childCollisionText.Contains("Level Mesh", StringComparison.Ordinal),
            childCollisionText);

        world.Selection.SelectedNode = meshNode;
        root.UpdateLayout(new Vector2(420, 900));

        // ---------------------------------------------------------------- clear of the scroll bar
        // Editors used to run right up to the bar, which reads as content sliding underneath it. The
        // viewer reserves the bar's width plus a gap, so there is visible space between the two.
        var viewer = FindAll<ScrollViewer>(view).FirstOrDefault();
        if (viewer is { CanScrollVertically: true })
        {
            var barLeft = viewer.Bounds.Right - root.Theme.ScrollBarThickness;

            var touching = FindAll<UiElement>(view)
                .Where(e => e is NumericBox or TextBox or ColorField or SliderBox)
                .Where(e => !e.Bounds.IsEmpty)
                .ToList();

            var overlapping = touching.Count(e => e.Bounds.Right > barLeft - 0.5f);
            Check("no editor reaches the scroll bar", overlapping == 0,
                $"{overlapping} of {touching.Count} reach past {barLeft:F0}");

            var widest = touching.Count > 0 ? touching.Max(e => e.Bounds.Right) : 0.0f;
            Check("and there is a visible gap before it", touching.Count > 0 && barLeft - widest >= 2.0f,
                $"{barLeft - widest:F1}px of clearance");
        }

        // ---------------------------------------------------------------- one pass is enough
        // A rebuilt subtree's polled bindings used to evaluate a frame late, because the elements it
        // creates are not in the list the refresh is iterating. That showed as a frame of blank rows every
        // time the selection changed, and it is why these checks once needed layout run two or three times.
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 900));
        world.Selection.SelectedNode = meshNode;
        root.UpdateLayout(new Vector2(420, 900));

        // Something only a polled binding ever writes: the slot tooltips are set nowhere else, so a
        // tooltip present here means the new subtree's bindings ran on the pass that created it.
        var settled = FindAll<Button>(view).Count(b =>
            b.Tooltip?.Contains(" map", StringComparison.OrdinalIgnoreCase) == true);
        Check("a rebuilt panel is populated in the same pass that built it", settled == 7,
            $"{settled} of 7 slots bound after one pass");

        // ---------------------------------------------------------------- the name column
        // How much room a name needs depends entirely on what is being inspected, so the divider between
        // the name and the editor is draggable -- and it moves the whole column, not one row, or the
        // labels would stop lining up.
        var splitters = FindAll<InspectorSplitter>(view);
        Check("every row carries a divider", splitters.Count > 3, $"{splitters.Count} dividers");

        if (splitters.Count > 0)
        {
            var splitter = splitters[0];
            var startX = splitter.Bounds.X;
            var otherStart = splitters[^1].Bounds.X;

            Check("and they all sit at the same place", MathF.Abs(startX - otherStart) < 0.5f,
                $"{startX:F0} vs {otherStart:F0}");

            // Drawn at rest, not only under the pointer. Painting it on hover alone made it a control you
            // had to already know was there -- nothing to aim at, and no hint the column could move.
            var painted = root.Render().Instances.ToArray().Count(i =>
                i.Rect.X >= splitter.Bounds.X - 1.0f
                && i.Rect.Z <= splitter.Bounds.Right + 1.0f
                && i.Rect.W - i.Rect.Y > 4.0f);

            Check("the divider is visible without being hovered", painted > 0,
                $"{painted} instances in the divider's column");

            root.Input.InjectPointerMoved(splitter.Bounds.Center);
            root.Input.InjectPointerPressed(splitter.Bounds.Center, PointerButton.Left, 40.0);
            root.Input.InjectPointerMoved(splitter.Bounds.Center + new Vector2(40.0f, 0.0f));
            root.Input.InjectPointerReleased(splitter.Bounds.Center + new Vector2(40.0f, 0.0f), PointerButton.Left);
            root.UpdateLayout(new Vector2(420, 900));

            Check("dragging one widens the name column",
                splitter.Bounds.X > startX + 30.0f, $"{startX:F0} -> {splitter.Bounds.X:F0}");

            Check("and every other row follows it",
                MathF.Abs(splitters[^1].Bounds.X - splitter.Bounds.X) < 0.5f,
                $"{splitters[^1].Bounds.X:F0} vs {splitter.Bounds.X:F0}");

            // Dragged to nothing the panel would be a column of anonymous editors, so it is clamped.
            root.Input.InjectPointerMoved(splitter.Bounds.Center);
            root.Input.InjectPointerPressed(splitter.Bounds.Center, PointerButton.Left, 41.0);
            root.Input.InjectPointerMoved(new Vector2(-500.0f, splitter.Bounds.Center.Y));
            root.Input.InjectPointerReleased(new Vector2(-500.0f, splitter.Bounds.Center.Y), PointerButton.Left);
            root.UpdateLayout(new Vector2(420, 900));

            Check("it cannot be dragged away entirely",
                splitter.Bounds.X >= InspectorMetrics.MinimumLabelWidth - 0.5f,
                $"{splitter.Bounds.X:F0}, floor {InspectorMetrics.MinimumLabelWidth:F0}");

            // Back to something sensible for the checks that follow.
            root.Input.InjectPointerMoved(splitter.Bounds.Center);
            root.Input.InjectPointerPressed(splitter.Bounds.Center, PointerButton.Left, 42.0);
            root.Input.InjectPointerMoved(new Vector2(104.0f, splitter.Bounds.Center.Y));
            root.Input.InjectPointerReleased(new Vector2(104.0f, splitter.Bounds.Center.Y), PointerButton.Left);
            root.UpdateLayout(new Vector2(420, 900));
        }

        // ---------------------------------------------------------------- texture channels
        // Every PBR channel has a slot whether or not it is filled -- an empty slot is where you click to
        // assign one, which is the difference between showing a material and being able to dress it.
        // The slot sits on the row of the value it modulates rather than in a section of its own, because
        // which value a map belonged to was the thing that was hardest to tell when the two were separated.
        var emptySlots = FindAll<UiImage>(view);
        Check("every channel has a slot before anything is assigned", emptySlots.Count == 7,
            $"{emptySlots.Count} slots");
        Check("and all of them start empty", emptySlots.All(i => !i.HasSource),
            $"{emptySlots.Count(i => i.HasSource)} filled");

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

        // One preview slot per channel, filled or not.
        var previews = FindAll<UiImage>(view);
        Check("every channel has a preview slot", previews.Count == 7, $"{previews.Count} images");

        // The slot is a square barely larger than a line of text, so a picture is all it can show. The
        // path is what tells two grey noise maps apart, and it goes in the tooltip.
        var albedoSlot = FindAll<Button>(view)
            .FirstOrDefault(b => b.Tooltip != null && b.Tooltip.StartsWith("Albedo map", StringComparison.Ordinal));

        Check("a filled slot names its file on hover",
            albedoSlot?.Tooltip?.Contains("albedo.png", StringComparison.Ordinal) == true,
            albedoSlot?.Tooltip?.Replace("\n", " | ") ?? "no tooltip");

        var emptySlotTip = FindAll<Button>(view)
            .FirstOrDefault(b => b.Tooltip != null && b.Tooltip.StartsWith("Normal map", StringComparison.Ordinal));

        Check("and an empty one says it is empty",
            emptySlotTip?.Tooltip?.Contains("Empty", StringComparison.Ordinal) == true,
            emptySlotTip?.Tooltip?.Replace("\n", " | ") ?? "no tooltip");

        if (previews.Count == 7)
        {
            Check("the filled channel points at the colour atlas",
                previews[0].HasSource && previews[0].SourceSize.X > 0.0f,
                $"{previews[0].SourceSize} from {previews[0].SourceUv}");

            Check("an empty channel shows nothing", !previews[1].HasSource,
                previews[1].SourceSize.ToString());

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
        var firstUv = previews.Count > 0 ? previews[0].SourceUv : Vector4.Zero;
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(420, 900));
        world.Selection.SelectedNode = meshNode;
        root.UpdateLayout(new Vector2(420, 900));

        var rebuiltPreview = FindAll<UiImage>(view).FirstOrDefault();
        Check("rebuilding reuses the cached thumbnail",
            rebuiltPreview != null && rebuiltPreview.SourceUv == firstUv,
            "same atlas region");
        Check("and packs nothing new", root.Images.Pixels.Length == before, "atlas unchanged");

        // ---------------------------------------------------------------- assigning a texture
        // The picker writes straight to the channel, which is what makes a plain box wearable.
        material.Albedo.TextureValue = null;
        root.UpdateLayout(new Vector2(420, 900));

        Check("clearing a channel empties its slot",
            FindAll<UiImage>(view)[0].HasSource == false, "no source");

        material.Albedo.TextureValue = map;
        root.UpdateLayout(new Vector2(420, 900));

        Check("assigning fills it again without a rebuild",
            FindAll<UiImage>(view)[0].HasSource, "polled, so the row follows the channel");

        // The library is what the picker offers. Scanned from the project rather than a file dialog, so a
        // material cannot end up pointing at something outside it.
        // Relative to the working directory, which for the suite is its own output folder -- where the
        // engine's assets are copied. Reported rather than assumed, so a checkout without them says so.
        Check("the texture library scans the project", ManaUiTextureLibrary.Assets.Count > 0,
            $"{ManaUiTextureLibrary.Assets.Count} textures under {Directory.GetCurrentDirectory()}");
        Check("and stores relative paths",
            ManaUiTextureLibrary.Assets.All(a => !Path.IsPathRooted(a.RelativePath)),
            "portable across machines");

        // ---------------------------------------------------------------- the picker
        // Opened by clicking the slot, so the gesture is covered rather than the handler being called.
        var slotButton = FindAll<Button>(view)
            .FirstOrDefault(b => b.Tooltip?.StartsWith("Albedo map", StringComparison.Ordinal) == true);

        if (slotButton != null && ManaUiTextureLibrary.Assets.Count > 0)
        {
            root.UpdateLayout(new Vector2(420, 900));
            root.Input.InjectPointerMoved(slotButton.Bounds.Center);

            // Rested on first, which is how anyone actually reaches this button: you hover the slot to see
            // what is in it, the tooltip appears, and then you click. The tooltip is anchored to the slot,
            // and light dismissal swallows a press that lands on an open popup's anchor -- a rule that
            // exists so a dropdown button cannot close and instantly reopen. Applied to a tooltip it ate
            // the click outright, so the slot could not be clicked at all once its tooltip was up.
            // Two frames: the tick that first sees a new candidate starts its dwell rather than completing
            // it, so a single one never shows anything.
            root.Popups.Tick(0.1);
            root.Popups.Tick(1.0);
            root.UpdateLayout(new Vector2(420, 900));

            Check("resting on a slot shows its tooltip", root.Popups.Open.Count > 0,
                $"{root.Popups.Open.Count} popups");

            root.Input.InjectPointerPressed(slotButton.Bounds.Center, PointerButton.Left, 20.0);
            root.Input.InjectPointerReleased(slotButton.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(420, 900));

            var picker = root.Popups.Open.LastOrDefault();
            Check("clicking a slot opens the picker", picker != null);

            // And the tooltip is gone rather than left floating over what the click opened.
            Check("the tooltip does not survive the click", root.Popups.Open.Count == 1,
                $"{root.Popups.Open.Count} popups");

            var offered = FindAll<Button>(picker?.Child)
                .Where(b => b.Tooltip != null)
                .ToList();

            Check("the picker offers the library",
                offered.Count == ManaUiTextureLibrary.Assets.Count,
                $"{offered.Count} of {ManaUiTextureLibrary.Assets.Count}");

            Check("each entry carries its full path",
                offered.All(b => !string.IsNullOrWhiteSpace(b.Tooltip)),
                "hover tells you which of several similarly named maps this is");

            // A filter, because a real project has hundreds of these and scrolling is not a search.
            var filter = FindAll<TextBox>(picker?.Child).FirstOrDefault();
            Check("the picker has a filter", filter != null);

            if (filter != null && offered.Count > 0)
            {
                var target = ManaUiTextureLibrary.Assets[0].Name;

                filter.Text = target;
                root.UpdateLayout(new Vector2(420, 900));

                var visible = offered.Count(b => b.Visibility == Visibility.Visible);
                Check("filtering narrows the list", visible < offered.Count && visible > 0,
                    $"{visible} of {offered.Count} match '{target}'");

                Check("and every survivor actually matches",
                    offered.Where(b => b.Visibility == Visibility.Visible)
                        .All(b => b.Tooltip!.Contains(target, StringComparison.OrdinalIgnoreCase)),
                    "no false positives");

                filter.Text = "zzz-no-such-texture";
                root.UpdateLayout(new Vector2(420, 900));
                Check("a filter matching nothing empties the list",
                    offered.All(b => b.Visibility == Visibility.Collapsed),
                    $"{offered.Count(b => b.Visibility == Visibility.Visible)} still shown");

                filter.Text = string.Empty;
                root.UpdateLayout(new Vector2(420, 900));
                Check("clearing the filter brings them back",
                    offered.All(b => b.Visibility == Visibility.Visible), "all offered again");
            }

            picker?.Close();
            root.UpdateLayout(new Vector2(420, 900));
        }

        // ---------------------------------------------------------------- and it reaches the renderer
        // The failure this covers: the picker assigned a map, the inspector showed it, and the mesh stayed
        // untextured. The backends take the albedo map from the mesh's CPU data, where it is baked in at
        // load time -- and left null by everything procedural, which a cube is. A map assigned to the
        // material afterwards was written to a field nothing on the render path ever read.
        if (ManaUiTextureLibrary.Assets.Count > 0)
        {
            var onDisk = ManaUiTextureLibrary.Assets[0];
            material.Albedo.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };

            var collected = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(collected);

            var instance = collected.FirstOrDefault();
            Check("the cube is collected for rendering", instance != null, $"{collected.Count} instances");

            Check("an assigned map reaches the render instance",
                !string.IsNullOrWhiteSpace(instance?.MeshData.AlbedoTexturePath),
                instance?.MeshData.AlbedoTexturePath ?? "null -- the material's map was dropped");

            // Resolved to somewhere on disk, because a relative path is what a material stores and an
            // absolute one is what the backend opens. This is the exact test the WebGPU loader makes.
            Check("and points at a file the backend can open",
                instance?.MeshData.AlbedoTexturePath is { } path && File.Exists(path),
                instance?.MeshData.AlbedoTexturePath ?? "none");

            // Collected every frame. A fresh copy each time would fail the renderers' reference check and
            // have them rebuilding the mesh's GPU buffers on every frame it is visible.
            var again = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(again);

            Check("the same assignment yields the same data object",
                ReferenceEquals(instance?.MeshData, again.FirstOrDefault()?.MeshData),
                "so the renderer does not rebuild it every frame");

            // A picked texture is a filename and nothing else -- never loaded, no pixels. The preview has to
            // come off disk, or the slot sits empty after a successful pick and looks like it did nothing.
            world.Selection.SelectedNode = null;
            root.UpdateLayout(new Vector2(420, 900));
            world.Selection.SelectedNode = meshNode;
            root.UpdateLayout(new Vector2(420, 900));

            var picked = FindAll<UiImage>(view).FirstOrDefault();
            Check("a picked texture previews without ever being loaded",
                picked?.HasSource == true, $"{picked?.SourceSize} from {onDisk.Name}");

            // Holding a source is not the same as being seen. The slot is a small square inside a border
            // inside a button, and any of those collapsing to nothing would leave the row looking exactly
            // like a failed pick.
            Check("the slot is big enough to see", picked is { Bounds.Width: > 8.0f, Bounds.Height: > 8.0f },
                picked?.Bounds.ToString() ?? "no bounds");

            var textured = root.Render().Instances.ToArray()
                .Where(i => (i.Flags & (uint)ManaUiInstanceFlags.Texture) != 0)
                .Count(i => picked != null
                    && i.Rect.X >= picked.Bounds.X - 1.0f
                    && i.Rect.Z <= picked.Bounds.Right + 1.0f
                    && i.Rect.Y >= picked.Bounds.Y - 1.0f
                    && i.Rect.W <= picked.Bounds.Bottom + 1.0f);

            Check("and the thumbnail is actually drawn there", textured > 0,
                $"{textured} textured instances inside the slot");

            // Every channel, not just albedo. The maps used to stop at the material: the picker wrote them
            // and nothing downstream read them, so assigning a normal or emission map did nothing at all.
            material.Normal.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };
            material.Emission.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };
            material.Metallic.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };
            material.Roughness.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };
            material.Occlusion.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };
            material.Opacity.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };

            var allMaps = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(allMaps);
            var mapped = allMaps.FirstOrDefault()?.MeshData;

            Check("a normal map reaches the render instance",
                mapped?.NormalTexturePath is { } n && File.Exists(n), mapped?.NormalTexturePath ?? "null");
            Check("an emission map does too",
                mapped?.EmissionTexturePath is { } e && File.Exists(e), mapped?.EmissionTexturePath ?? "null");
            Check("and metallic, roughness, ambient and opacity",
                mapped?.MetallicTexturePath != null && mapped.RoughnessTexturePath != null
                    && mapped.OcclusionTexturePath != null && mapped.OpacityTexturePath != null,
                $"{mapped?.MetallicTexturePath != null}/{mapped?.RoughnessTexturePath != null}/"
                    + $"{mapped?.OcclusionTexturePath != null}/{mapped?.OpacityTexturePath != null}");

            // Still stable frame to frame, or the renderer rebuilds every mesh every frame.
            var repeat = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(repeat);
            Check("with all six assigned it is still the same object",
                ReferenceEquals(mapped, repeat.FirstOrDefault()?.MeshData), "no per-frame rebuild");

            // Opacity is the one that needs more than a map: the opaque blend state is One/Zero, so a
            // material that only lowers its alpha renders exactly as before. Auto resolves to Cutout the
            // moment there is anything to say, rather than leaving an assigned map doing nothing.
            material.Opacity.TextureValue = new ManaTexture(context) { OriginalFilename = onDisk.RelativePath };
            material.OpacityMode = ManaOpacityMode.Auto;

            Check("assigning an opacity map makes the material cut out",
                material.EffectiveOpacityMode == ManaOpacityMode.Cutout,
                material.EffectiveOpacityMode.ToString());

            material.Opacity.TextureValue = null;
            material.Opacity.Value = 1.0f;
            Check("a material with nothing to say stays opaque",
                material.EffectiveOpacityMode == ManaOpacityMode.Opaque,
                material.EffectiveOpacityMode.ToString());

            // A uniform value is a fade, and only blending can express that. Cutting out with the same
            // alpha everywhere is a cliff: nothing, nothing, then the whole surface at once.
            material.Opacity.Value = 0.5f;
            Check("lowering the value alone asks for a fade, so it blends",
                material.EffectiveOpacityMode == ManaOpacityMode.Blend,
                material.EffectiveOpacityMode.ToString());

            // And the value has to actually reach the renderer, which is the half that was missing: the
            // map was wired and the slider was not, so it moved and nothing happened.
            var faded = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(faded);

            Check("the opacity value reaches the render instance",
                MathF.Abs((faded.FirstOrDefault()?.Opacity ?? 1.0f) - 0.5f) < 0.001f,
                $"{faded.FirstOrDefault()?.Opacity}");

            // Blending is a material decision as much as a shader one: a PBR shader is opaque by nature,
            // but a PBR material set to blend is not, and the pipeline is chosen from the instance.
            material.OpacityMode = ManaOpacityMode.Blend;

            var blended = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(blended);
            var blendInstance = blended.FirstOrDefault();

            Check("a blending material reaches the renderer as transparent",
                blendInstance?.IsTransparent == true, $"{blendInstance?.IsTransparent}");
            Check("and stops writing depth",
                blendInstance?.DisableDepthWrite == true, $"{blendInstance?.DisableDepthWrite}");
            Check("with its mode and cutoff",
                blendInstance?.OpacityMode == ManaOpacityMode.Blend
                    && MathF.Abs((blendInstance?.AlphaCutoff ?? 0.0f) - 0.5f) < 0.001f,
                $"{blendInstance?.OpacityMode}, cutoff {blendInstance?.AlphaCutoff}");

            material.OpacityMode = ManaOpacityMode.Auto;
            material.Opacity.Value = 1.0f;

            var opaque = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(opaque);
            Check("an ordinary material is still opaque",
                opaque.FirstOrDefault()?.IsTransparent == false, "no blending unless asked");

            // A loaded glTF writes NormalSelector = 0, which means "use the map fully" -- it mixes toward
            // the vertex normal, so it reads backwards everywhere downstream. The instance carries a
            // strength instead, and the inspector shows that, or a helmet with a perfectly good normal
            // map appears to have it switched off.
            material.NormalSelector = 0.0;

            var normals = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(normals);

            Check("a fully applied normal map reaches the renderer at full strength",
                MathF.Abs((normals.FirstOrDefault()?.NormalStrength ?? 0.0f) - 1.0f) < 0.001f,
                $"{normals.FirstOrDefault()?.NormalStrength}");

            material.NormalSelector = 1.0;
            var ignored = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(ignored);

            Check("and one mixed all the way to the vertex normal arrives at zero",
                MathF.Abs(ignored.FirstOrDefault()?.NormalStrength ?? 1.0f) < 0.001f,
                $"{ignored.FirstOrDefault()?.NormalStrength}");

            material.NormalSelector = 0.0;

            // UV tiling rides on the instance rather than the mesh: it is a material property, and the
            // same brick wants a different repeat on a wall than on a doorframe.
            material.UvScale = new Vector2(4.0f, 2.0f);
            material.UvOffset = new Vector2(0.25f, 0.5f);

            var tiled = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(tiled);

            Check("uv tiling reaches the render instance",
                tiled.FirstOrDefault()?.UvTransform == new Vector4(4.0f, 2.0f, 0.25f, 0.5f),
                tiled.FirstOrDefault()?.UvTransform.ToString() ?? "none");

            material.UvScale = Vector2.One;
            material.UvOffset = Vector2.Zero;
            material.Normal.TextureValue = null;
            material.Emission.TextureValue = null;
            material.Metallic.TextureValue = null;
            material.Roughness.TextureValue = null;
            material.Occlusion.TextureValue = null;
            material.Opacity.TextureValue = null;

            // Cleared, and the mesh goes back to whatever it was carrying itself.
            material.Albedo.TextureValue = null;
            var cleared = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(cleared);

            Check("clearing the channel takes the map off the mesh",
                cleared.FirstOrDefault()?.MeshData.AlbedoTexturePath == null,
                cleared.FirstOrDefault()?.MeshData.AlbedoTexturePath ?? "null");
        }

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
