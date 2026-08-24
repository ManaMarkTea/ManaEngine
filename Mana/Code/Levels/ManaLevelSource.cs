namespace Mana.Code.Levels
{
    /// <summary>What kind of authored thing a node is, and therefore how a level file rebuilds it.</summary>
    public enum ManaLevelSourceKind
    {
        /// <summary>A procedural primitive, rebuilt by <see cref="Shapes.ManaShapeFactory"/>.</summary>
        Shape,

        /// <summary>A glTF/GLB asset, addressed the same way the mesh picker addresses one.</summary>
        Mesh,

        /// <summary>A <see cref="Nodes.ManaVoxelsNode"/>, whose grid is written into the level itself.</summary>
        Voxels,

        /// <summary>A <see cref="Nodes.ManaPlayerStartNode"/> -- where Play drops the character.</summary>
        PlayerStart,

        /// <summary>An empty scene node that owns authored children.</summary>
        Group
    }

    /// <summary>
    /// How a scene node came to exist, recorded on the node itself so a level can recreate it later.
    /// <para>
    /// This is the line between authored content and demo scenery: saving walks the scene graph and keeps
    /// only the nodes carrying one of these. Everything the game mode builds in code -- grid planes, the
    /// planet, streamed terrain patches, border overlays -- has no source and is skipped, which is what
    /// keeps a level file to the things somebody actually placed rather than a snapshot of a whole world
    /// that could not be meaningfully rebuilt anyway.
    /// </para>
    /// <para>
    /// <see cref="Size"/> is the argument the shape factory was called with, not the node's scale. The two
    /// differ by construction: AddCube bakes its size into the mesh and leaves the node at scale 1, while
    /// AddSphere builds a unit mesh and puts the size on the node. Recording the factory argument means
    /// loading can call the same method and then apply the saved transform on top, and both cases land
    /// where they started.
    /// </para>
    /// </summary>
    public sealed record ManaLevelSource
    {
        public ManaLevelSourceKind Kind { get; init; }

        /// <summary>Primitive name for <see cref="ManaLevelSourceKind.Shape"/>; see ManaShapeFactory.</summary>
        public string? Shape { get; init; }

        /// <summary>Asset reference for <see cref="ManaLevelSourceKind.Mesh"/> -- a path, or a zip-encoded entry.</summary>
        public string? Asset { get; init; }

        public Vector4 Color { get; init; } = Vector4.One;

        public Vector3 Size { get; init; } = Vector3.One;

        public static ManaLevelSource ForShape(string shape, Vector4 color, Vector3 size) =>
            new() { Kind = ManaLevelSourceKind.Shape, Shape = shape, Color = color, Size = size };

        public static ManaLevelSource ForMesh(string asset) =>
            new() { Kind = ManaLevelSourceKind.Mesh, Asset = asset };

        public static ManaLevelSource Group { get; } = new() { Kind = ManaLevelSourceKind.Group };

        public static ManaLevelSource Voxels { get; } = new() { Kind = ManaLevelSourceKind.Voxels };

        public static ManaLevelSource PlayerStart { get; } = new() { Kind = ManaLevelSourceKind.PlayerStart };
    }
}
