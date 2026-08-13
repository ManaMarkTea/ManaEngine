using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaUI;

using Mana.Code.ManaShaders;
using Mana.Code.UI.ManaUi;
using Mana.Code.Materials;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

// Aliased rather than importing Silk.NET.OpenGL wholesale: this file needs exactly one type from it, and
// the namespace collides with several the UI already uses.
using UniformType = Silk.NET.OpenGL.UniformType;

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
            var renderer = new Expander()
                .Header(ComponentLabel(meshRenderer))
                .Section(theme.Spacing,
                    Row(theme, "Mesh", ReadOnly(theme, meshRenderer.Mesh?.GetType().Name ?? "none")),

                    new CheckBox().Label("Draw after scene")
                        .Checked(meshRenderer.DrawAfterScene)
                        .OnChanged(value => meshRenderer.DrawAfterScene = value),

                    new CheckBox().Label("Outline")
                        .Checked(meshRenderer.OutlineEnabled)
                        .OnChanged(value => meshRenderer.OutlineEnabled = value),

                    Row(theme, "Width", Scalar(
                        () => meshRenderer.OutlineWidth,
                        value => meshRenderer.OutlineWidth = value,
                        1.0f,
                        4.0f)));

            var material = meshRenderer.Material;
            if (material == null)
            {
                return renderer;
            }

            // A sibling section rather than a nested one. Materials are shared between renderers, so
            // burying one inside a component's section implies an ownership that is not there.
            return new StackPanel().Vertical(theme.Spacing).Children(renderer, Material(theme, material));
        }

        /// <summary>
        /// The material section. The PBR channels are shown only for the shader that reads them -- a
        /// "shader toy" material ignores albedo and roughness entirely, and offering them there is an
        /// invitation to spend ten minutes wondering why nothing happens.
        /// </summary>
        private static UiElement Material(UiTheme theme, ManaMaterial material)
        {
            var rows = new StackPanel().Vertical(theme.Spacing).Children(
                Row(theme, "Name", new TextBox()
                    .Value(material.Name ?? string.Empty)
                    .Hint("unnamed")
                    .OnCommitted(text => material.Name = text)),

                Row(theme, "Shader", ReadOnly(theme, material.Shader?.GetType().Name ?? "none")));

            if (material.Shader is ManaPbrShader)
            {
                rows.Children.Add(Row(theme, "Albedo",
                    Colour(() => material.Albedo.Value, value => material.Albedo.Value = value)));
                rows.Children.Add(Row(theme, "Emission",
                    Colour(() => material.Emission.Value, value => material.Emission.Value = value)));

                rows.Children.Add(Row(theme, "Metallic",
                    Scalar(() => material.Metallic.Value, v => material.Metallic.Value = v, 0.0f, 1.0f)));
                rows.Children.Add(Row(theme, "Roughness",
                    Scalar(() => material.Roughness.Value, v => material.Roughness.Value = v, 0.0f, 1.0f)));
                rows.Children.Add(Row(theme, "Ambient",
                    Scalar(() => material.Ambient.Value, v => material.Ambient.Value = v, 0.0f, 1.0f)));

                rows.Children.Add(Row(theme, "Normal mix", Scalar(
                    () => (float)material.NormalSelector,
                    v => material.NormalSelector = v,
                    0.0f,
                    1.0f)));

                rows.Children.Add(Row(theme, "Emissive", Scalar(
                    () => (float)material.EmissionStrength,
                    v => material.EmissionStrength = v,
                    0.0f,
                    25.0f)));

                rows.Children.Add(new CheckBox().Label("Flat shading")
                    .Checked(material.FlatShaded)
                    .OnChanged(value => material.FlatShaded = value));
            }
            else
            {
                rows.Children.Add(new TextBlock()
                    .Text("This shader ignores the standard PBR channels. See Shader Uniforms.")
                    .Wrap()
                    .Foreground(theme.TextMuted));
            }

            var textures = TextureChannels(theme, material);
            if (textures != null)
            {
                rows.Children.Add(textures);
            }

            var uniforms = ShaderUniforms(theme, material);
            if (uniforms != null)
            {
                rows.Children.Add(uniforms);
            }

            return new Expander().Header("Material").Content(rows);
        }

        /// <summary>
        /// The maps a material is wearing, with a thumbnail each. Returns null when it wears none, so a
        /// flat-coloured material is not given an empty section to scroll past.
        /// </summary>
        private static UiElement? TextureChannels(UiTheme theme, ManaMaterial material)
        {
            var channels = new (string Name, ManaTexture? Texture)[]
            {
                ("Albedo", material.Albedo?.TextureValue),
                ("Normal", material.Normal?.TextureValue),
                ("Emission", material.Emission?.TextureValue),
                ("Metallic", material.Metallic?.TextureValue),
                ("Roughness", material.Roughness?.TextureValue),
                ("Ambient", material.Ambient?.TextureValue)
            };

            var present = channels.Where(c => c.Texture != null).ToList();
            if (present.Count == 0)
            {
                return null;
            }

            var rows = new StackPanel().Vertical(theme.Spacing);
            foreach (var (name, texture) in present)
            {
                rows.Children.Add(TextureRow(theme, name, texture!));
            }

            return new Expander().Header($"Textures ({present.Count})").Content(rows);
        }

        /// <summary>
        /// One map: a thumbnail, the channel it fills, and where it came from.
        /// <para>
        /// The thumbnail is built on the first frame the row is shown, which means decoding the image --
        /// several thousand pixels square for a scanned asset. It is cached from then on, so the cost lands
        /// once per texture rather than on every rebuild.
        /// </para>
        /// </summary>
        private static UiElement TextureRow(UiTheme theme, string channel, ManaTexture texture)
        {
            var preview = new UiImage { Fit = UiImageFit.Contain }.Size(44.0f, 44.0f);

            // Polled, because the pixels may not exist yet: a texture uploaded straight to the GPU has no
            // CPU copy until something asks, and the atlas it lands in belongs to the root rather than to
            // anything reachable when this row is built.
            preview.BindPolled(image => image.SourceUv, () => Resolve(image: preview, texture).Uv);
            preview.BindPolled(image => image.SourceSize, () => Resolve(image: preview, texture).Size);

            var detail = new TextBlock()
                .BindTextPolled(() =>
                {
                    var found = ManaUiTexturePreviews.TryGet(preview.Root?.Images!, texture, out var p);
                    return found ? $"{p.SourceWidth} x {p.SourceHeight}" : "not loaded";
                })
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall);

            return new Grid().Columns("44, *", theme.Spacing).Rows("Auto").Children(
                new Border()
                    .Fill(theme.SurfaceAlt)
                    .Outline(theme.Border, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Size(44.0f, 44.0f)
                    .Content(preview)
                    .GridColumn(0),

                new StackPanel().Vertical(1.0f).CenterV().GridColumn(1).Children(
                    new TextBlock().Text(channel).FontFamily(theme.FontFamilyMedium),
                    new TextBlock()
                        .Text(FileNameOf(texture))
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .Ellipsis(),
                    detail));
        }

        /// <summary>
        /// Looks the thumbnail up through the element's own root, which is the only way to reach the atlas
        /// the renderer uploads. Cheap after the first call.
        /// </summary>
        private static ManaUiTexturePreview Resolve(UiImage image, ManaTexture texture)
        {
            var atlas = image.Root?.Images;
            if (atlas == null || !ManaUiTexturePreviews.TryGet(atlas, texture, out var preview))
            {
                return default;
            }

            return preview;
        }

        private static string FileNameOf(ManaTexture texture)
        {
            var name = texture.OriginalFilename;
            return string.IsNullOrWhiteSpace(name) ? "embedded" : Path.GetFileName(name);
        }

        /// <summary>
        /// The material's custom shader uniforms, one editor per type. Returns null when there are none to
        /// show, so a PBR material is not given an empty section to scroll past.
        /// <para>
        /// Built once, from the uniforms present when the inspector was created. That matches how the panel
        /// works everywhere else -- selecting a different node rebuilds it -- and a shader's uniform set
        /// does not change while it is being looked at.
        /// </para>
        /// </summary>
        private static UiElement? ShaderUniforms(UiTheme theme, ManaMaterial material)
        {
            material.EnsureShaderMetadataDefaults();

            var editable = material.ShaderUniforms
                .Where(entry => IsEditable(entry.Value.Type))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (editable.Count == 0)
            {
                return null;
            }

            var rows = new StackPanel().Vertical(theme.Spacing);
            foreach (var (name, value) in editable)
            {
                rows.Children.Add(Row(theme, name, UniformEditor(name, value)));
            }

            // Restores from the snapshot rather than removing keys: some render paths assign buffer slots
            // by sorting the uniform names, so dropping one would silently shift every other uniform.
            if (material.DefaultShaderUniforms.Count > 0)
            {
                rows.Children.Add(new Button()
                    .Label("Reset to defaults")
                    .Variant(ButtonVariant.Subtle)
                    .OnClick(() =>
                    {
                        foreach (var entry in material.DefaultShaderUniforms)
                        {
                            material.ShaderUniforms[entry.Key] = entry.Value.Clone();
                        }
                    }));
            }

            return new Expander().Header($"Shader Uniforms ({editable.Count})").Content(rows);
        }

        private static bool IsEditable(UniformType type)
        {
            return type is UniformType.Float
                or UniformType.FloatVec2
                or UniformType.FloatVec3
                or UniformType.FloatVec4
                or UniformType.Int
                or UniformType.Bool;
        }

        /// <summary>
        /// An editor matched to the uniform's type, and to its name for vectors -- a "colour" or "tint"
        /// gets a swatch rather than three numbers, which is the same heuristic the ImGui inspector uses so
        /// the two panels agree about what a uniform is.
        /// </summary>
        private static UiElement UniformEditor(string name, ManaMaterialUniformValue value)
        {
            var isColour = name.Contains("color", StringComparison.OrdinalIgnoreCase)
                || name.Contains("colour", StringComparison.OrdinalIgnoreCase)
                || name.Contains("tint", StringComparison.OrdinalIgnoreCase);

            switch (value.Type)
            {
                case UniformType.Bool:
                    return new CheckBox()
                        .Checked(value.BoolValue)
                        .OnChanged(v => value.BoolValue = v);

                case UniformType.Int:
                {
                    var box = new NumericBox().Integer().Value(value.IntValue);
                    box.ValueChanged += (_, v) => value.IntValue = (int)v;
                    box.BindPolled(b => b.Value, () => value.IntValue);
                    return box;
                }

                case UniformType.FloatVec2:
                {
                    var box = new VectorBox().Components(2).Step(0.01f).Places(3);
                    box.Value2 = value.Vector2Value;
                    box.ValueChanged += (_, v) => value.Vector2Value = new Vector2(v.X, v.Y);
                    box.BindPolled(b => b.Value2, () => value.Vector2Value);
                    return box;
                }

                case UniformType.FloatVec3 when isColour:
                    return Colour(
                        () => new Vector4(value.Vector3Value, 1.0f),
                        v => value.Vector3Value = new Vector3(v.X, v.Y, v.Z));

                case UniformType.FloatVec3:
                {
                    var box = new VectorBox().Step(0.01f).Places(3);
                    box.Value = value.Vector3Value;
                    box.ValueChanged += (_, v) => value.Vector3Value = new Vector3(v.X, v.Y, v.Z);
                    box.BindPolled(b => b.Value, () => value.Vector3Value);
                    return box;
                }

                case UniformType.FloatVec4 when isColour:
                    return Colour(() => value.Vector4Value, v => value.Vector4Value = v);

                case UniformType.FloatVec4:
                {
                    var box = new VectorBox().Components(4).Step(0.01f).Places(3);
                    box.Vector = value.Vector4Value;
                    box.ValueChanged += (_, v) => value.Vector4Value = v;
                    box.BindPolled(b => b.Vector, () => value.Vector4Value);
                    return box;
                }

                default:
                {
                    var box = new NumericBox().Step(0.01f).Places(3).Value(value.FloatValue);
                    box.ValueChanged += (_, v) => value.FloatValue = v;
                    box.BindPolled(b => b.Value, () => value.FloatValue);
                    return box;
                }
            }
        }

        /// <summary>
        /// A ranged scalar: a slider with the number beside it. The range is soft, so a value typed past
        /// either end widens the slider rather than being clamped -- an emissive strength offering 0..25
        /// still accepts 200 and rescales around it.
        /// </summary>
        private static SliderBox Scalar(Func<float> read, Action<float> write, float minimum, float maximum)
        {
            var slider = new SliderBox
            {
                Minimum = minimum,
                Maximum = maximum,
                Step = (maximum - minimum) / 200.0f,
                DecimalPlaces = 3
            };

            slider.Value = read();
            slider.ValueChanged += (_, value) => write(value);
            slider.BindPolled(s => s.Value, read);
            return slider;
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

                    Row(theme, "Intensity", Scalar(
                        () => light.Intensity,
                        value => light.Intensity = value,
                        0.0f,
                        20.0f)),

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
