using System.Numerics;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Models;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>Merging a static model's parts: one mesh per shared material, each part where its node put it.</summary>
public static class MeshBatchChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        var brick = new ManaMaterial(context) { Name = "Brick" };
        var glass = new ManaMaterial(context) { Name = "Glass" };

        var root = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Block" };
        root.AddChild(Part(context, brick, new Vector3(0, 0, 0)));
        root.AddChild(Part(context, brick, new Vector3(10, 0, 0)));
        var group = new ManaSceneNode(context, Matrix4x4.CreateTranslation(0, 5, 0)) { Name = "Upper" };
        group.AddChild(Part(context, glass, new Vector3(0, 0, 3)));
        root.AddChild(group);

        var made = ManaMeshBatch.MergeInPlace(context, root);
        var merged = root.Children.OfType<ManaMeshSceneNode>().ToList();
        check("parts sharing a material merge into one mesh, one per material",
            made == 2 && merged.Count == 2 && root.Children.Count == 2, $"{made} made, {root.Children.Count} children");

        var bricks = merged.FirstOrDefault(node => node.MeshRenderer.Material == brick);
        check("the merged mesh keeps the parts' own material", bricks != null && merged.Any(node => node.MeshRenderer.Material == glass), "");
        if (bricks?.Mesh.CpuData is { } data)
        {
            var world = data.Positions.Select(position => position + bricks.Position).ToList();
            check("and holds every part's triangles, each where its node stood",
                data.Indexes.Length == 6 && world.Any(p => Vector3.Distance(p, new Vector3(10, 0, 0)) < 1e-4f)
                    && world.Any(p => Vector3.Distance(p, Vector3.Zero) < 1e-4f), string.Join(" ", world));
        }
        var panes = merged.FirstOrDefault(node => node.MeshRenderer.Material == glass);
        check("a part under a moved group lands where the group put it",
            panes?.Mesh.CpuData?.Positions.Select(position => position + panes.Position)
                .Any(p => Vector3.Distance(p, new Vector3(0, 5, 3)) < 1e-4f) == true, "");

        // WebGPU caps a buffer's size, so a material with more vertices than the batch allows goes on in a second mesh.
        var capped = new ManaMeshBatch { GroupByMaterial = true, MaxVertices = 3 };
        capped.Add(Triangle(context, brick), brick, Matrix4x4.Identity);
        capped.Add(Triangle(context, brick), brick, Matrix4x4.CreateTranslation(4, 0, 0));
        var chunks = capped.Build(context, "Capped");
        check("a material past the vertex cap carries on in another mesh",
            chunks.Count == 2 && chunks.All(node => node.Components.OfType<Mana.MeshRendererComponent>().Single().Mesh.CpuData?.Positions.Length == 3), $"{chunks.Count} meshes");
    }

    private static ManaSceneNode Part(ManaContext context, ManaMaterial material, Vector3 position)
    {
        var node = new ManaMeshSceneNode(context, Triangle(context, material)) { Name = material.Name };
        node.SetLocalTransform(Matrix4x4.CreateTranslation(position));
        node.MeshRenderer.Material = material;
        return node;
    }

    private static ManaMesh Triangle(ManaContext context, ManaMaterial material)
    {
        var mesh = new ManaMesh(context) { Name = "Triangle", Materials = new[] { material } };
        mesh.SetCpuData(new ManaMeshCpuData
        {
            Indexes = new uint[] { 0, 1, 2 },
            Positions = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
            Normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ },
            Colors = new[] { Vector4.One, Vector4.One, Vector4.One },
            TextureCoordinates0 = new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY },
            TextureCoordinates1 = new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero }
        });
        return mesh;
    }
}
