using Mana.Code.Input;

namespace ManaVoxel.iOS;

/// <summary>The phone's Taptic Engine behind <see cref="ManaRumble"/>: a tick for a tap, light to heavy impacts for the rest.</summary>
public sealed class IosHaptics : IManaHaptics
{
    private readonly UISelectionFeedbackGenerator _tap;
    private readonly UIImpactFeedbackGenerator _light;
    private readonly UIImpactFeedbackGenerator _medium;
    private readonly UIImpactFeedbackGenerator _heavy;

    /// <summary>Installs the phone's haptics, attached to the game's view (where the feedback is felt from).</summary>
    public static void Install(UIView view) => ManaRumble.Haptics = new IosHaptics(view);

    public IosHaptics(UIView view)
    {
        _tap = UISelectionFeedbackGenerator.GetFeedbackGenerator(view);
        _light = UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Light, view);
        _medium = UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Medium, view);
        _heavy = UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Heavy, view);
        // Warmed up, so the first one is not late.
        _tap.Prepare();
        _light.Prepare();
        _medium.Prepare();
        _heavy.Prepare();
    }

    public void Play(ManaRumbleStrength strength)
    {
        switch (strength)
        {
            case ManaRumbleStrength.Tap:
                _tap.SelectionChanged();
                _tap.Prepare();
                break;
            case ManaRumbleStrength.Light:
                _light.ImpactOccurred();
                _light.Prepare();
                break;
            case ManaRumbleStrength.Medium:
                _medium.ImpactOccurred();
                _medium.Prepare();
                break;
            default:
                _heavy.ImpactOccurred();
                _heavy.Prepare();
                break;
        }
    }
}
