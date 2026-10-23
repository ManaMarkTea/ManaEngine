using ManaCore.Audio;
using ManaCore.Diagnostics;

namespace Mana.Code.Audio
{
    /// <summary>
    /// A game's sound effects, loaded up front and played by path. A sound still loading is skipped rather than played
    /// late; a sound asked for again within <see cref="MinimumGap"/> seconds is skipped too (dragging out a row of
    /// buildings clicks once a few frames, not every frame).
    /// </summary>
    public sealed class ManaSoundBank
    {
        private readonly IManaAudio _audio;
        private readonly Dictionary<string, Task<ManaAudioClip>> _clips = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="owner">Names the game in the log when a sound will not load.</param>
        public ManaSoundBank(IManaAudio audio, IEnumerable<string> paths, string owner)
        {
            _audio = audio;
            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var load = audio.LoadClipAsync(path);
                _ = load.ContinueWith(task => ManaLog.WriteDiagnostic($"{owner}: sound '{path}' did not load: {task.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
                _clips[path] = load;
            }
            // Finished, whether every clip loaded or not.
            Ready = Task.WhenAll(_clips.Values).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        /// <summary>Done when every sound has loaded or failed to.</summary>
        public Task Ready { get; }

        /// <summary>Seconds a sound must wait before it plays again; 0 lets it overlap itself.</summary>
        public double MinimumGap { get; init; }

        /// <summary>The clip, once it has loaded; null while it is loading or when it failed.</summary>
        public ManaAudioClip? Clip(string path)
        {
            if (_clips.TryGetValue(path, out var load) && load.IsCompletedSuccessfully)
            {
                return load.Result;
            }
            return null;
        }

        /// <summary>Plays a sound if it has loaded (and not just played); returns whether it did.</summary>
        public bool Play(string path, float volume = 1.0f, float pitch = 1.0f)
        {
            if (!_audio.IsAvailable || Clip(path) is not { } clip)
            {
                return false;
            }
            if (MinimumGap > 0)
            {
                var now = Environment.TickCount64 / 1000.0;
                if (now - _lastPlayed.GetValueOrDefault(path, double.MinValue) < MinimumGap)
                {
                    return false;
                }
                _lastPlayed[path] = now;
            }
            _audio.Play(clip, new ManaAudioPlayOptions { Volume = volume, Pitch = pitch });
            return true;
        }
    }
}
