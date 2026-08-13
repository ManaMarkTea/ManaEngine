using Mana;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// What a widget is handed: the palette to draw with, the engine to create resources against, and the
    /// registry, so a widget can compose sections for the things it owns without knowing what they are.
    /// </summary>
    public sealed class InspectorContext
    {
        public InspectorContext(UiTheme theme, ManaContext? engine, InspectorWidgetFactory? widgets = null)
        {
            ArgumentNullException.ThrowIfNull(theme);

            Theme = theme;
            Engine = engine;
            Widgets = widgets ?? InspectorWidgetFactory.CreateDefault();
        }

        public UiTheme Theme { get; }

        /// <summary>
        /// The engine context, or null. Nullable because a widget can be built and laid out headlessly --
        /// which is how the suite covers all of this -- and only the parts that create engine resources,
        /// such as assigning a texture, actually need one.
        /// </summary>
        public ManaContext? Engine { get; }

        public InspectorWidgetFactory Widgets { get; }

        /// <summary>
        /// Measurements shared by every row -- the width of the name column, which the divider moves.
        /// Lives on the context so it survives the panel being rebuilt when the selection changes.
        /// </summary>
        public InspectorMetrics Metrics { get; } = new();

        /// <summary>Sections for something this widget owns -- a renderer's material, say.</summary>
        public List<UiElement> BuildAll(object? subject)
        {
            return Widgets.BuildAll(subject, this);
        }
    }
}
