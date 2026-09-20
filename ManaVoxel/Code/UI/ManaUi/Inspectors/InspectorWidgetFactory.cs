using Mana;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The registry the inspector asks "what shows this?".
    /// <para>
    /// Several widgets may claim the same subject, and that is the point: a material is a name and a shader
    /// (<see cref="MaterialWidget"/>), a set of PBR channels when its shader reads them
    /// (<see cref="PbrShaderWidget"/>), and a set of custom uniforms when it has any
    /// (<see cref="ShaderUniformsWidget"/>). Each is a separate section, and each decides for itself
    /// whether it applies.
    /// </para>
    /// <para>
    /// <see cref="BuildAll"/> keeps registration order, so sections appear in the order they were added.
    /// <see cref="For"/> searches backwards, so an application registering its own widget for a type the
    /// defaults already cover replaces it rather than fighting it.
    /// </para>
    /// </summary>
    public sealed class InspectorWidgetFactory
    {
        private readonly List<IInspectorWidget> _widgets = new();

        /// <summary>
        /// Shown when nothing else claims a subject. Reflection over its own properties by default, which
        /// beats a placeholder: most components are a handful of floats and flags.
        /// <para>
        /// Set to null to get nothing instead, which is what a release build of a game UI would want.
        /// </para>
        /// </summary>
        public IInspectorWidget? Fallback { get; set; } = DefaultFallback();

        /// <summary>
        /// Reflection, minus the plumbing every component inherits. Owner and Position come from
        /// <c>ManaComponent</c> and say nothing about what a particular component is.
        /// </summary>
        private static IInspectorWidget DefaultFallback()
        {
            var reflected = new ReflectedPropertiesWidget();
            reflected.IgnoredDeclaringTypes.Add(typeof(ManaComponent));
            reflected.IgnoredDeclaringTypes.Add(typeof(ManaObject));
            return reflected;
        }

        /// <summary>Everything the engine knows how to inspect out of the box.</summary>
        public static InspectorWidgetFactory CreateDefault()
        {
            return new InspectorWidgetFactory()
                .Register(new SceneWidget())
                .Register(new NodeIdentityWidget())
                .Register(new TransformWidget())
                .Register(new LevelSourceWidget())
                .Register(new CollisionWidget())
                .Register(new MeshRendererWidget())
                .Register(new SkeletalAnimationWidget())
                .Register(new LightWidget())
                .Register(new AudioSourceWidget())
                .Register(new CameraWidget())
                .Register(new FlyingMountWidget())
                .Register(new CameraMouseInputWidget())
                .Register(new MaterialWidget())
                .Register(new PbrShaderWidget())
                .Register(new ShaderUniformsWidget())
                .Register(new PlanetWidget())
                .Register(new TerrainCacheWidget())
                .Register(new RegionHexTilingWidget())
                .Register(new LineMeshWidget())
                .Register(new TerrainPatchWidget());
        }

        public IReadOnlyList<IInspectorWidget> Widgets => _widgets;

        public InspectorWidgetFactory Register(IInspectorWidget widget)
        {
            ArgumentNullException.ThrowIfNull(widget);

            _widgets.Add(widget);
            return this;
        }

        /// <summary>The most recently registered widget that claims the subject, or null.</summary>
        public IInspectorWidget? For(object? subject)
        {
            if (subject == null)
            {
                return null;
            }

            for (var i = _widgets.Count - 1; i >= 0; i--)
            {
                if (_widgets[i].CanInspect(subject))
                {
                    return _widgets[i];
                }
            }

            return null;
        }

        /// <summary>Every widget that claims the subject, in registration order.</summary>
        public IEnumerable<IInspectorWidget> AllFor(object? subject)
        {
            if (subject == null)
            {
                yield break;
            }

            foreach (var widget in _widgets)
            {
                if (widget.CanInspect(subject))
                {
                    yield return widget;
                }
            }
        }

        /// <summary>
        /// Every section for the subject, or the <see cref="Fallback"/>'s when none claim it. Empty only
        /// when there is no fallback, or when the fallback has nothing to show either.
        /// </summary>
        public List<UiElement> BuildAll(object? subject, InspectorContext context)
        {
            var built = new List<UiElement>();

            foreach (var widget in AllFor(subject))
            {
                built.Add(widget.Build(subject!, context));
            }

            if (built.Count == 0 && subject != null && Fallback?.CanInspect(subject) == true)
            {
                built.Add(Fallback.Build(subject, context));
            }

            return built;
        }

        public UiElement? Build(object? subject, InspectorContext context)
        {
            return For(subject)?.Build(subject!, context);
        }
    }
}
