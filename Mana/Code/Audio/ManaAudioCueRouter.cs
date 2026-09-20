using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaCore.Audio;

namespace Mana.Code.Audio;

/// <summary>World-scoped routing. Only components between BeginPlay/EndPlay receive cues.</summary>
public sealed class ManaAudioCueRouter
{
    private readonly HashSet<ManaAudioSourceComponent> sources = new();
    internal void Register(ManaAudioSourceComponent source) => sources.Add(source);
    internal void Unregister(ManaAudioSourceComponent source) => sources.Remove(source);
    public void Emit(ManaSoundCue cue)
    {
        foreach (var source in sources)
        {
            if (source.Global && source.Cue == cue) { source.Trigger(); }
        }
    }
    public static bool EmitAt(ManaSceneNode? node, ManaSoundCue cue, System.Numerics.Vector3? position = null)
    {
        bool matched = false;
        if (node != null)
        {
            foreach (var source in node.Components.OfType<ManaAudioSourceComponent>())
            {
                if (!source.Global && source.Cue == cue)
                {
                    source.Trigger(position);
                    matched = true;
                }
            }
        }
        return matched;
    }
    public static void StopAt(ManaSceneNode node, ManaSoundCue cue)
    {
        foreach (var source in node.Components.OfType<ManaAudioSourceComponent>())
        {
            if (!source.Global && source.Cue == cue) { source.Stop(); }
        }
    }
}
