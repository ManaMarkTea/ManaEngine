#if MANA_BROWSER
using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ManaCore.Audio;

namespace ManaAudio.Browser;

[SupportedOSPlatform("browser")]
public sealed partial class ManaBrowserAudioBackend : IManaAudioBackend
{
    private const string Module = "mana_audio";
    private readonly Dictionary<ManaAudioClip, int> buffers = new();
    private int device;
    private static Task? initialization;
    public static Task InitializeAsync() => initialization ??= JSHost.ImportAsync(Module, "../mana_audio.js");
    public ManaBrowserAudioBackend() => device = CreateDevice();
    public string OutputDeviceName => "Browser default output";
    public bool IsAvailable => device != 0;
    public bool IsUnlocked => device != 0 && Unlocked(device);

    public int Play(ManaAudioClip clip, ManaAudioPlayOptions options)
    {
        // Drop effects triggered before the user's first gesture, without even uploading their buffers.
        if (!options.Loop && !IsUnlocked)
        {
            return 0;
        }
        if (!buffers.TryGetValue(clip, out int buffer))
        {
            var pcm = new byte[clip.Samples.Length * 2];
            for (int i = 0; i < clip.Samples.Length; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), clip.Samples.Span[i]);
            }
            buffer = Upload(device, pcm, clip.Channels, clip.SampleRate);
            buffers.Add(clip, buffer);
        }
        var p = options.Position ?? Vector3.Zero;
        return Start(device, buffer, options.Loop, options.Position.HasValue, options.Volume, options.Pitch,
            p.X, p.Y, p.Z, options.ReferenceDistance, options.MaxDistance, options.Rolloff);
    }
    public bool IsPlaying(int voice) => device != 0 && Playing(device, voice);
    public void Configure(int voice, float gain, float pitch, Vector3? position)
    {
        var p = position ?? Vector3.Zero;
        Change(device, voice, gain, pitch, p.X, p.Y, p.Z);
    }
    public void Stop(int voice) => StopVoice(device, voice);
    public void SetListener(Vector3 position, Vector3 forward, Vector3 up) =>
        Listener(device, position.X, position.Y, position.Z, forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z);
    public void Dispose()
    {
        if (device != 0)
        {
            Destroy(device);
            device = 0;
            buffers.Clear();
        }
    }

    [JSImport("createDevice", Module)] private static partial int CreateDevice();
    [JSImport("unlocked", Module)] private static partial bool Unlocked(int device);
    [JSImport("upload", Module)] private static partial int Upload(int device, byte[] samples, int channels, int rate);
    [JSImport("start", Module)] private static partial int Start(int device, int buffer, bool loop, bool spatial,
        float gain, float pitch, float x, float y, float z, float referenceDistance, float maxDistance, float rolloff);
    [JSImport("playing", Module)] private static partial bool Playing(int device, int voice);
    [JSImport("change", Module)] private static partial void Change(int device, int voice, float gain, float pitch, float x, float y, float z);
    [JSImport("stop", Module)] private static partial void StopVoice(int device, int voice);
    [JSImport("listener", Module)] private static partial void Listener(int device, float x, float y, float z,
        float fx, float fy, float fz, float ux, float uy, float uz);
    [JSImport("destroy", Module)] private static partial void Destroy(int device);
}
#endif
