using ManaUI;

namespace Mana.Code.UI
{
    /// <summary>
    /// The viewport's Show Gizmos switch: editor helpers drawn in the scene -- the transform handles, collider
    /// outlines, grid map cell outlines -- as opposed to the scene itself. Off shows the level as it will look.
    /// </summary>
    public static class ManaEditorGizmos
    {
        public static bool Visible { get; set; } = true;

        /// <summary>
        /// What is left of the window once docked panels have taken their share, in logical UI pixels --
        /// published every frame by <c>ViewportOverlayLayer.Viewport</c>, which is the only place that
        /// actually knows it. <see cref="ManaUI.UiRect.Empty"/> (the default) means no UI frame has run yet
        /// or there is no dock layout at all; readers should treat that as "the whole window".
        /// </summary>
        public static UiRect Viewport { get; set; } = UiRect.Empty;

        /// <summary>
        /// Keeps the editor room's walls and ceiling up. Off, they show only while drawing voxels, where they
        /// are the planes drawn against; the rest of the time the floor is enough.
        /// </summary>
        public static bool ShowRoom { get; set; }
    }
}
