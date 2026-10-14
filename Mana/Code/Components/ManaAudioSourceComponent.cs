using System.Text.Json.Serialization;
using Mana.Code.Nodes;
using ManaCore.Audio;
using ManaCore.Diagnostics;

namespace Mana.Code.Components;

/// <summary>Serializable emitter. Loading never blocks the game loop or starts stale one-shots.</summary>
[EditorComponent(AllowMultiple = true)]
public sealed class ManaAudioSourceComponent(ManaSceneNode owner) : ManaComponent(owner), IManaAudioEmitter, IDisposable
{
    public const string ScriptType = "AudioSource";
    [SoundPath]
    public string ClipPath { get; set; } = string.Empty;
    /// <summary>Additional clip paths separated by semicolons, played in rotation.</summary>
    public string Variants { get; set; } = string.Empty;
    public ManaSoundCue Cue { get; set; }
    /// <summary>Receive world/player cues rather than this node's cues.</summary>
    public bool Global { get; set; }
    public bool Spatial { get; set; } = true;
    public Vector3 LocalOffset { get => Position; set => Position = value; }
    public bool PlayOnBegin { get; set; } = true;
    public bool Loop { get; set; }
    public float Volume { get; set; } = 1;
    public float Pitch { get; set; } = 1;
    public float ReferenceDistance { get; set; } = 1;
    public float MaxDistance { get; set; } = 100;
    public float Rolloff { get; set; } = 1;
    public ManaAudioBus Bus { get; set; } = ManaAudioBus.Sfx;
    public float FadeSeconds { get; set; } = 0.08f;
    public float DelaySeconds { get; set; }
    [JsonIgnore] public ManaAudioClip? Clip { get; set; }
    [JsonIgnore] public IManaAudioVoice? Voice { get; private set; }
    [JsonIgnore] public string? LoadError { get; private set; }
    private Vector3 WorldPosition => Vector3.Transform(Position, Owner.WorldTransform);
    private Task<ManaAudioClip[]>? loading;
    private string? requestedPaths;
    private ManaAudioClip[] ready = [];
    private readonly List<IManaAudioVoice> oneShots = new();
    private int variant;
    private bool active, wanted, disposed;
    private float delay = -1;
    private Vector3? delayedPosition;

    public static IEnumerable<string> Paths(string clipPath, string variants) =>
        (clipPath + ";" + variants).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal);

    public Task PrepareAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string signature = ClipPath + ";" + Variants;
        if (requestedPaths != signature)
        {
            StopImmediately();
            requestedPaths = signature;
            ready = [];
            variant = 0;
            LoadError = null;
            try
            {
                loading = Task.WhenAll(Paths(ClipPath, Variants).Select(path => Context.Audio.LoadClipAsync(path)));
            }
            catch (Exception ex)
            {
                loading = Task.FromException<ManaAudioClip[]>(ex);
            }
            // Observe failures even if the node is removed before its next update.
            _ = loading.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        return loading ?? Task.CompletedTask;
    }

    private ManaAudioClip? NextClip()
    {
        PollLoad();
        if (ready.Length == 0) { return Clip; }
        var next = ready[variant];
        variant = (variant + 1) % ready.Length;
        return next;
    }

    private void PollLoad()
    {
        if (loading is { IsCompletedSuccessfully: true })
        {
            ready = loading.Result;
            loading = null;
        }
        else if (loading is { IsFaulted: true })
        {
            LoadError = loading.Exception.GetBaseException().Message;
            ManaLog.WriteDiagnostic($"Audio source '{Owner.Name}' ({ClipPath}): {LoadError}");
            loading = null;
        }
    }

    private ManaAudioPlayOptions Options(bool loop, Vector3? at = null) => new()
    {
        Position = Spatial ? at ?? WorldPosition : null,
        Loop = loop, Volume = Volume, Pitch = Pitch, ReferenceDistance = ReferenceDistance,
        MaxDistance = MaxDistance, Rolloff = Rolloff, Bus = Bus
    };

    public void Play()
    {
        _ = PrepareAsync();
        wanted = true;
        if (Voice is { IsPlaying: true }) { return; }
        var clip = NextClip();
        if (clip == null)
        {
            wanted = Loop; // Only loops may wait for loading.
            return;
        }
        Voice?.Dispose();
        Voice = Context.Audio.Play(clip, Options(Loop) with { Volume = Loop && FadeSeconds > 0 ? 0 : Volume });
    }

    public IManaAudioVoice? PlayOneShot() => PlayOneShotAt(null);
    public async Task PreviewAsync()
    {
        await PrepareAsync();
        if (!disposed) { Play(); }
    }

    private IManaAudioVoice? PlayOneShotAt(Vector3? at)
    {
        _ = PrepareAsync();
        var clip = NextClip();
        if (clip == null) { return null; }
        var voice = Context.Audio.Play(clip, Options(false, at));
        oneShots.Add(voice);
        return voice;
    }

    internal void Trigger(Vector3? at = null)
    {
        if (!active || disposed) { return; }
        if (Loop) { Play(); }
        else if (DelaySeconds > 0)
        {
            delay = DelaySeconds;
            delayedPosition = at;
        }
        else { PlayOneShotAt(at); }
    }

    public override void BeginPlay()
    {
        _ = PrepareAsync();
        active = true;
        Context.World?.AudioCues.Register(this);
        if (PlayOnBegin) { Play(); }
    }

    public override void UpdateComponent()
    {
        if (disposed) { return; }
        _ = PrepareAsync();
        PollLoad();
        float dt = Math.Clamp((float)ManaTime.GlobalTime.DeltaTime.TotalSeconds, 0, 0.1f);
        if (active && delay >= 0)
        {
            delay -= dt;
            if (delay <= 0)
            {
                delay = -1;
                PlayOneShotAt(delayedPosition);
            }
        }
        if (wanted && Loop && Context.Audio.IsAvailable && Voice is not { IsPlaying: true } && (ready.Length > 0 || Clip != null)) { Play(); }
        if (Voice is { IsPlaying: true } voice)
        {
            if (voice.Position.HasValue) { voice.Position = WorldPosition; }
            float target = wanted ? Volume : 0;
            float step = FadeSeconds > 0 ? dt / FadeSeconds : 1;
            voice.Volume = Math.Clamp(voice.Volume + Math.Clamp(target - voice.Volume, -step, step), 0, 1);
            if (voice.Pitch != Pitch) { voice.Pitch = Pitch; }
            if (!wanted && voice.Volume <= 0) { voice.Dispose(); Voice = null; }
        }
        for (int i = oneShots.Count - 1; i >= 0; i--)
        {
            if (!oneShots[i].IsPlaying) { oneShots[i].Dispose(); oneShots.RemoveAt(i); }
        }
    }

    public void Stop()
    {
        wanted = false;
        delay = -1;
        foreach (var voice in oneShots) { voice.Dispose(); }
        oneShots.Clear();
        if (!Loop || FadeSeconds <= 0) { StopImmediately(); }
    }

    private void StopImmediately()
    {
        wanted = false;
        delay = -1;
        Voice?.Dispose();
        Voice = null;
        foreach (var voice in oneShots) { voice.Dispose(); }
        oneShots.Clear();
    }

    public override void EndPlay()
    {
        active = false;
        Context.World?.AudioCues.Unregister(this);
        StopImmediately();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        EndPlay();
        disposed = true;
    }
}
