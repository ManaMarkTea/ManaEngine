using Mana;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The camera's recent mouse-look history, as its own section beside <see cref="CameraWidget"/>'s
    /// settings -- registering a second widget for the same subject rather than folding this into
    /// CameraWidget, the same way MaterialWidget and PbrShaderWidget split a material's sections.
    /// <para>
    /// Position and rotation aren't repeated here: they're already the node's own <see cref="TransformWidget"/>
    /// section, which applies to the camera's node the same as any other. The ImGui version's Pitch/Yaw
    /// fields and its "Advanced" raw matrix editor aren't ported -- neither ever actually wrote back an
    /// edit (the drag targets were local copies that nothing used), so there was no real functionality
    /// there to carry over, only the appearance of some.
    /// </para>
    /// </summary>
    public sealed class CameraMouseInputWidget : InspectorWidget<CameraComponent>
    {
        protected override UiElement Build(CameraComponent camera, InspectorContext context)
        {
            var theme = context.Theme;

            var dx = new Sparkline { Minimum = -0.01f, Maximum = 0.01f };
            dx.BindPolled(s => s.Samples, () => camera.GetMouseDeltaHistoryXSnapshot());

            var dy = new Sparkline { Minimum = -0.01f, Maximum = 0.01f };
            dy.BindPolled(s => s.Samples, () => camera.GetMouseDeltaHistoryYSnapshot());

            return new Expander()
                .Header("Camera Mouse Input")
                .Section(theme.Spacing,
                    new TextBlock().Text("dx").Foreground(theme.TextMuted),
                    dx,
                    new TextBlock().Text("dy").Foreground(theme.TextMuted),
                    dy);
        }
    }
}
