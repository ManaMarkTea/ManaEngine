namespace Mana.Code.Content
{
    /// <summary>
    /// A pointer to a user-made asset in Content/: its id, which is what is trusted, and where it was last seen,
    /// relative to Content/ -- so a file that is moved or renamed is still found. The same rule prefabs use.
    /// </summary>
    public sealed record ManaAssetRef
    {
        // A parameterless constructor and settable properties, not a positional record: a trimmed build (the web one)
        // strips constructor parameter names, and JSON could then not make one at all.
        public ManaAssetRef()
        {
        }

        public ManaAssetRef(string assetId, string assetPath)
        {
            AssetId = assetId;
            AssetPath = assetPath;
        }

        public string AssetId { get; init; } = string.Empty;
        public string AssetPath { get; init; } = string.Empty;

        public static readonly ManaAssetRef None = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsEmpty => AssetId.Length == 0 && AssetPath.Length == 0;

        public override string ToString() => IsEmpty ? "none" : Path.GetFileNameWithoutExtension(AssetPath);
    }

    /// <summary>Marks a <see cref="ManaAssetRef"/> property with the kind of asset it takes, by file extension, for the inspector's picker.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AssetKindAttribute(string extension) : Attribute
    {
        public string Extension { get; } = extension;
    }
}
