using System.Numerics;

namespace ManaCore.Audio;

public enum ManaAudioBus { Sfx, Music, Ui }

public sealed record ManaAudioPlayOptions
{
    public bool Loop { get; init; }
    /// <summary>Null means a non-positional sound. Positional clips must be mono.</summary>
    public Vector3? Position { get; init; }
    public float Volume { get; init; } = 1;
    public float Pitch { get; init; } = 1;
    public float ReferenceDistance { get; init; } = 1;
    public float MaxDistance { get; init; } = 100;
    public float Rolloff { get; init; } = 1;
    public ManaAudioBus Bus { get; init; } = ManaAudioBus.Sfx;
}

public interface IManaAudioVoice : IDisposable
{
    bool IsPlaying { get; }
    float Volume { get; set; }
    float Pitch { get; set; }
    Vector3? Position { get; set; }
    void Stop();
}

public interface IManaAudio : IDisposable
{
    string OutputDeviceName { get; }
    bool IsAvailable { get; }
    bool IsUnlocked { get; }
    float MasterVolume { get; set; }
    Task<ManaAudioClip> LoadClipAsync(string path, CancellationToken cancellationToken = default);
    IManaAudioVoice Play(ManaAudioClip clip, ManaAudioPlayOptions? options = null);
    void SetBusVolume(ManaAudioBus bus, float volume);
    void SetListener(Vector3 position, Vector3 forward, Vector3 up);
    void Update();
    void StopAll();
}
