using Mana;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A mesh renderer, followed by whatever the registry has to say about its material.
    /// <para>
    /// The material's sections are siblings rather than nested. Materials are shared between renderers, so
    /// burying one inside a component's section implies an ownership that is not there.
    /// </para>
    /// </summary>
    public sealed class MeshRendererWidget : InspectorWidget<MeshRendererComponent>
    {
        protected override UiElement Build(MeshRendererComponent renderer, InspectorContext context)
        {
            var theme = context.Theme;
            var drawAfterScene = Toggle(context, "Draw after scene",
                () => renderer.DrawAfterScene, value => renderer.DrawAfterScene = value);
            var outline = Toggle(context, "Outline",
                () => renderer.OutlineEnabled, value => renderer.OutlineEnabled = value)
                .WithTooltip("Custom mesh outline. The editor's selection highlight is separate.");
            var width = InspectorControls.Scalar(
                () => renderer.OutlineWidth, value => renderer.OutlineWidth = value, 1.0f, 4.0f);
            width.BindPolled(control => control.IsEnabled, () => renderer.OutlineEnabled);
            var color = InspectorControls.Colour(() => renderer.OutlineColor, value => renderer.OutlineColor = value);
            color.BindPolled(control => control.IsEnabled, () => renderer.OutlineEnabled);

            var section = new Expander()
                .Header("Mesh Renderer")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Mesh",
                        InspectorControls.ReadOnly(theme, renderer.Mesh?.GetType().Name ?? "none")),

                    drawAfterScene,
                    outline,
                    InspectorControls.Row(context, "Outline width", width),
                    InspectorControls.Row(context, "Outline color", color));

            var material = context.BuildAll(renderer.Material);
            if (material.Count == 0)
            {
                return section;
            }

            var stack = new StackPanel().Vertical(theme.Spacing).Children(section);
            foreach (var part in material)
            {
                stack.Children.Add(part);
            }

            return stack;
        }

        private static CheckBox Toggle(InspectorContext context, string label, Func<bool> read, Action<bool> write)
        {
            var toggle = new CheckBox().Label(label).Checked(read());
            toggle.CheckedChanged += (_, value) =>
            {
                if (toggle.IsApplyingBinding) return;
                var before = read();
                write(value);
                // Update the poll's cached value immediately, including when Undo occurs
                // before the next frame has had a chance to observe this edit.
                toggle.RefreshBindings();
                if (before != value)
                    context.History?.Record($"Change {label}", () => write(before), () => write(value));
            };
            toggle.BindPolled(control => control.IsChecked, read);
            return toggle;
        }
    }
}
