using ManaCore.Audio;

namespace Mana.Code.Audio
{
    /// <summary>One piece of music: its clip, how loud, and how fast (a pitch above 1 plays it quicker).</summary>
    public readonly record struct ManaMusic(string Path, float Volume = 1.0f, float Pitch = 1.0f);

    /// <summary>
    /// Looping music on the music bus that crossfades: asking for another tune fades the current one out as the new one
    /// fades in; asking for the same tune again only eases its volume. Clips come from a <see cref="ManaSoundBank"/>;
    /// call <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class ManaMusicPlayer(IManaAudio audio, ManaSoundBank clips, float fadeSeconds = 1.6f)
    {
        // The tune playing now and any still fading out behind it.
        private readonly List<Track> _tracks = new();
        private bool _stopped;

        private sealed class Track
        {
            public required IManaAudioVoice Voice { get; init; }
            public required ManaMusic Music { get; set; }
            public float Level;
            public bool Leaving;
        }

        /// <summary>Plays <paramref name="music"/>, faded in (or at once), and fades out whatever was playing.</summary>
        public void Play(ManaMusic music, bool fadeIn = true)
        {
            if (_stopped)
            {
                return;
            }
            var playing = _tracks.LastOrDefault(track => !track.Leaving);
            if (playing != null && SameTune(playing.Music, music))
            {
                playing.Music = music;
                return;
            }
            foreach (var track in _tracks)
            {
                track.Leaving = true;
            }
            if (clips.Clip(music.Path) is not { } clip)
            {
                return;
            }
            var level = 1.0f;
            if (fadeIn)
            {
                level = 0.0f;
            }
            var voice = audio.Play(clip, new ManaAudioPlayOptions
            {
                Loop = true, Bus = ManaAudioBus.Music, Volume = music.Volume * level, Pitch = music.Pitch
            });
            _tracks.Add(new Track { Voice = voice, Music = music, Level = level });
        }

        public void Tick(float deltaSeconds)
        {
            var step = deltaSeconds / fadeSeconds;
            for (var i = _tracks.Count - 1; i >= 0; i--)
            {
                var track = _tracks[i];
                var direction = step;
                if (track.Leaving)
                {
                    direction = -step;
                }
                track.Level = Math.Clamp(track.Level + direction, 0.0f, 1.0f);
                if (track.Leaving && track.Level <= 0.0f)
                {
                    track.Voice.Stop();
                    _tracks.RemoveAt(i);
                    continue;
                }
                // Eased, so a fade reads as even to the ear rather than rushing at one end.
                track.Voice.Volume = track.Music.Volume * track.Level * track.Level;
            }
        }

        /// <summary>Stops everything at once; the player plays nothing more.</summary>
        public void Stop()
        {
            _stopped = true;
            foreach (var track in _tracks)
            {
                track.Voice.Stop();
            }
            _tracks.Clear();
        }

        private static bool SameTune(ManaMusic a, ManaMusic b) =>
            string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) && MathF.Abs(a.Pitch - b.Pitch) < 0.01f;
    }
}
