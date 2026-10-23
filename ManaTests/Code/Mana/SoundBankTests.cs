using System.Numerics;
using Mana.Code.Audio;
using ManaCore.Audio;
using ManaUI;

namespace ManaTests;

/// <summary>
/// ManaSoundBank and ManaMusicPlayer, against a recording stand-in for the audio system: sounds play once loaded and
/// not while loading, a minimum gap thins repeats, and music crossfades -- the old tune out, the new one in, a repeat of
/// the same tune left playing.
/// </summary>
public static class SoundBankChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var audio = new RecordingAudio();
        var bank = new ManaSoundBank(audio, ["a.wav", "b.wav", "slow.wav", "a.wav"], "Test") { MinimumGap = 10 };
        check("each sound is loaded once", audio.Loaded.Count == 3, string.Join(", ", audio.Loaded));
        check("a loaded sound plays", bank.Play("a.wav", 0.5f) && audio.Played.Count == 1 && audio.Played[0].Options.Volume == 0.5f, "");
        check("asked again within the gap, it does not", !bank.Play("a.wav") && audio.Played.Count == 1, "");
        check("a sound still loading is skipped, not played late", !bank.Play("slow.wav") && audio.Played.Count == 1, "");
        check("an unknown sound plays nothing", !bank.Play("missing.wav"), "");
        check("the bank is not ready while a sound is still loading", !bank.Ready.IsCompleted, "");
        audio.Finish("slow.wav");
        bank.Ready.Wait(1000);
        check("and is once every sound has loaded", bank.Ready.IsCompleted, "");

        var music = new ManaMusicPlayer(audio, bank, fadeSeconds: 1.0f);
        music.Play(new ManaMusic("a.wav", 0.8f), fadeIn: false);
        var first = audio.Voices[^1];
        check("music loops on the music bus at its volume", audio.Played[^1].Options is { Loop: true, Bus: ManaAudioBus.Music } && MathF.Abs(first.Volume - 0.8f) < 1e-3f, "");
        var before = audio.Voices.Count;
        music.Play(new ManaMusic("a.wav", 0.4f));
        check("the same tune again starts no new voice", audio.Voices.Count == before, $"{audio.Voices.Count} voices");
        music.Tick(0.1f);
        check("and keeps playing", !first.Stopped && MathF.Abs(first.Volume - 0.4f) < 1e-3f, $"{first.Volume}");
        var voices = audio.Voices.Count;
        music.Play(new ManaMusic("b.wav", 1.0f));
        var second = audio.Voices[^1];
        check("another tune starts silent", audio.Voices.Count == voices + 1 && second.Volume == 0, $"{second.Volume}");
        music.Tick(0.5f);
        check("halfway, one fades out as the other fades in", first.Volume < 0.4f && second.Volume > 0 && !first.Stopped, $"{first.Volume} / {second.Volume}");
        music.Tick(0.6f);
        check("the old tune stops once faded out", first.Stopped && MathF.Abs(second.Volume - 1.0f) < 1e-3f, $"{second.Volume}");
        music.Stop();
        check("stopping stops the music", second.Stopped, "");
    }

    private sealed class RecordingAudio : IManaAudio
    {
        private readonly Dictionary<string, TaskCompletionSource<ManaAudioClip>> _pending = new();

        public List<string> Loaded { get; } = new();
        public List<(ManaAudioClip Clip, ManaAudioPlayOptions Options)> Played { get; } = new();
        public List<Voice> Voices { get; } = new();

        public string OutputDeviceName => "test";
        public bool IsAvailable => true;
        public bool IsUnlocked => true;
        public float MasterVolume { get; set; } = 1;

        public Task<ManaAudioClip> LoadClipAsync(string path, CancellationToken cancellationToken = default)
        {
            Loaded.Add(path);
            if (path.StartsWith("slow", StringComparison.Ordinal))
            {
                var pending = new TaskCompletionSource<ManaAudioClip>();
                _pending[path] = pending;
                return pending.Task;
            }
            return Task.FromResult(new ManaAudioClip(new short[] { 0, 0 }, 1, 8000));
        }

        public void Finish(string path) => _pending[path].SetResult(new ManaAudioClip(new short[] { 0, 0 }, 1, 8000));

        public IManaAudioVoice Play(ManaAudioClip clip, ManaAudioPlayOptions? options = null)
        {
            options ??= new ManaAudioPlayOptions();
            Played.Add((clip, options));
            var voice = new Voice { Volume = options.Volume, Pitch = options.Pitch };
            Voices.Add(voice);
            return voice;
        }

        public void SetBusVolume(ManaAudioBus bus, float volume) { }
        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }
        public void Update() { }
        public void StopAll() { }
        public void Dispose() { }
    }

    private sealed class Voice : IManaAudioVoice
    {
        public bool Stopped { get; private set; }
        public bool IsPlaying => !Stopped;
        public float Volume { get; set; }
        public float Pitch { get; set; }
        public Vector3? Position { get; set; }
        public void Stop() => Stopped = true;
        public void Dispose() { }
    }
}
