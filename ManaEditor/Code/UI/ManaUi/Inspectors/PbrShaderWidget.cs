using Mana;
using ManaCore.Graphics;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The channels a PBR shader reads, each value paired with the map that modulates it.
    /// <para>
    /// Offered only for the shader that actually reads them, which is why acceptance looks at the
    /// material's state rather than only its type: a material whose shader ignores these would take an
    /// assignment and quietly do nothing with it.
    /// </para>
    /// <para>
    /// Each map sits on the row of the value it modulates rather than in a section of its own further
    /// down -- which value a map belonged to was the thing that was hardest to tell when the two were
    /// separated -- and a channel is a row of data here rather than a branch, so adding one is a line in
    /// the table.
    /// </para>
    /// </summary>
    public sealed class PbrShaderWidget : InspectorWidget<ManaMaterial>
    {
        protected override bool Accepts(ManaMaterial material)
        {
            return material.Shader is ManaPbrShader;
        }

        protected override UiElement Build(ManaMaterial material, InspectorContext context)
        {
            var theme = context.Theme;
            var rows = new StackPanel().Vertical(theme.Spacing);

            foreach (var (label, editor, map) in Channels(material))
            {
                var slot = map == null
                    ? null
                    : TextureSlotControl.Build(
                        theme,
                        label,
                        () => map.TextureValue,
                        texture => map.TextureValue = texture,
                        context.Engine);

                rows.Children.Add(InspectorControls.Row(context, label, editor, slot, Explain(label)));
            }

            // Beside Opacity, because a map with no mode does nothing and that is exactly the trap.
            var modes = Enum.GetNames<ManaOpacityMode>();
            var opacityMode = new ComboBox().Options(modes);
            opacityMode.SelectedIndex = Math.Max(0, Array.IndexOf(modes, material.OpacityMode.ToString()));
            opacityMode.SelectionChanged += (_, _) =>
            {
                if (!opacityMode.IsApplyingBinding
                    && opacityMode.SelectedItem is string name
                    && Enum.TryParse<ManaOpacityMode>(name, out var parsed))
                {
                    material.OpacityMode = parsed;
                }
            };
            opacityMode.BindPolled(
                c => c.SelectedIndex,
                () => Math.Max(0, Array.IndexOf(modes, material.OpacityMode.ToString())));

            rows.Children.Add(InspectorControls.Row(context, "Opacity mode", opacityMode, null,
                "Auto cuts out as soon as there is a map or a value below one. Cutout discards below the "
                + "cutoff and is otherwise solid -- foliage, grates. Blend is real transparency and needs "
                + "back-to-front ordering to look right."));

            var cutoff = InspectorControls.Scalar(
                () => (float)material.AlphaCutoff,
                value => material.AlphaCutoff = value,
                0.0f,
                1.0f);

            // Only means anything in the mode that reads it.
            cutoff.BindPolled(
                c => c.Visibility,
                () => material.EffectiveOpacityMode == ManaOpacityMode.Cutout
                    ? Visibility.Visible
                    : Visibility.Collapsed);

            rows.Children.Add(InspectorControls.Row(context, "Alpha cutoff", cutoff, null,
                "Alpha below this is discarded; anything above is fully solid."));

            rows.Children.Add(InspectorControls.Row(context, "UV scale",
                InspectorControls.Vector2(
                    () => material.UvScale,
                    value => material.UvScale = value,
                    step: 0.1f),
                null,
                "How many times a texture repeats across the surface. Applies to every map."));

            rows.Children.Add(InspectorControls.Row(context, "UV offset",
                InspectorControls.Vector2(
                    () => material.UvOffset,
                    value => material.UvOffset = value,
                    step: 0.01f),
                null,
                "Shifts every map across the surface."));

            rows.Children.Add(InspectorControls.Row(context, "Inset depth",
                InspectorControls.Scalar(() => material.InsetDepth, value => material.InsetDepth = value, 0.0f, 1.0f),
                null, "Apparent recess in metres. Try 0.02 to 0.1. Zero disables parallax. "
                    + "Moves all maps together; geometry, silhouettes and shadows stay at the surface. Best on opaque panels."));

            rows.Children.Add(new CheckBox().Label("Flat shading")
                .Checked(material.FlatShaded)
                .OnChanged(value => material.FlatShaded = value));

            // Polled: a Sprite sets its material's Unlit from its own setting, so the box follows that too.
            var unlit = new CheckBox().Label("Unlit")
                .Checked(material.Unlit)
                .OnChanged(value => material.Unlit = value)
                .WithTooltip("Show the colour as drawn, ignoring every light -- flat art like 2D sprites.");
            unlit.BindPolled(box => box.IsChecked, () => material.Unlit);
            rows.Children.Add(unlit);

            return new Expander().Header("PBR Channels").Content(rows);
        }

        /// <summary>
        /// What a channel is for, shown on hover. Written here rather than read from a doc comment
        /// because these are the shader's channels, not a C# property's.
        /// </summary>
        private static string? Explain(string label)
        {
            return label switch
            {
                "Albedo" => "Base colour. A map here is multiplied by the colour beside it.",
                "Emission" => "Colour this surface gives off on its own, before Emissive scales it.",
                "Metallic" => "0 is a dielectric, 1 is bare metal. Values in between are rarely physical.",
                "Roughness" => "How scattered reflections are. 0 is a mirror, 1 is matte.",
                "Occlusion" => "How much indirect light reaches the surface. 1 is fully lit, 0 is fully shadowed.",
                "Normal" => "How strongly the map perturbs the surface. 1 applies it fully, 0 ignores it.",
                "Emissive" => "Multiplies the emission colour. Above 1 it blooms.",
                "Opacity" => "How solid the surface is. A map here cuts holes without changing the geometry.",
                _ => null
            };
        }

        /// <summary>
        /// The channels as data: a label, the editor for the value, and the map that modulates it. A row
        /// with no map of its own leaves that null.
        /// </summary>
        private static IEnumerable<(string Label, UiElement Editor, ManaTextureDynamic? Map)> Channels(
            ManaMaterial material)
        {
            yield return ("Albedo",
                InspectorControls.Colour(() => material.Albedo.Value, v => material.Albedo.Value = v),
                material.Albedo);

            yield return ("Emission",
                InspectorControls.Colour(() => material.Emission.Value, v => material.Emission.Value = v),
                material.Emission);

            yield return ("Metallic",
                InspectorControls.Scalar(() => material.Metallic.Value, v => material.Metallic.Value = v, 0.0f, 1.0f),
                material.Metallic);

            yield return ("Roughness",
                InspectorControls.Scalar(() => material.Roughness.Value, v => material.Roughness.Value = v, 0.0f, 1.0f),
                material.Roughness);

            yield return ("Occlusion",
                InspectorControls.Scalar(() => material.Occlusion.Value, v => material.Occlusion.Value = v, 0.0f, 1.0f),
                material.Occlusion);

            // Shown the way round anyone would expect. The stored NormalSelector is a mix toward the
            // vertex normal, so 0 there means "use the map fully" -- which reads on screen as a normal map
            // that is switched off, and is exactly what a loaded glTF looks like.
            yield return ("Normal",
                InspectorControls.Scalar(
                    () => 1.0f - (float)material.NormalSelector,
                    v => material.NormalSelector = 1.0f - v,
                    0.0f,
                    1.0f),
                material.Normal);

            yield return ("Opacity",
                InspectorControls.Scalar(() => material.Opacity.Value, v => material.Opacity.Value = v, 0.0f, 1.0f),
                material.Opacity);

            // Strength rather than a channel, so there is no map to pair it with.
            yield return ("Emissive",
                InspectorControls.Scalar(
                    () => (float)material.EmissionStrength,
                    v => material.EmissionStrength = v,
                    0.0f,
                    25.0f),
                null);
        }
    }
}
