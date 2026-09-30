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
        /// Keeps the editor room's walls and ceiling up. Off, they show only while drawing voxels, where they
        /// are the planes drawn against; the rest of the time the floor is enough.
        /// </summary>
        public static bool ShowRoom { get; set; }
    }
}
