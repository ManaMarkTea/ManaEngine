namespace Mana.Code.UI
{
    /// <summary>
    /// The viewport's Show Gizmos switch: editor helpers drawn in the scene -- the transform handles, collider
    /// outlines, grid map cell outlines -- as opposed to the scene itself. Off shows the level as it will look.
    /// </summary>
    public static class ManaEditorGizmos
    {
        public static bool Visible { get; set; } = true;
    }
}
