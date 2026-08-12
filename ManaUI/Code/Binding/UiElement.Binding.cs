namespace ManaUI
{
    public abstract partial class UiElement
    {
        private List<IUiBinding>? _bindings;
        private List<IUiBinding>? _polledBindings;

        /// <summary>True when this element has bindings that must be re-read every frame.</summary>
        internal bool HasPolledBindings => _polledBindings is { Count: > 0 };

        /// <summary>
        /// Takes ownership of a binding. Bindings are released when the element leaves the tree, which is
        /// what stops a long-lived view model's PropertyChanged list from pinning discarded UI forever.
        /// </summary>
        internal void AddBinding(IUiBinding binding)
        {
            (_bindings ??= new List<IUiBinding>()).Add(binding);
        }

        internal void AddPolledBinding(IUiBinding binding)
        {
            (_polledBindings ??= new List<IUiBinding>()).Add(binding);
            AddBinding(binding);
        }

        internal void RefreshPolledBindings()
        {
            if (_polledBindings == null)
            {
                return;
            }

            foreach (var binding in _polledBindings)
            {
                binding.Refresh();
            }
        }

        /// <summary>Re-reads every binding on this element, push and polled alike.</summary>
        public void RefreshBindings()
        {
            if (_bindings == null)
            {
                return;
            }

            foreach (var binding in _bindings)
            {
                binding.Refresh();
            }
        }

        /// <summary>Detaches every binding on this element and its subtree.</summary>
        public void ClearBindings()
        {
            if (_bindings != null)
            {
                foreach (var binding in _bindings)
                {
                    binding.Dispose();
                }

                _bindings.Clear();
                _polledBindings?.Clear();
            }

            var count = VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                GetVisualChild(i).ClearBindings();
            }
        }
    }
}
