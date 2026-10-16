namespace ManaUI
{
    internal sealed class UiElementBindings
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

        private int _bindingApplyDepth;

        /// <summary>
        /// True while a binding is pushing a value into this element, as opposed to a user editing it.
        /// <para>
        /// An editor bound to something that changes on its own -- a node being animated, a gizmo drag --
        /// gets its value set every frame, and setting it raises the same change event a keystroke does.
        /// A handler that writes back on that event forms a loop: the model's value is round-tripped
        /// through the control's display precision on every frame, and anything the write triggers happens
        /// every frame too. Handlers that write to a model should do nothing while this is true.
        /// </para>
        /// </summary>
        public bool IsApplyingBinding => _bindingApplyDepth > 0;

        internal void BeginBindingApply()
        {
            _bindingApplyDepth++;
        }

        internal void EndBindingApply()
        {
            _bindingApplyDepth = Math.Max(0, _bindingApplyDepth - 1);
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
        /// Activates or suspends every owned binding, following attachment to the tree.
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
        }

        /// <summary>Disposes every owned binding. Permanent, unlike suspension.</summary>
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
        }
    }
}
