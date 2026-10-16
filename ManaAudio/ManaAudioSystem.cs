using System.Numerics;
using ManaCore.Assets;
using ManaCore.Audio;

namespace ManaAudio;

/// <summary>
/// Owns one audio device, its clip cache and at most 64 simultaneous voices. Call Update each frame.
/// All device operations are serialized; file decoding may complete on a different thread.
/// </summary>
public sealed class ManaAudioSystem : IManaAudio
{
    private readonly object gate = new();
    private readonly IManaAudioBackend backend;
    private readonly Dictionary<string, Task<ManaAudioClip>> clips = new(StringComparer.Ordinal);
    private readonly List<Voice> voices = new();
    private readonly float[] busVolumes = [1, 1, 1];
    private float masterVolume = 1;
    private bool disposed;
    public const int MaxVoices = 64;

    public ManaAudioSystem(IManaAudioBackend backend) => this.backend = backend;
    public string OutputDeviceName { get { lock (gate) { return backend.OutputDeviceName; } } }
    public bool IsAvailable { get { lock (gate) { return !disposed && backend.IsAvailable; } } }
    public bool IsUnlocked { get { lock (gate) { return !disposed && backend.IsUnlocked; } } }

    public float MasterVolume
    {
        get { lock (gate) { return masterVolume; } }
        set
        {
            Gain(value);
            lock (gate)
            {
                ThrowIfDisposed();
                masterVolume = value;
                RefreshVolumes();
            }
        }
    }

