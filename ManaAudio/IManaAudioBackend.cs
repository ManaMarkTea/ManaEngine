using System.Numerics;
using ManaCore.Audio;

namespace ManaAudio;

/// <summary>Playback commands only: backends own their buffers and release them on disposal.</summary>
public interface IManaAudioBackend : IDisposable
{
    bool IsAvailable { get; }
    bool IsUnlocked { get; }
    int Play(ManaAudioClip clip, ManaAudioPlayOptions options);
    bool IsPlaying(int voice);
    void Configure(int voice, float gain, float pitch, Vector3? position);
    void Stop(int voice);
    void SetListener(Vector3 position, Vector3 forward, Vector3 up);
}

public sealed class ManaNullAudioBackend : IManaAudioBackend
{
    public bool IsAvailable => false;
    public bool IsUnlocked => false;
    public int Play(ManaAudioClip clip, ManaAudioPlayOptions options) => 0;
    public bool IsPlaying(int voice) => false;
    public void Configure(int voice, float gain, float pitch, Vector3? position) { }
    public void Stop(int voice) { }
    public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }
    public void Dispose() { }
}
