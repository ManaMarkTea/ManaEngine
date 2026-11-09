using System.Numerics;
using AVFoundation;
using ManaAudio;
using ManaCore.Audio;

namespace ManaEditor.iOS;

/// <summary>
/// Sound on the phone, through AVAudioEngine (iOS has no OpenAL to speak of): a player node per voice through a
/// varispeed (pitch) into the main mixer. Positional sound is worked out here as OpenAL would -- inverse-distance
/// falloff from the listener, panned by which side of it the sound is on.
/// </summary>
public sealed class IosAudioBackend : IManaAudioBackend
{
    private sealed class Voice
    {
        public required AVAudioPlayerNode Player { get; init; }
        public required AVAudioUnitVarispeed Speed { get; init; }
        public required ManaAudioPlayOptions Options { get; init; }
        public volatile bool Finished;
        public float Gain;
        public Vector3? Position;
    }

    private readonly AVAudioEngine _engine = new();
    private readonly Dictionary<ManaAudioClip, AVAudioPcmBuffer> _buffers = new();
    private readonly Dictionary<int, Voice> _voices = new();
    private int _nextVoice = 1;
    private Vector3 _listener;
    private Vector3 _listenerRight = Vector3.UnitX;
    private bool _started;

    public static void Install() => ManaAudioFactory.PlatformBackend = () => new IosAudioBackend();

    public IosAudioBackend()
    {
        // Playback rather than ambient: a game is played with the sound on, ringer switch or not; others' audio mixes in.
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers, out _);
        session.SetActive(true, out _);
        _ = _engine.MainMixerNode;
        _engine.Prepare();
        _started = _engine.StartAndReturnError(out var error);
        if (!_started)
        {
            Console.WriteLine($"[IosAudio] The audio engine did not start: {error?.LocalizedDescription}");
        }
    }

    public bool IsAvailable => _started;
    public bool IsUnlocked => _started;

    public int Play(ManaAudioClip clip, ManaAudioPlayOptions options)
    {
        if (!_started)
        {
            return 0;
        }
        var buffer = BufferFor(clip);
        var player = new AVAudioPlayerNode();
        var speed = new AVAudioUnitVarispeed();
        _engine.AttachNode(player);
        _engine.AttachNode(speed);
        // The overloads that report errors arrive with iOS 27; these are the ones iOS 18 to 26 have.
#pragma warning disable CA1422
        _engine.Connect(player, speed, buffer.Format);
        _engine.Connect(speed, _engine.MainMixerNode, buffer.Format);

        var id = _nextVoice++;
        var voice = new Voice { Player = player, Speed = speed, Options = options };
        _voices[id] = voice;
        player.ScheduleBuffer(buffer, null, options.Loop ? AVAudioPlayerNodeBufferOptions.Loops : 0, () => voice.Finished = true);
        Apply(voice, options.Volume, options.Pitch, options.Position);
        player.Play();
#pragma warning restore CA1422
        return id;
    }

    /// <summary>The clip as float samples in the engine's own layout, made once per clip.</summary>
    private AVAudioPcmBuffer BufferFor(ManaAudioClip clip)
    {
        if (_buffers.TryGetValue(clip, out var cached))
        {
            return cached;
        }
        var channels = (uint)Math.Clamp(clip.Channels, 1, 2);
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, clip.SampleRate, channels, interleaved: false);
        var frames = (uint)(clip.Samples.Length / clip.Channels);
        var buffer = new AVAudioPcmBuffer(format, frames) { FrameLength = frames };
        var samples = clip.Samples.Span;
        unsafe
        {
            var data = (float**)buffer.FloatChannelData;
            for (var c = 0; c < channels; c++)
            {
                var channel = data[c];
                for (var i = 0; i < frames; i++)
                {
                    channel[i] = samples[i * clip.Channels + c] / 32768.0f;
                }
            }
        }
        _buffers[clip] = buffer;
        return buffer;
    }

    private void Apply(Voice voice, float gain, float pitch, Vector3? position)
    {
        voice.Gain = gain;
        voice.Position = position;
        voice.Speed.Rate = Math.Clamp(pitch, 0.25f, 4.0f);
        if (position is not { } at)
        {
            voice.Player.Volume = gain;
            voice.Player.Pan = 0.0f;
            return;
        }
        // OpenAL's inverse distance, clamped: full within the reference distance, falling off beyond it.
        var options = voice.Options;
        var distance = Math.Clamp(Vector3.Distance(at, _listener), options.ReferenceDistance, MathF.Max(options.ReferenceDistance, options.MaxDistance));
        var falloff = options.ReferenceDistance / (options.ReferenceDistance + options.Rolloff * (distance - options.ReferenceDistance));
        voice.Player.Volume = gain * falloff;
        var toward = at - _listener;
        voice.Player.Pan = toward.LengthSquared() < 1e-4f ? 0.0f : Math.Clamp(Vector3.Dot(Vector3.Normalize(toward), _listenerRight), -1.0f, 1.0f) * 0.8f;
    }

    public bool IsPlaying(int voice) => _voices.TryGetValue(voice, out var playing) && !playing.Finished;

    public void Configure(int voice, float gain, float pitch, Vector3? position)
    {
        if (_voices.TryGetValue(voice, out var playing))
        {
            Apply(playing, gain, pitch, position);
        }
    }

    public void Stop(int voice)
    {
        if (!_voices.Remove(voice, out var playing))
        {
            return;
        }
        playing.Player.Stop();
        _engine.DetachNode(playing.Player);
        _engine.DetachNode(playing.Speed);
    }

    public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
    {
        _listener = position;
        var right = Vector3.Cross(forward, up);
        _listenerRight = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        foreach (var voice in _voices.Values)
        {
            if (voice.Position != null)
            {
                Apply(voice, voice.Gain, voice.Speed.Rate, voice.Position);
            }
        }
    }

    public void Dispose()
    {
        foreach (var id in _voices.Keys.ToArray())
        {
            Stop(id);
        }
        _engine.Stop();
        _engine.Dispose();
    }
}
