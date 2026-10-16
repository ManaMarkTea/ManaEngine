#if !MANA_BROWSER
using System.Numerics;
using ManaCore.Audio;
using Silk.NET.OpenAL;

namespace ManaAudio.Desktop;

/// <summary>OpenAL Soft owns mixing; no managed audio callback or render-thread sample pump.</summary>
public sealed unsafe class ManaOpenAlBackend : IManaAudioBackend
{
    private ALContext? alc;
    private AL? al;
    private Device* device;
    private Context* context;
    private readonly Dictionary<ManaAudioClip, uint> buffers = new();
    private readonly HashSet<uint> sources = new();
    public string OutputDeviceName { get; private set; } = string.Empty;
    public bool IsAvailable => context != null;
    public bool IsUnlocked => IsAvailable;

    public ManaOpenAlBackend()
    {
        try
        {
            alc = ALContext.GetApi(soft: true);
            device = alc.OpenDevice(null);
            if (device == null)
            {
                throw new InvalidOperationException("OpenAL could not open the default output device.");
            }
            context = alc.CreateContext(device, (int*)null);
            if (context == null || !alc.MakeContextCurrent(context))
            {
                throw new InvalidOperationException("OpenAL could not create an audio context.");
            }
            // ALC_ALL_DEVICES_SPECIFIER names the endpoint; the basic specifier may only say "OpenAL Soft".
            var deviceNameToken = alc.IsExtensionPresent(device, "ALC_ENUMERATE_ALL_EXT")
                ? (GetContextString)0x1013 : GetContextString.DeviceSpecifier;
            OutputDeviceName = alc.GetContextProperty(device, deviceNameToken)
                ?? alc.GetContextProperty(device, GetContextString.DeviceSpecifier) ?? "Default output";
            al = AL.GetApi(soft: true);
            al.DistanceModel(DistanceModel.InverseDistanceClamped);
            al.DopplerFactor(0); // Match the shared API, which currently has no velocity input.
            Check();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private AL Bind()
    {
        ObjectDisposedException.ThrowIf(context == null || alc == null || al == null, this);
        if (!alc.MakeContextCurrent(context))
        {
            throw new InvalidOperationException("Could not bind the OpenAL context.");
        }
        return al;
    }

    private void Check()
    {
        var error = al?.GetError() ?? AudioError.NoError;
        if (error != AudioError.NoError)
        {
            throw new InvalidOperationException($"OpenAL error: {error}");
        }
    }

    public int Play(ManaAudioClip clip, ManaAudioPlayOptions options)
    {
        var api = Bind();
        if (!buffers.TryGetValue(clip, out uint buffer))
        {
            buffer = api.GenBuffer();
            try
            {
                fixed (short* pcm = clip.Samples.Span)
                {
                    api.BufferData(buffer, clip.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16,
                        pcm, clip.Samples.Length * sizeof(short), clip.SampleRate);
                }
                Check();
                buffers.Add(clip, buffer);
            }
            catch
            {
                api.DeleteBuffer(buffer);
                throw;
            }
        }
        uint source = api.GenSource();
        try
        {
            Check();
            api.SetSourceProperty(source, SourceInteger.Buffer, buffer);
            api.SetSourceProperty(source, SourceBoolean.Looping, options.Loop);
            api.SetSourceProperty(source, SourceBoolean.SourceRelative, !options.Position.HasValue);
            api.SetSourceProperty(source, SourceFloat.ReferenceDistance, options.ReferenceDistance);
            api.SetSourceProperty(source, SourceFloat.MaxDistance, options.MaxDistance);
            api.SetSourceProperty(source, SourceFloat.RolloffFactor, options.Position.HasValue ? options.Rolloff : 0);
            Apply(source, options.Volume, options.Pitch, options.Position);
            api.SourcePlay(source);
            Check();
            sources.Add(source);
            return checked((int)source);
        }
        catch
        {
            api.DeleteSource(source);
            throw;
        }
    }

    private void Apply(uint source, float gain, float pitch, Vector3? position)
    {
        var api = Bind();
        api.SetSourceProperty(source, SourceFloat.Gain, gain);
        api.SetSourceProperty(source, SourceFloat.Pitch, pitch);
        var p = position ?? Vector3.Zero;
        api.SetSourceProperty(source, SourceVector3.Position, p.X, p.Y, p.Z);
    }

    public bool IsPlaying(int voice)
    {
        if (!sources.Contains((uint)voice))
        {
            return false;
        }
        Bind().GetSourceProperty((uint)voice, GetSourceInteger.SourceState, out int state);
        Check();
        return state == (int)SourceState.Playing;
    }

    public void Configure(int voice, float gain, float pitch, Vector3? position)
    {
        if (sources.Contains((uint)voice))
        {
            Apply((uint)voice, gain, pitch, position);
            Check();
        }
    }

    public void Stop(int voice)
    {
        if (sources.Remove((uint)voice))
        {
            var api = Bind();
            api.SourceStop((uint)voice);
            api.DeleteSource((uint)voice);
            Check();
        }
    }

    public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
    {
        var api = Bind();
        api.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);
        float* orientation = stackalloc float[] { forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z };
        api.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
        Check();
    }

    public void Dispose()
    {
        if (context != null && alc != null)
        {
            alc.MakeContextCurrent(context);
            if (al != null)
            {
                foreach (uint source in sources)
                {
                    al.SourceStop(source);
                    al.DeleteSource(source);
                }
                foreach (uint buffer in buffers.Values)
                {
                    al.DeleteBuffer(buffer);
                }
            }
            sources.Clear();
            buffers.Clear();
            alc.MakeContextCurrent((Context*)null);
            alc.DestroyContext(context);
            context = null;
        }
        if (device != null && alc != null)
        {
            alc.CloseDevice(device);
            device = null;
        }
        al?.Dispose();
        al = null;
        alc?.Dispose();
        alc = null;
    }
}
#endif
