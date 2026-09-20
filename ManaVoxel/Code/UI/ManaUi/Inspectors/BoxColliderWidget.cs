using Mana.Code.Components;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors;

public sealed class BoxColliderWidget : InspectorWidget<ManaBoxColliderComponent>
{
    private readonly record struct Bounds(Vector3 Size, Vector3 Center)
    {
        public static Bounds Capture(ManaBoxColliderComponent box) => new(box.Size, box.Center);
        public void Apply(ManaBoxColliderComponent box) { box.Size = Size; box.Center = Center; }
    }

    protected override UiElement Build(ManaBoxColliderComponent box, InspectorContext context)
    {
        var visible = new CheckBox().Label("Show collider").Checked(box.ShowDebug)
            .OnChanged(value => box.ShowDebug = value);
        visible.BindPolled(control => control.IsChecked, () => box.ShowDebug);
        var fit = new ManaUI.Button().Label("Fit visual bounds").Variant(ButtonVariant.Subtle)
            .OnClick(() =>
            {
                if (!box.TryGetVisualBounds(out var size, out var center)) return;
                var before = Bounds.Capture(box);
                box.Size = size;
                box.Center = center;
                Record(box, context, before, "Fit box collider");
            });
        fit.BindPolled(button => button.IsEnabled, () => box.TryGetVisualBounds(out _, out _));
        return new Expander().Header("Box Collider").Section(context.Theme.Spacing,
            new TextBlock().Text("Follows this node. Size and center use local coordinates.")
                .Foreground(context.Theme.TextMuted).Wrap(),
            InspectorControls.Row(context, "Size", Edit(box, context, () => box.Size, value => box.Size = value)),
            InspectorControls.Row(context, "Center", Edit(box, context, () => box.Center, value => box.Center = value)),
            visible, fit);
    }

    private static VectorBox Edit(ManaBoxColliderComponent box, InspectorContext context,
        Func<Vector3> read, Action<Vector3> write)
    {
        var editor = new VectorBox().Step(0.05f).Places(3);
        editor.Value = read();
        Bounds? before = null;
        editor.ValueChanged += (_, value) =>
        {
            if (editor.IsApplyingBinding) return;
            before ??= Bounds.Capture(box);
            write(new Vector3(value.X, value.Y, value.Z));
        };
        editor.ValueCommitted += (_, _) =>
        {
            if (before.HasValue) Record(box, context, before.Value, "Edit box collider");
            before = null;
        };
        editor.BindPolled(control => control.Value, read);
        return editor;
    }

    private static void Record(ManaBoxColliderComponent box, InspectorContext context, Bounds before, string description)
    {
        var after = Bounds.Capture(box);
        if (before == after) return;
        context.History?.Record(description, () => before.Apply(box), () => after.Apply(box));
    }
}
