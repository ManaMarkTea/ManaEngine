using System.ComponentModel;

namespace ManaUI
{
    public enum BindingMode
    {
        /// <summary>Source drives target. The default, and what most of a UI wants.</summary>
        OneWay,

        /// <summary>Source drives target, and edits in the target write back.</summary>
        TwoWay,

        /// <summary>Applied once at bind time, then never again.</summary>
        OneTime,

        /// <summary>Target drives source only.</summary>
        OneWayToSource
    }

    /// <summary>A live binding. Disposing detaches it from the source permanently.</summary>
    public interface IUiBinding : IDisposable
    {
        /// <summary>Pushes the current source value into the target.</summary>
        void Refresh();

        /// <summary>
        /// Subscribes or unsubscribes from the source, following the element's attachment to the tree.
        /// <para>
        /// This is what lets a binding survive being temporarily removed -- switching dock tabs detaches
        /// the hidden panel's content and re-attaches it later, and its bindings must come back to life
        /// rather than having been destroyed. Reactivating also refreshes, so a panel that was hidden
        /// while its source changed shows the current value the moment it returns.
        /// </para>
        /// </summary>
        void SetActive(bool active);
    }

    /// <summary>
    /// Binds a source property to a target property, tracking changes through
    /// <see cref="INotifyPropertyChanged"/>.
    /// <para>
    /// This is the push half of the binding system: the source announces changes and the binding reacts.
    /// See <see cref="UiPolledBinding{TValue}"/> for the pull half, which exists because most engine state
    /// will never implement INotifyPropertyChanged.
    /// </para>
    /// </summary>
    internal sealed class UiBinding<TSource, TValue> : IUiBinding
        where TSource : class
    {
        private readonly TSource _source;
        private readonly Func<TSource, TValue> _getValue;
        private readonly Action<TSource, TValue>? _setValue;
        private readonly Action<TValue> _applyToTarget;
        private readonly Func<TValue>? _readTarget;
        private readonly string _propertyName;
        private readonly BindingMode _mode;

        private INotifyPropertyChanged? _observable;
        private bool _suppress;
        private bool _active;
        private bool _disposed;

        public UiBinding(
            TSource source,
            Func<TSource, TValue> getValue,
            Action<TSource, TValue>? setValue,
            string propertyName,
            Action<TValue> applyToTarget,
            Func<TValue>? readTarget,
            BindingMode mode)
        {
            _source = source;
            _getValue = getValue;
            _setValue = setValue;
            _propertyName = propertyName;
            _applyToTarget = applyToTarget;
            _readTarget = readTarget;
            _mode = mode;

            if (mode != BindingMode.OneWayToSource)
            {
                Refresh();
            }
            else
            {
                PushToSource();
            }

            SetActive(true);
        }

        public void SetActive(bool active)
        {
            if (_disposed || _active == active)
            {
                return;
            }

            _active = active;

            // OneTime deliberately never subscribes, so a static label costs nothing to keep alive.
            if (_mode is not (BindingMode.OneWay or BindingMode.TwoWay) || _source is not INotifyPropertyChanged observable)
            {
                return;
            }

            if (active)
            {
                _observable = observable;
                _observable.PropertyChanged += OnSourcePropertyChanged;

                // The source may have moved on while this was detached.
                Refresh();
            }
            else if (_observable != null)
            {
                _observable.PropertyChanged -= OnSourcePropertyChanged;
                _observable = null;
            }
        }

        public void Refresh()
        {
            if (_disposed || _mode == BindingMode.OneWayToSource)
            {
                return;
            }

            // Guards the TwoWay loop: applying to the target can raise the target's own change event,
            // which would otherwise write straight back to the source and bounce.
            _suppress = true;
            try
            {
                _applyToTarget(_getValue(_source));
            }
            finally
            {
                _suppress = false;
            }
        }

        /// <summary>Called when the target's value changed, for TwoWay and OneWayToSource.</summary>
        public void PushToSource()
        {
            if (_disposed || _suppress || _readTarget == null || _setValue == null)
            {
                return;
            }

            if (_mode is not (BindingMode.TwoWay or BindingMode.OneWayToSource))
            {
                return;
            }

            var value = _readTarget();
            if (!EqualityComparer<TValue>.Default.Equals(_getValue(_source), value))
            {
                _setValue(_source, value);
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // An empty or null name is the framework convention for "everything changed".
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _propertyName)
            {
                Refresh();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            SetActive(false);
            _disposed = true;
        }
    }

    /// <summary>
    /// A binding that re-reads its source every frame instead of waiting to be told.
    /// <para>
    /// The deliberate escape hatch for engine state -- frame times, mesh counts, a hovered voxel position --
    /// none of which implements INotifyPropertyChanged and none of which should have to. Without this the
    /// only options would be wrapping every engine field in a view model or cheating outside the binding
    /// system entirely.
    /// </para>
    /// Only applies to the target when the value actually changed, so a stable readout costs one comparison.
    /// </summary>
    internal sealed class UiPolledBinding<TValue> : IUiBinding
    {
        private readonly Func<TValue> _read;
        private readonly Action<TValue> _applyToTarget;
        private TValue _last;
        private bool _hasValue;
        private bool _active = true;
        private bool _disposed;

        public UiPolledBinding(Func<TValue> read, Action<TValue> applyToTarget)
        {
            _read = read;
            _applyToTarget = applyToTarget;
            _last = default!;
            Refresh();
        }

        /// <summary>A polled binding has nothing to subscribe to, so activation only gates evaluation.</summary>
        public void SetActive(bool active)
        {
            _active = active;
        }

        public void Refresh()
        {
            if (_disposed || !_active)
            {
                return;
            }

            var value = _read();
            if (_hasValue && EqualityComparer<TValue>.Default.Equals(_last, value))
            {
                return;
            }

            _last = value;
            _hasValue = true;
            _applyToTarget(value);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
