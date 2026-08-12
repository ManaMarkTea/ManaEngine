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

            // The root keeps a flat list of elements carrying polled bindings and only rebuilds it when
            // the tree changed. A binding added to an element that is already attached and laid out would
            // otherwise never join that list, and so would never be evaluated.
            Root?.NotifyPolledBindingsChanged();
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

        /// <summary>
        /// Activates or suspends every binding in this subtree, following attachment to the tree.
        /// <para>
        /// Suspending rather than disposing is what lets a subtree be removed and put back -- a dock tab
        /// switch, a collapsed panel -- without losing its bindings. It also stops a detached subtree from
        /// being kept alive by a long-lived view model's handler list.
        /// </para>
        /// </summary>
        internal void SetBindingsActive(bool active)
        {
            if (_bindings != null)
            {
                foreach (var binding in _bindings)
                {
                    binding.SetActive(active);
                }
            }

            var count = VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                GetVisualChild(i).SetBindingsActive(active);
            }
        }

        /// <summary>Disposes every binding on this element and its subtree. Permanent, unlike suspension.</summary>
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
