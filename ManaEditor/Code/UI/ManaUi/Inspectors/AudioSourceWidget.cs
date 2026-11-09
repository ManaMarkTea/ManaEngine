using Mana;
using Mana.Code.Components;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors;

public sealed class AudioSourceWidget : InspectorWidget<ManaAudioSourceComponent>
{
    protected override UiElement Build(ManaAudioSourceComponent source, InspectorContext context)
    {
        var reflected = new ReflectedPropertiesWidget();
        reflected.IgnoredDeclaringTypes.Add(typeof(ManaComponent));
        reflected.IgnoredDeclaringTypes.Add(typeof(Mana.Code.ManaObject));
        reflected.IgnoredNames.Add(nameof(source.LoadError));
        var section = (Expander)reflected.Build(source, context);
        section.BindPolled(x => x.Header, () => $"Audio: {source.Cue} — {Path.GetFileName(source.ClipPath)}");
        var settings = section.Content;
        section.Content = null;
        var rows = new StackPanel().Vertical(context.Theme.Spacing);
        string? previewError = null;
        var play = new ManaUI.Button().Label("Preview").OnClick(async () =>
        {
            try { previewError = null; await source.PreviewAsync(); }
            catch (Exception ex) { previewError = ex.Message; }
        });
        play.BindPolled(x => x.IsEnabled, () => source.Context.Audio.IsAvailable);
        rows.Children.Add(new StackPanel().Horizontal(context.Theme.Spacing).Children(
            play, new ManaUI.Button().Label("Stop").OnClick(source.Stop)));
        var status = new TextBlock();
        status.BindPolled(x => x.Text, () => previewError ?? source.LoadError ??
            (source.Voice is { IsPlaying: true } ? "Playing" : ""));
        rows.Children.Add(status);
        if (settings != null) { rows.Children.Add(settings); }
        section.Content = rows;
        return section;
    }
}
