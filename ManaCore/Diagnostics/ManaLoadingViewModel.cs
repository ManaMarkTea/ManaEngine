using System.ComponentModel;

namespace ManaCore.Diagnostics
{
    public readonly record struct ManaLoadingProgress(string Status, float Fraction);

    public sealed class ManaLoadingViewModel : INotifyPropertyChanged, IProgress<ManaLoadingProgress>
    {
        private readonly object _sync = new();
        private readonly SynchronizationContext? _notificationContext = SynchronizationContext.Current;
        private bool _isActive;
        private string _status = "Initializing...";
        private float _progress;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsActive
        {
            get
            {
                lock (_sync)
                {
                    return _isActive;
                }
            }
        }

        public string Status
        {
            get
            {
                lock (_sync)
                {
                    return _status;
                }
            }
        }

        public float Progress
        {
            get
            {
                lock (_sync)
                {
                    return _progress;
                }
            }
        }

        public ManaLoadingProgress Snapshot
        {
            get
            {
                lock (_sync)
                {
                    return new ManaLoadingProgress(_status, _progress);
                }
            }
        }

        public void Start(string status, float progress = 0.0f)
        {
            Update(true, status, progress);
        }

        public void Report(ManaLoadingProgress value)
        {
            Update(null, value.Status, value.Fraction);
        }

        public void SetActive(bool isActive)
        {
            Update(isActive, null, null);
        }

        public void SetStatus(string status)
        {
            Update(null, status, null);
        }

        public void SetProgress(float progress)
        {
            Update(null, null, progress);
        }

        public void Complete(string status = "Ready")
        {
            Update(false, status, 1.0f);
        }

        public void Fail(string status)
        {
            Update(false, status, null);
        }

        private void Update(bool? isActive, string? status, float? progress)
        {
            List<string>? changedProperties = null;
            var snapshotChanged = false;
            lock (_sync)
            {
                if (isActive.HasValue && _isActive != isActive.Value)
                {
                    _isActive = isActive.Value;
                    (changedProperties ??= new()).Add(nameof(IsActive));
                }

                if (status != null && !string.Equals(_status, status, StringComparison.Ordinal))
                {
                    _status = status;
                    (changedProperties ??= new()).Add(nameof(Status));
                    snapshotChanged = true;
                }

                if (progress.HasValue)
                {
                    var clampedProgress = Math.Clamp(progress.Value, 0.0f, 1.0f);
                    if (Math.Abs(_progress - clampedProgress) > 0.0001f)
                    {
                        _progress = clampedProgress;
                        (changedProperties ??= new()).Add(nameof(Progress));
                        snapshotChanged = true;
                    }
                }

                if (snapshotChanged)
                {
                    (changedProperties ??= new()).Add(nameof(Snapshot));
                }
            }

            if (changedProperties == null)
            {
                return;
            }

            RaisePropertyChanged(changedProperties);
        }

        private void RaisePropertyChanged(IReadOnlyList<string> propertyNames)
        {
            var handler = PropertyChanged;
            if (handler == null)
            {
                return;
            }

            void Raise()
            {
                foreach (var propertyName in propertyNames)
                {
                    handler(this, new PropertyChangedEventArgs(propertyName));
                }
            }

            if (_notificationContext != null && SynchronizationContext.Current != _notificationContext)
            {
                _notificationContext.Post(_ => Raise(), null);
                return;
            }

            Raise();
        }
    }
}
