using System.Text.Json;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaAudio;
using ManaCore.Assets;
using ManaCore.Audio;
using ManaUI;

namespace ManaTests;

public static class AudioChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Reject(string name, Action action)
        {
            try { action(); check(name, false, "Expected an exception"); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException or ObjectDisposedException)
            { check(name, true, ""); }
        }

        var wav = Wav(16, 1, [0, 128, 0, 0, 255, 127]);
        var clip = ManaAudioClip.FromWav(wav);
        check("PCM16 preserves signed extrema", clip.Samples.Span.SequenceEqual(new short[] { -32768, 0, 32767 }), "");
        check("duration counts frames", Math.Abs(clip.Duration - 3.0 / 48000) < 1e-9, "");
        check("PCM8 converts unsigned samples", ManaAudioClip.FromWav(Wav(8, 1, [0, 128, 255])).Samples.Span
            .SequenceEqual(new short[] { -32768, 0, 32512 }), "");
        check("PCM24 preserves signed extrema", ManaAudioClip.FromWav(Wav(24, 1, [0, 0, 128, 0, 0, 0, 255, 255, 127])).Samples.Span
            .SequenceEqual(new short[] { -32768, 0, 32767 }), "");
        check("PCM32 preserves signed extrema", ManaAudioClip.FromWav(Wav(32, 1, [0, 0, 0, 128, 0, 0, 0, 0, 255, 255, 255, 127])).Samples.Span
            .SequenceEqual(new short[] { -32768, 0, 32767 }), "");
        var floats = new[] { -2f, 0f, 2f }.SelectMany(BitConverter.GetBytes).ToArray();
        check("float WAV clips to PCM16", ManaAudioClip.FromWav(Wav(32, 3, floats)).Samples.Span
            .SequenceEqual(new short[] { -32768, 0, 32767 }), "");
        check("unknown odd-sized chunks are skipped with padding", ManaAudioClip.FromWav(Wav(16, 1, [0, 0], junk: true)).Samples.Length == 1, "");
        Reject("reject truncated container", () => ManaAudioClip.FromWav(wav[..^1]));
        Reject("reject invalid header", () => ManaAudioClip.FromWav(new byte[44]));
        Reject("reject partial PCM frame", () => ManaAudioClip.FromWav(Wav(16, 1, [0])));
        Reject("reject compressed WAV", () => ManaAudioClip.FromWav(Wav(16, 17, [0, 0])));
        Reject("reject NaN samples", () => ManaAudioClip.FromWav(Wav(32, 3, BitConverter.GetBytes(float.NaN))));
        var corrupt = wav.ToArray();
        Array.Fill(corrupt, (byte)255, 40, 4);
        Reject("reject overflowing chunk size", () => ManaAudioClip.FromWav(corrupt));

        var backend = new RecordingBackend();
        using var audio = new ManaAudioSystem(backend);
        var first = audio.Play(clip, new() { Volume = 0.5f, Loop = true });
        var second = audio.Play(clip, new() { Bus = ManaAudioBus.Music });
        check("same clip supports overlapping voices", backend.Active.Count == 2, "");
        audio.MasterVolume = 0.5f;
        audio.SetBusVolume(ManaAudioBus.Sfx, 0.4f);
        check("volume combines voice, bus and master", Math.Abs(backend.Gains[1] - 0.1f) < 1e-6f && backend.Gains[2] == 0.5f, "");
        Reject("reject nonfinite volume", () => first.Volume = float.NaN);
        Reject("reject zero pitch", () => first.Pitch = 0);
        Reject("reject spatial stereo", () => audio.Play(new ManaAudioClip(new short[2], 2, 48000), new() { Position = Vector3.Zero }));
        Reject("reject invalid bus", () => audio.SetBusVolume((ManaAudioBus)10, 1));
        Reject("reject degenerate listener", () => audio.SetListener(Vector3.Zero, Vector3.UnitY, Vector3.UnitY));
        first.Stop(); first.Stop();
        check("stop is idempotent and leaves other voices playing", backend.StopCount == 1 && second.IsPlaying, "");
        backend.Active.Remove(2); // Simulate natural completion in the mixer.
        audio.Update();
        check("completed voices release resources", backend.StopCount == 2 && !second.IsPlaying, "");
        for (int i = 0; i < ManaAudioSystem.MaxVoices; i++) { audio.Play(clip, new() { Loop = true }); }
        var overflow = audio.Play(clip);
        check("voice limit drops excess requests", backend.Active.Count == 64 && !overflow.IsPlaying, "");
        audio.StopAll();
        check("StopAll releases every voice", backend.Active.Count == 0, "");
        var survivor = audio.Play(clip);
        audio.Dispose(); audio.Dispose(); survivor.Stop(); survivor.Volume = 0;
        check("dispose invalidates retained handles safely", !survivor.IsPlaying && backend.DisposeCount == 1, "");
        Reject("disposed system rejects new playback", () => audio.Play(clip));
        using var silent = ManaAudioFactory.CreateSilent();
        check("silent backend needs no device", !silent.IsAvailable && !silent.Play(clip).IsPlaying, "");

        var emitterBackend = new RecordingBackend();
        using var emitterAudio = new ManaAudioSystem(emitterBackend);
        var context = new ManaContext(null!) { Audio = emitterAudio };
        var node = new ManaSceneNode(context, Matrix4x4.CreateTranslation(10, 0, 0));
        using var emitter = new ManaAudioSourceComponent(node) { Clip = clip, Loop = true, Position = Vector3.UnitX };
        emitter.BeginPlay();
        check("emitter starts with transformed local offset", emitterBackend.Positions[1] == new Vector3(11, 0, 0), "");
        node.WorldTransform = Matrix4x4.CreateTranslation(20, 0, 0);
        emitter.UpdateComponent();
        check("emitter follows world transform", emitterBackend.Positions[1] == new Vector3(21, 0, 0), "");
        emitter.EndPlay();
        check("emitter releases its loop on EndPlay", emitterBackend.Active.Count == 0 && emitter.Voice == null, "");

        var directory = Directory.CreateTempSubdirectory("mana-audio-tests-");
        var originalCatalog = ManaAssetCatalog.Current;
        try
        {
            string path = Path.Combine(directory.FullName, "sample.wav");
            File.WriteAllBytes(path, wav);
            var loaded = silent.LoadClipAsync(path).GetAwaiter().GetResult();
            check("clip cache shares decoded data", ReferenceEquals(loaded, silent.LoadClipAsync(path).GetAwaiter().GetResult()), "");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { silent.LoadClipAsync(path, cancellation.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            check("cancelled request leaves shared clip usable", cancelled && ReferenceEquals(loaded, silent.LoadClipAsync(path).GetAwaiter().GetResult()), "");
            string retry = Path.Combine(directory.FullName, "retry.wav");
            try { silent.LoadClipAsync(retry).GetAwaiter().GetResult(); }
            catch (FileNotFoundException) { }
            File.WriteAllBytes(retry, wav);
            check("failed loads can be retried", silent.LoadClipAsync(retry).GetAwaiter().GetResult().Samples.Length == 3, "");

            string catalogJson = JsonSerializer.Serialize(new
            {
                version = 1, models = Array.Empty<object>(),
                files = new[] { new { path = "Assets/Audio/test.wav", url = "AssetLibrary/test.wav", sha256 = "test",
                    size = wav.Length, dependencies = Array.Empty<string>() } }
            });
            ManaAssetCatalog.Current = new ManaAssetCatalog(catalogJson, directory.FullName, (_, _, _) => Task.FromResult(wav));
            var downloaded = silent.LoadClipAsync("Assets/Audio/test.wav").GetAwaiter().GetResult();
            check("audio loads from the catalog's own root", downloaded.Samples.Span.SequenceEqual(clip.Samples.Span), "");
        }
        finally { ManaAssetCatalog.Current = originalCatalog; directory.Delete(true); }
    }

    private static byte[] Wav(ushort bits, ushort format, byte[] data, bool junk = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVE"u8);
        if (junk) { writer.Write("JUNK"u8); writer.Write(1); writer.Write((byte)42); writer.Write((byte)0); }
        writer.Write("fmt "u8); writer.Write(16); writer.Write(format); writer.Write((ushort)1);
        writer.Write(48000); writer.Write(48000 * bits / 8); writer.Write((ushort)(bits / 8)); writer.Write(bits);
        writer.Write("data"u8); writer.Write(data.Length); writer.Write(data);
        if (data.Length % 2 != 0) { writer.Write((byte)0); }
        stream.Position = 4; writer.Write((int)stream.Length - 8);
        return stream.ToArray();
    }

    private sealed class RecordingBackend : IManaAudioBackend
    {
        public readonly HashSet<int> Active = new();
        public readonly Dictionary<int, float> Gains = new();
        public readonly Dictionary<int, Vector3?> Positions = new();
        public int StopCount, DisposeCount;
        private int next;
        public bool IsAvailable => true;
        public bool IsUnlocked => true;
        public int Play(ManaAudioClip clip, ManaAudioPlayOptions options)
        {
            int id = ++next; Active.Add(id); Gains[id] = options.Volume; Positions[id] = options.Position; return id;
        }
        public bool IsPlaying(int voice) => Active.Contains(voice);
        public void Configure(int voice, float gain, float pitch, Vector3? position)
        {
            Gains[voice] = gain;
            Positions[voice] = position;
        }
        public void Stop(int voice) { Active.Remove(voice); StopCount++; }
        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }
        public void Dispose() { DisposeCount++; }
    }
}
