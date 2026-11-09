using Mana.Code.Materials;
using ManaUI;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

using UniformType = ManaCore.Graphics.ManaUniformType;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A material's custom shader uniforms, one editor per type.
    /// <para>
    /// Claims a material only when it has editable uniforms, so a plain PBR material is not given an empty
    /// section to scroll past.
    /// </para>
    /// <para>
    /// Built from the uniforms present when the inspector was created. That matches how the panel works
    /// everywhere else -- selecting a different node rebuilds it -- and a shader's uniform set does not
    /// change while it is being looked at.
    /// </para>
    /// </summary>
    public sealed class ShaderUniformsWidget : InspectorWidget<ManaMaterial>
    {
        protected override bool Accepts(ManaMaterial material)
        {
            material.EnsureShaderMetadataDefaults();
            return Editable(material).Count > 0;
        }

        protected override UiElement Build(ManaMaterial material, InspectorContext context)
        {
            var theme = context.Theme;
            var editable = Editable(material);

            var rows = new StackPanel().Vertical(theme.Spacing);
            foreach (var (name, value) in editable)
            {
                rows.Children.Add(InspectorControls.Row(context, name, Editor(name, value)));
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

        private static List<KeyValuePair<string, ManaMaterialUniformValue>> Editable(ManaMaterial material)
        {
            return material.ShaderUniforms
                .Where(entry => IsEditable(entry.Value.Type))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
        private static UiElement Editor(string name, ManaMaterialUniformValue value)
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
                    return InspectorControls.Colour(
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
                    return InspectorControls.Colour(() => value.Vector4Value, v => value.Vector4Value = v);

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
    }
}
