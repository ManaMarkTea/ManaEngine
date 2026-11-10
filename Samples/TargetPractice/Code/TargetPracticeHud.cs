using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TargetPractice
{
    /// <summary>
    /// What the HUD shows, for <c>Content/UI/Target Practice HUD.manaui</c> to bind to: the layout, its colours and its
    /// words are data, edited in the UI Designer; this is only the numbers. A UiCanvas script in the level names the mode
    /// (its <c>bindingSource</c>), and the mode hands this over as its <see cref="TargetPracticeMode.HudViewModel"/>.
    /// </summary>
    public sealed class TargetPracticeHud : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _down;
        private int _total;
        private float _time;
        private int _shots;
        private bool _won;

        /// <summary>Targets knocked down so far.</summary>
        public int Down { get => _down; private set => Set(ref _down, value, [nameof(Count), nameof(Progress), nameof(Accuracy)]); }

        /// <summary>Targets in the level.</summary>
        public int Total { get => _total; private set => Set(ref _total, value, [nameof(Count), nameof(Progress), nameof(NoTargets)]); }

        /// <summary>Seconds since the first shot.</summary>
        public float Time { get => _time; private set => Set(ref _time, value, [nameof(WinMessage)]); }

        public int Shots { get => _shots; private set => Set(ref _shots, value, [nameof(WinMessage), nameof(Accuracy)]); }

        public bool Won { get => _won; private set => Set(ref _won, value, [nameof(WinMessage)]); }

        /// <summary>"3 / 10".</summary>
        public string Count => $"{Down} / {Total}";

        /// <summary>The share knocked down, 0 to 1, for a bar.</summary>
        public float Progress
        {
            get
            {
                if (Total == 0)
                {
                    return 0.0f;
                }
                return Down / (float)Total;
            }
        }

        /// <summary>Hits per shot, 0 to 1.</summary>
        public float Accuracy
        {
            get
            {
                if (Shots == 0)
                {
                    return 0.0f;
                }
                return Math.Min(Down / (float)Shots, 1.0f);
            }
        }

        public string WinMessage => $"All down in {Time:F1} s with {Shots} shots -- R to go again";

        /// <summary>A level with nothing to shoot: says how to make targets.</summary>
        public bool NoTargets => Total == 0;

        /// <summary>Copies the round's state in; only what changed is announced.</summary>
        public void Refresh(int down, int total, float time, int shots, bool won)
        {
            Down = down;
            Total = total;
            // A tenth of a second is all the HUD shows: no need to rebind sixty times a second.
            Time = MathF.Floor(time * 10.0f) / 10.0f;
            Shots = shots;
            Won = won;
        }

        // Announces the property and the ones worked out from it.
        private void Set<T>(ref T field, T value, string[] alsoChanged, [CallerMemberName] string property = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }
            field = value;
            Raise(property);
            foreach (var name in alsoChanged)
            {
                Raise(name);
            }
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
