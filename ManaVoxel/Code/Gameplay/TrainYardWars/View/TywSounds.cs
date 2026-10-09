using ManaCore.Audio;
using ManaCore.Diagnostics;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>The game's sound effects: the engine's starter clips, loaded up front and played by name.</summary>
    internal sealed class TywSounds
    {
        public const string Track = "Assets/Audio/SampleCourse/Platforms/stop_clunk.wav";
        public const string Build = "Assets/Audio/Starter/Gameplay/impact_soft.wav";
        public const string Train = "Assets/Audio/Starter/Gameplay/whoosh.wav";
        public const string Bank = "Assets/Audio/Starter/Gameplay/pickup.wav";
        public const string Points = "Assets/Audio/SampleCourse/Feedback/all_coins.wav";
        public const string Alarm = "Assets/Audio/SampleCourse/Enemies/alert.wav";
        public const string Hit = "Assets/Audio/Starter/Gameplay/impact_hard.wav";
        public const string Confirm = "Assets/Audio/Starter/UI/confirm.wav";
        public const string Award = "Assets/Audio/Starter/Gameplay/checkpoint.wav";
        public const string Rival = "Assets/Audio/Starter/UI/notification.wav";
        public const string Win = "Assets/Audio/Starter/Gameplay/goal.wav";
        public const string Lose = "Assets/Audio/SampleCourse/Enemies/defeat.wav";
        public const string Error = "Assets/Audio/Starter/UI/error.wav";
        public const string Toggle = "Assets/Audio/Starter/UI/toggle_on.wav";

        private static readonly string[] All = { Track, Build, Train, Bank, Points, Alarm, Hit, Confirm, Award, Rival, Win, Lose, Error, Toggle };

        private readonly IManaAudio _audio;
        private readonly Dictionary<string, Task<ManaAudioClip>> _clips = new();

        public TywSounds(IManaAudio audio)
        {
            _audio = audio;
            foreach (var path in All)
            {
                var load = audio.LoadClipAsync(path);
                _ = load.ContinueWith(task => ManaLog.WriteDiagnostic($"Train Yard Wars: sound '{path}' did not load: {task.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
                _clips[path] = load;
            }
        }

        /// <summary>Plays a clip if it has finished loading; a sound still on its way is skipped rather than late.</summary>
        public void Play(string path, float volume = 1.0f, float pitch = 1.0f)
        {
            if (!_audio.IsAvailable || !_clips.TryGetValue(path, out var load) || !load.IsCompletedSuccessfully)
            {
                return;
            }

            _audio.Play(load.Result, new ManaAudioPlayOptions { Volume = volume, Pitch = pitch });
        }
    }
}
