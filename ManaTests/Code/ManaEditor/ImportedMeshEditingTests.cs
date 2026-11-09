using System.Numerics;
using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Levels;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;
using ManaEditor.Code.Levels;
namespace ManaTests;
public static class ImportedMeshEditingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool ok) => check(name, ok, "");
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        var path = Path.Combine(Path.GetTempPath(), $"mana-edit-{Guid.NewGuid():N}.gltf");
        try
        {
            var bytes = new byte[42];
            Buffer.BlockCopy(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, 0, bytes, 0, 36);
            Buffer.BlockCopy(new ushort[] { 0, 1, 2 }, 0, bytes, 36, 6);
            File.WriteAllText(path, """
                {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
                "nodes":[{"name":"Model","children":[1,2]},{"name":"Chair","mesh":0},{"name":"Table","mesh":0}],
                "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
                "buffers":[{"byteLength":42,"uri":"data:application/octet-stream;base64,DATA"}],
                "bufferViews":[{"buffer":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],
                "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
                {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]}
                """.Replace("DATA", Convert.ToBase64String(bytes)));
            var scene = new ManaScene(context); world.Scenes.Add(scene);
            var level = new ManaLevel { Nodes = [new() { Kind = ManaLevelSourceKind.Mesh, Asset = path, MergeParts = true }] };
            var result = ManaLevelLoader.Load(level, context, scene, replaceAuthoredNodes: false);
            var root = result.Nodes.Single();
            ManaSceneNode Find(ManaSceneNode n, string name) => n.Name == name ? n : n.Children.SelectMany(All).First(c => c.Name == name);
            var chair = Find(root, "Chair");
            Check("batched imports retain visible editable hierarchy", !chair.IsEditorTransient && chair.Components.OfType<MeshRendererComponent>().Any());
            Check("opening an unchanged model produces no part overrides", ManaLevelCapture.CaptureScene(world, scene, "test").Nodes.Single().MeshParts == null);
            chair.Components.Add(new ManaGlowComponent(chair) { Strength = 2.5f });
            chair.Name = "My Chair"; chair.Position = new(4, 5, 6); chair.Scale = new(2, 3, 4);
            var renderer = chair.Components.OfType<MeshRendererComponent>().Single();
            renderer.DynamicMaterial.Roughness.Value = 0.23f;
            renderer.DynamicMaterial.Albedo.Value = new(0.2f, 0.3f, 0.4f, 1);
            var table = Find(root, "Table"); table.Parent!.RemoveChild(table);
            var saved = ManaLevel.FromJson(ManaLevelCapture.CaptureScene(world, scene, "test").ToJson());
            var restoredScene = new ManaScene(context); world.Scenes.Add(restoredScene);
            var restored = ManaLevelLoader.Load(saved, context, restoredScene, replaceAuthoredNodes: false);
            var edited = Find(restored.Nodes.Single(), "My Chair");
            Check("renames and transforms survive reload", restored.Warnings.Count == 0 && edited.Position == chair.Position && edited.Scale == chair.Scale);
            Check("added components survive reload", edited.Components.OfType<ManaGlowComponent>().Single().Strength == 2.5f);
            var material = edited.Components.OfType<MeshRendererComponent>().Single().Material;
            Check("material edits survive reload", MathF.Abs(material.Roughness.Value - 0.23f) < 0.0001f && material.Albedo.Value == renderer.Material.Albedo.Value);
            Check("deleted imported parts stay deleted", !All(restored.Nodes.Single()).Any(n => n.Name == "Table"));
            var again = ManaLevelCapture.CaptureScene(world, restoredScene, "test");
            Check("saved imported edits reopen clean", saved.ToJson() == again.ToJson());
        }
        finally { File.Delete(path); }
    }
    private static IEnumerable<ManaSceneNode> All(ManaSceneNode n)
    {
        yield return n;
        foreach (var child in n.Children) foreach (var item in All(child)) yield return item;
    }
}