    public Task<ManaAudioClip> LoadClipAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (gate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!clips.TryGetValue(path, out var task) || task.IsFaulted || task.IsCanceled)
            {
                task = ReadClipAsync(path);
                clips[path] = task;
            }
            // One caller cancelling must not cancel another caller's shared load.
            return task.WaitAsync(cancellationToken);
        }
    }

    private static async Task<ManaAudioClip> ReadClipAsync(string path)
    {
        var catalog = ManaAssetCatalog.Current;
        if (catalog != null)
        {
            await catalog.EnsureAsync(path);
        }
        string local = catalog?.SizeOf(path) != null && !Path.IsPathRooted(path)
            ? catalog.LocalFilePath(path)
            : Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(local) && File.Exists(path))
        {
            local = path;
        }
        if (new FileInfo(local).Length > 65 * 1024 * 1024)
        {
            throw new InvalidDataException("WAV file exceeds the buffered clip size limit (65 MiB).");
        }
        return ManaAudioClip.FromWav(await File.ReadAllBytesAsync(local));
    }

    public IManaAudioVoice Play(ManaAudioClip clip, ManaAudioPlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        options ??= new();
        Validate(clip, options);
        lock (gate)
        {
            ThrowIfDisposed();
            Update();
            if (voices.Count >= MaxVoices)
            {
                // A bounded drop policy avoids an unbounded backlog (especially before browser unlock).
                return new Voice(this, 0, options);
            }
            int id = backend.Play(clip, options with { Volume = EffectiveVolume(options.Volume, options.Bus) });
            var voice = new Voice(this, id, options);
            if (id != 0)
            {
                voices.Add(voice);
            }
            return voice;
        }
    }

    public void SetBusVolume(ManaAudioBus bus, float volume)
    {
        Bus(bus);
        Gain(volume);
        lock (gate)
        {
            ThrowIfDisposed();
            busVolumes[(int)bus] = volume;
            RefreshVolumes();
        }
    }

    public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
    {
        Vector(position);
        Vector(forward);
        Vector(up);
        if (forward.LengthSquared() < 0.000001f || up.LengthSquared() < 0.000001f ||
            Vector3.Cross(Vector3.Normalize(forward), Vector3.Normalize(up)).LengthSquared() < 0.000001f)
        {
            throw new ArgumentException("Listener forward and up must be nonzero and not parallel.");
        }
        forward = Vector3.Normalize(forward);
        up = Vector3.Normalize(up - Vector3.Dot(up, forward) * forward);
        lock (gate)
        {
            ThrowIfDisposed();
            backend.SetListener(position, forward, up);
        }
    }

    public void Update()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                if (!backend.IsPlaying(voices[i].Id))
                {
                    StopVoice(voices[i]);
                }
            }
        }
    }

    public void StopAll()
    {
        lock (gate)
        {
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                StopVoice(voices[i]);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            StopAll();
            disposed = true;
            clips.Clear();
            backend.Dispose();
        }
    }

    private void StopVoice(Voice voice)
    {
        if (voice.Id != 0)
        {
            backend.Stop(voice.Id);
            voice.Id = 0;
            voices.Remove(voice);
        }
    }

    private float EffectiveVolume(float volume, ManaAudioBus bus) => volume * masterVolume * busVolumes[(int)bus];
    private void RefreshVolumes()
    {
        foreach (var voice in voices)
        {
            Configure(voice);
        }
    }
    private void Configure(Voice voice)
    {
        if (voice.Id != 0)
        {
            backend.Configure(voice.Id, EffectiveVolume(voice.volume, voice.bus), voice.pitch, voice.position);
        }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    private static void Gain(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Volume must be between zero and one.");
        }
    }
    private static void PitchValue(float value)
    {
        if (!float.IsFinite(value) || value < 0.01f || value > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Pitch must be between 0.01 and 4.");
        }
    }
    private static void Vector(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentException("Audio positions and directions must be finite.");
        }
    }
    private static void Bus(ManaAudioBus bus)
    {
        if ((uint)bus > (uint)ManaAudioBus.Ui)
        {
            throw new ArgumentOutOfRangeException(nameof(bus));
        }
    }
    private static void Validate(ManaAudioClip clip, ManaAudioPlayOptions options)
    {
        Gain(options.Volume);
        PitchValue(options.Pitch);
        Bus(options.Bus);
        if (options.Position is { } position)
        {
            Vector(position);
            if (clip.Channels != 1)
            {
                throw new ArgumentException("Use a mono clip for positional audio.");
            }
        }
        if (!float.IsFinite(options.ReferenceDistance) || options.ReferenceDistance <= 0 ||
            !float.IsFinite(options.MaxDistance) || options.MaxDistance < options.ReferenceDistance ||
            !float.IsFinite(options.Rolloff) || options.Rolloff < 0)
        {
            throw new ArgumentException("Invalid audio attenuation distances or rolloff.");
        }
    }

    private sealed class Voice(ManaAudioSystem owner, int id, ManaAudioPlayOptions options) : IManaAudioVoice
    {
        internal int Id = id;
        internal float volume = options.Volume;
        internal float pitch = options.Pitch;
        internal Vector3? position = options.Position;
        internal readonly ManaAudioBus bus = options.Bus;
        public bool IsPlaying
        {
            get { lock (owner.gate) { return Id != 0 && owner.backend.IsPlaying(Id); } }
        }
        public float Volume
        {
            get { lock (owner.gate) { return volume; } }
            set { Gain(value); lock (owner.gate) { volume = value; owner.Configure(this); } }
        }
        public float Pitch
        {
            get { lock (owner.gate) { return pitch; } }
            set { PitchValue(value); lock (owner.gate) { pitch = value; owner.Configure(this); } }
        }
        public Vector3? Position
        {
            get { lock (owner.gate) { return position; } }
            set
            {
                if (value is { } p) { Vector(p); }
                lock (owner.gate)
                {
                    if (value.HasValue != position.HasValue)
                    {
                        throw new InvalidOperationException("A voice cannot change between spatial and non-spatial playback.");
                    }
                    position = value;
                    owner.Configure(this);
                }
            }
        }
        public void Stop() { lock (owner.gate) { owner.StopVoice(this); } }
        public void Dispose() => Stop();
    }
}
