using Mana;

namespace ManaEditor.Code.Assets;

/// <summary>Asset browsing and thumbnail workers owned by one editor world.</summary>
public sealed class EditorAssetServices : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ManaWorld, EditorAssetServices> Worlds = new();

    public static EditorAssetServices For(ManaWorld world) => Worlds.GetValue(world, _ => new EditorAssetServices());

    public MeshAssetLibrary Library { get; } = new();
    public MeshThumbnailCache? Thumbnails { get; private set; }

    /// <summary>Takes ownership; replacement stops the previous worker before releasing it.</summary>
    public void SetThumbnailCache(MeshThumbnailCache? cache)
    {
        if (ReferenceEquals(Thumbnails, cache))
        {
            return;
        }
        Thumbnails?.Dispose();
        Thumbnails = cache;
    }

    public void Dispose()
    {
        SetThumbnailCache(null);
        Library.Dispose();
    }
}
