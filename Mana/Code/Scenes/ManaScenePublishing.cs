namespace Mana
{
    /// <summary>
    /// Where a level goes beyond the editor. The web build's level selector lists a level only when
    /// <see cref="PublishToWeb"/> is on (Tools/WebAssets/publish_assets.py reads it from the level's <c>web</c> block).
    /// </summary>
    public sealed class ManaScenePublishing
    {
        /// <summary>List this level in the web build's level selector.</summary>
        public bool PublishToWeb { get; set; }

        /// <summary>The line under its name on the selector's card.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Its place in the selector: lower comes first; levels with the same order go by name.</summary>
        public int Order { get; set; } = DefaultOrder;

        public const int DefaultOrder = 100;

        public bool IsDefault => !PublishToWeb && Description.Length == 0 && Order == DefaultOrder;
    }
}
