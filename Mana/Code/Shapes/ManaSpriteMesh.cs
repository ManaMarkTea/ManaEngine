using ManaCore.Graphics;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;

namespace Mana.Code.Shapes
{
    /// <summary>
    /// A flat, textured quad in the XY plane facing +Z -- the mesh a sprite draws with. Its size is the picture's
    /// in world units, and <see cref="Pivot"/> (0..1 across and up) is the point that sits on the node: (0.5, 0) puts
    /// a character's feet on its position. The picture and its normal map go in the mesh data, as a model's do, so
    /// the PBR pass lights it like anything else; the material's UV scale and offset choose a sheet frame.
    /// </summary>
    public sealed class ManaSpriteMesh : ManaMesh
    {
        public Vector2 Size { get; }
        public Vector2 Pivot { get; }

        public ManaSpriteMesh(ManaContext context, string texturePath, string? normalMapPath, Vector2 size, Vector2 pivot, Vector4 tint, bool pixelArt = false)
            : base(context)
        {
            Size = size;
            Pivot = pivot;
            Name = $"Sprite {Path.GetFileNameWithoutExtension(texturePath)}";

            var left = -pivot.X * size.X;
            var bottom = -pivot.Y * size.Y;
            var right = left + size.X;
            var top = bottom + size.Y;

            // Pictures load flipped, bottom row first (OpenGL's way round), so the bottom edge reads v = 0.
            SetCpuData(new ManaMeshCpuData
            {
                Positions = [new(left, bottom, 0), new(right, bottom, 0), new(right, top, 0), new(left, top, 0)],
                Normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
                Colors = [Vector4.One, Vector4.One, Vector4.One, Vector4.One],
                TextureCoordinates0 = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
                TextureCoordinates1 = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
                Indexes = [0, 1, 2, 0, 2, 3],
                AlbedoTexturePath = texturePath,
                NormalTexturePath = normalMapPath,
                NearestFiltering = pixelArt
            });

            var material = new ManaMaterial(context)
            {
                Name = "Sprite Material",
                Shader = new ManaPbrShader(context),
                // Sprites are pictures with holes: cut out what is transparent, and let nothing shine.
                OpacityMode = ManaOpacityMode.Cutout,
                AlphaCutoff = 0.5
            };
            material.Albedo.Value = tint;
            material.Roughness.Value = 1.0f;
            material.Metallic.Value = 0.0f;
            // The selector defaults to the vertex normal (strength 0); a sprite given a normal map means to use it.
            material.NormalSelector = normalMapPath != null ? 0.0 : 1.0;
            Materials = [material];
        }
    }
}
