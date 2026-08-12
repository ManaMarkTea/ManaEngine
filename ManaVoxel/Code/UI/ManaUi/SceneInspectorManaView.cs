using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The scene inspector rebuilt in ManaUI, over the live scene graph and sharing the editor's
    /// <see cref="SceneSelectionState"/> with the ImGui inspector and both outliners.
    /// <para>
    /// Everything here writes straight to the node. The scene graph is not a view model and never will be,
    /// so the editors are bound with polled reads and direct writes rather than through
    /// <c>INotifyPropertyChanged</c> -- the same escape hatch the outliner uses. That also means a value
    /// changed by anything else, a gizmo or an animation, shows up here without being told.
    /// </para>
    /// </summary>
    public static class SceneInspectorManaView
    {
        public static UiElement Build(UiTheme theme, SceneSelectionState selection)
        {
            // The tree is retained, so the panel cannot simply re-run when the selection changes -- it has
            // to notice. DynamicContent polls the selection and rebuilds only on an actual change, which is
            // what keeps a half-typed field from being thrown away every frame.
            var content = new DynamicContent()
                .Showing<ManaSceneNode>(
                    () => selection.SelectedNode,
                    node => BuildNodeInspector(theme, node, selection))
                .Empty(EmptyState(theme));

            return new Grid().Rows("*").Columns("*").Padding(8.0f).Children(
                content.Scrollable().Configure(viewer => viewer.MinHeight = 120.0f));
        }

        private static UiElement EmptyState(UiTheme theme)
        {
            return new TextBlock()
                .Text("Nothing selected.")
                .Foreground(theme.TextMuted)
                .Margin(4.0f);
        }

        private static UiElement BuildNodeInspector(UiTheme theme, ManaSceneNode node, SceneSelectionState selection)
        {
            var sections = new StackPanel().Vertical(theme.Spacing).Children(
                Identity(theme, node),
                Transform(theme, node));

            // A node can host components belonging to unrelated things -- a planet's shared root holds one
            // per patch -- so selecting a component shows that one alone rather than every sibling.
            var selected = selection.SelectedComponent;
            if (selected != null && node.Components.Contains(selected))
            {
                sections.Children.Add(ComponentSection(theme, selected));
                return sections;
            }

            foreach (var component in node.Components)
            {
                sections.Children.Add(ComponentSection(theme, component));
            }

            return sections;
        }

        // ----- sections -----------------------------------------------------------------------------

        private static UiElement Identity(UiTheme theme, ManaSceneNode node)
        {
            return new Expander()
                .Header("Node")
                .Section(theme.Spacing,
                    Row(theme, "Name", new TextBox()
                        .Value(node.Name ?? string.Empty)
                        .Hint(node.GetType().Name)
                        .OnCommitted(text => node.Name = text)),

                    Row(theme, "Type", ReadOnly(theme, node.GetType().Name)));
        }

        private static UiElement Transform(UiTheme theme, ManaSceneNode node)
        {
            // Rotation is stored in radians and shown in degrees, so it round-trips through a conversion
            // rather than binding straight to the property the way position and scale do.
            var rotation = new VectorBox().Step(1.0f).Places(2);
            rotation.Value = node.RotationDegrees;
            rotation.ValueChanged += (_, value) =>
            {
                node.Rotation = new Vector3(
                    value.X.ToRadians(),
                    value.Y.ToRadians(),
                    value.Z.ToRadians());
                node.UpdateTransform();
            };

            // Polled rather than one-shot: a gizmo drag or an animation moves the node without telling
            // anyone, and an inspector showing a stale transform is worse than one showing none.
            rotation.BindPolled(box => box.Value, () => node.RotationDegrees);

            return new Expander()
                .Header("Transform")
                .Section(theme.Spacing,
                    Row(theme, "Position", Vector(node, () => node.Position, value =>
                    {
                        node.Position = value;
                        node.UpdateTransform();
                    }, step: 0.1f)),

                    Row(theme, "Rotation", rotation),

                    Row(theme, "Scale", Vector(node, () => node.Scale, value =>
                    {
                        node.Scale = value;
                        node.UpdateTransform();
                    }, step: 0.05f)));
        }

        private static VectorBox Vector(
            ManaSceneNode node,
            Func<Vector3> read,
            Action<Vector3> write,
            float step)
        {
            var box = new VectorBox().Step(step).Places(3);
            box.Value = read();
            box.ValueChanged += (_, value) => write(new Vector3(value.X, value.Y, value.Z));
            box.BindPolled(b => b.Value, read);
            return box;
        }

        private static UiElement ComponentSection(UiTheme theme, ManaComponent component)
        {
            return component switch
            {
                MeshRendererComponent meshRenderer => MeshRenderer(theme, meshRenderer),
                ManaLightSourceComponent light => Light(theme, light),
                _ => new Expander()
                    .Header(ComponentLabel(component))
                    .Section(theme.Spacing, ReadOnly(theme, "No inspector for this component yet."))
            };
        }

        private static UiElement MeshRenderer(UiTheme theme, MeshRendererComponent meshRenderer)
        {
            var material = meshRenderer.Material;

            return new Expander()
                .Header(ComponentLabel(meshRenderer))
                .Section(theme.Spacing,
                    Row(theme, "Mesh", ReadOnly(theme, meshRenderer.Mesh?.GetType().Name ?? "none")),
                    Row(theme, "Material", ReadOnly(theme, Fallback(material?.Name, "unnamed"))),

                    new CheckBox().Label("Draw after scene")
                        .Checked(meshRenderer.DrawAfterScene)
                        .OnChanged(value => meshRenderer.DrawAfterScene = value),

                    new CheckBox().Label("Outline")
                        .Checked(meshRenderer.OutlineEnabled)
                        .OnChanged(value => meshRenderer.OutlineEnabled = value),

                    Row(theme, "Width", new NumericBox()
                        .Step(0.1f)
                        .Range(1.0f, 4.0f)
                        .Places(2)
                        .Value(meshRenderer.OutlineWidth)
                        .OnChanged(value => meshRenderer.OutlineWidth = value)));
        }

        private static UiElement Light(UiTheme theme, ManaLightSourceComponent light)
        {
            var direction = new VectorBox().Step(0.05f).Places(3);
            direction.Value = light.Direction;
            direction.ValueChanged += (_, value) =>
                light.Direction = new Vector3(value.X, value.Y, value.Z);
            direction.BindPolled(box => box.Value, () => light.Direction);

            return new Expander()
                .Header(ComponentLabel(light))
                .Section(theme.Spacing,
                    new CheckBox().Label("Enabled")
                        .Checked(light.Enabled)
                        .OnChanged(value => light.Enabled = value),

                    Row(theme, "Intensity", new NumericBox()
                        .Step(0.05f)
                        .Range(0.0f, 20.0f)
                        .Places(2)
                        .Value(light.Intensity)
                        .OnChanged(value => light.Intensity = value)),

                    Row(theme, "Direction", direction),

                    Row(theme, "Colour", Colour(() => light.Color, value => light.Color = value)));
        }

        /// <summary>
        /// A colour swatch that opens a picker. Polled as well as written, for the same reason the transform
        /// editors are: nothing on the engine side announces a change.
        /// </summary>
        private static ColorField Colour(Func<Vector4> read, Action<Vector4> write)
        {
            var field = new ColorField { Color = read() };
            field.ColorChanged += (_, value) => write(value);
            field.BindPolled(f => f.Color, read);
            return field;
        }

        // ----- shared bits --------------------------------------------------------------------------

        /// <summary>A labelled property row. The star column is what lets an editor fill the panel's width.</summary>
        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            return new Grid().Columns("72, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock()
                    .Text(label)
                    .Foreground(theme.TextMuted)
                    .Ellipsis()
                    .CenterV()
                    .GridColumn(0),
                editor.GridColumn(1));
        }

        private static UiElement ReadOnly(UiTheme theme, string value)
        {
            return new TextBlock().Text(value).Foreground(theme.Text).Ellipsis().CenterV();
        }

        private static string ComponentLabel(ManaComponent component)
        {
            return component switch
            {
                MeshRendererComponent => "Mesh Renderer",
                ManaLightSourceComponent => "Light Source",
                _ => component.GetType().Name
            };
        }

        private static string Fallback(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
