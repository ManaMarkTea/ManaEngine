using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Builds the inspector UI for one kind of thing -- a component, a material, a shader's channels.
    /// <para>
    /// Acceptance is per instance rather than per type, because what a thing needs shown often depends on
    /// its state: a material's PBR channels are worth offering only when its shader actually reads them.
    /// </para>
    /// </summary>
    public interface IInspectorWidget
    {
        /// <summary>True when this widget has something to say about the subject.</summary>
        bool CanInspect(object subject);

        /// <summary>
        /// Builds the section. Only called when <see cref="CanInspect"/> returned true, so an implementation
        /// may assume the subject is the type and in the state it asked for.
        /// </summary>
        UiElement Build(object subject, InspectorContext context);
    }

    /// <summary>
    /// The usual case: a widget for one subject type. Override <see cref="Accepts"/> to narrow it further.
    /// </summary>
    public abstract class InspectorWidget<T> : IInspectorWidget
        where T : class
    {
        public bool CanInspect(object subject)
        {
            return subject is T typed && Accepts(typed);
        }

        public UiElement Build(object subject, InspectorContext context)
        {
            if (subject is not T typed)
            {
                throw new ArgumentException($"{GetType().Name} cannot inspect {subject?.GetType().Name ?? "null"}.");
            }

            return Build(typed, context);
        }

        /// <summary>An extra condition beyond the type -- the state that makes this widget relevant.</summary>
        protected virtual bool Accepts(T subject)
        {
            return true;
        }

        protected abstract UiElement Build(T subject, InspectorContext context);
    }
}
