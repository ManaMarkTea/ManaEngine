using Mana;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Clip picker and playback controls for a rigged character. Registering this claims
    /// <see cref="ManaSkeletalAnimationComponent"/> away from the generic reflection fallback, which could
    /// only ever show the scalar fields (Time/Speed/Looping/Playing/Blend Duration) -- Clips and ActiveClip
    /// are get-only and non-primitive, so the clip list itself never made it through reflection.
    /// </summary>
    public sealed class SkeletalAnimationWidget : InspectorWidget<ManaSkeletalAnimationComponent>
    {
        protected override UiElement Build(ManaSkeletalAnimationComponent animation, InspectorContext context)
        {
            var theme = context.Theme;

            var clipNames = animation.Clips.Select(clip => clip.Name).ToArray();
            var clipCombo = new ComboBox { Placeholder = "None" }.Options(clipNames);
            clipCombo.SelectedIndex = SelectedClipIndex(animation, clipNames);
            clipCombo.SelectionChanged += (_, _) =>
            {
                if (!clipCombo.IsApplyingBinding && clipCombo.SelectedItem is string name)
                {
                    animation.Play(name);
                }
            };
            clipCombo.BindPolled(c => c.SelectedIndex, () => SelectedClipIndex(animation, clipNames));

            var activeDuration = (float)(animation.ActiveClip?.DurationSeconds ?? 0.0);

            return new Expander()
                .Header("Skeletal Animation")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Clip", clipCombo, null,
                        animation.Clips.Count == 0
                            ? "This rig has no animation clips."
                            : $"{animation.Clips.Count} clip(s) available. Selecting one plays it."),

                    new CheckBox().Label("Playing")
                        .Checked(animation.IsPlaying)
                        .OnChanged(value => animation.IsPlaying = value),

                    new CheckBox().Label("Looping")
                        .Checked(animation.Looping)
                        .OnChanged(value => animation.Looping = value),

                    InspectorControls.Row(context, "Speed", InspectorControls.Scalar(
                        () => animation.PlaybackSpeed,
                        value => animation.PlaybackSpeed = value,
                        0.0f,
                        4.0f)),

                    InspectorControls.Row(context, "Time", InspectorControls.Scalar(
                        () => (float)animation.TimeSeconds,
                        value => animation.TimeSeconds = value,
                        0.0f,
                        MathF.Max(activeDuration, 0.01f))),

                    InspectorControls.Row(context, "Blend Duration", InspectorControls.Scalar(
                        () => animation.BlendDurationSeconds,
                        value => animation.BlendDurationSeconds = value,
                        0.0f,
                        2.0f)));
        }

        private static int SelectedClipIndex(ManaSkeletalAnimationComponent animation, string[] clipNames)
        {
            return animation.ActiveClip == null ? -1 : Array.IndexOf(clipNames, animation.ActiveClip.Name);
        }
    }
}
