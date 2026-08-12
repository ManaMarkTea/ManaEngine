namespace ManaUI
{
    /// <summary>
    /// Content that rebuilds itself when the thing it is showing changes -- the detail half of a
    /// master/detail pair, such as an inspector following a scene selection.
    /// <para>
    /// A retained tree has no way to notice that some unrelated object now holds a different value, and the
    /// engine state these panels show announces nothing. So the source is polled, exactly as
    /// <see cref="TreeView.StructureSignature"/> is, and the subtree is rebuilt only when the answer
    /// actually changes -- not every frame, which would throw away focus, scroll position and every binding
    /// underneath it sixty times a second.
    /// </para>
    /// </summary>
    public class DynamicContent : Decorator
    {
        private Func<object?>? _source;
        private object? _current;
        private object? _polled;
        private int _revision;
        private bool _built;
        private bool _sourceBound;

        /// <summary>
        /// What the content is showing. Polled once a frame; a change rebuilds via <see cref="Template"/>.
        /// </summary>
        public Func<object?>? Source
        {
            get => _source;
            set
            {
                _source = value;

                // Bound on first assignment, like TreeView's signature: a source that changes silently
                // never dirties layout, so there is nothing to hang a check inside MeasureOverride on.
                //
                // A revision rather than the source object itself, because polled bindings skip a write
                // when the new value Equals the old -- and a model type that overrides Equals would then
                // never deliver the switch between two equal-but-distinct instances. Comparing by
                // reference here and reporting a plain counter keeps identity intact through a comparison
                // that has no idea what these objects are.
                if (value != null && !_sourceBound)
                {
                    _sourceBound = true;
                    this.BindPolled(content => content.SourceRevision, PollSource);
                }

                Rebuild(_source?.Invoke());
            }
        }

        /// <summary>
        /// Builds the content for a source value. Returning null leaves <see cref="Placeholder"/> showing,
        /// which is how "nothing selected" is expressed.
        /// </summary>
        public Func<object?, UiElement?>? Template { get; set; }

        /// <summary>Shown whenever the template produces nothing -- an empty state, not an error state.</summary>
        public UiElement? Placeholder { get; set; }

        /// <summary>
        /// The value the current content was built for. The polled binding writes here; assigning it
        /// directly is how a host forces a rebuild.
        /// </summary>
        public object? Current
        {
            get => _current;
            set => Rebuild(value);
        }

        /// <summary>
        /// Bumped whenever the polled source becomes a different object. The binding target rather than
        /// <see cref="Current"/> itself; assigning it rebuilds against whatever the last poll saw.
        /// </summary>
        public int SourceRevision
        {
            get => _revision;
            set
            {
                _revision = value;
                Rebuild(_polled);
            }
        }

        /// <summary>
        /// Reads the source and reports a counter that only moves when the object does. Comparing here,
        /// where the reference is still in hand, is what keeps identity from being flattened by the
        /// binding's value comparison.
        /// </summary>
        private int PollSource()
        {
            var source = _source?.Invoke();
            if (!ReferenceEquals(source, _polled))
            {
                _polled = source;
                _revision++;
            }

            return _revision;
        }

        /// <summary>Rebuilds against the current source, for a change the source value itself cannot show.</summary>
        public void Refresh()
        {
            _built = false;
            Rebuild(_source?.Invoke());
        }

        private void Rebuild(object? source)
        {
            // Reference equality, not Equals: two model objects that compare equal are still different
            // things to inspect, and rebuilding on value equality would miss the switch between them.
            if (_built && ReferenceEquals(_current, source))
            {
                return;
            }

            _current = source;
            _polled = source;
            _built = true;

            var content = Template?.Invoke(source);
            Child = content ?? Placeholder;
        }
    }
}
