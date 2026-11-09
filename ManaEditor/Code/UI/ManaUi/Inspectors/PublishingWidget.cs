using Mana;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>A selected scene's publishing: whether the web build's level selector lists it, with what line, in what place.</summary>
    public sealed class PublishingWidget : InspectorWidget<ManaScene>
    {
        protected override UiElement Build(ManaScene scene, InspectorContext context) =>
            new ReflectedPropertiesWidget { Title = _ => "Publishing" }.Build(scene.Publishing, context);
    }
}
