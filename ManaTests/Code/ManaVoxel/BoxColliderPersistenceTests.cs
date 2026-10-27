using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

public static class BoxColliderPersistenceChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        context.Physics = new ManaPhysics(context);
        var physics = context.Physics;
        try
        {
            var scene = new ManaScene(context) { Name = "Collider persistence" };
            world.Scenes.Add(scene);
            var node = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = "Boxes", LevelSource = ManaLevelSource.Group, Scale = new Vector3(-3, 2, 1),
                Position = new Vector3(7, 8, 9)
            };
            scene.Children.Add(node);
            node.UpdateTransform();
            var box = new ManaBoxColliderComponent(node) { Size = new Vector3(3, 4, 5), Center = new Vector3(1, 2, 3) };
            node.Components.Add(box);
            node.Components.Add(new ManaBoxColliderComponent(node));
            var saved = ManaLevelCapture.CaptureScene(world, scene, scene.Name);
            Check("multiple colliders are serialized once each", saved.Nodes.Single().Scripts?.Count(s => s.Type == "BoxCollider") == 2
                && saved.Nodes.Single().Collision == null);
            var roundTrip = ManaLevel.FromJson(saved.ToJson());
            var loadedScene = new ManaScene(context) { Name = "Loaded colliders" };
            world.Scenes.Add(loadedScene);
            var result = ManaLevelLoader.Load(roundTrip, context, loadedScene, replaceAuthoredNodes: false);
            var loaded = result.Nodes.Single().Components.OfType<ManaBoxColliderComponent>().ToArray();
            Check("save and reload restores multiple physics colliders", result.Warnings.Count == 0 && loaded.Length == 2
                && loaded[0].Size == box.Size && loaded[0].Center == box.Center && physics.PhysicsMap.Count == 4);

            var assetPath = Path.Combine(Path.GetTempPath(), $"mana-collider-{Guid.NewGuid():N}.gltf");
            try
            {
                File.WriteAllText(assetPath, """
                    {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
                     "nodes":[{"name":"Model","children":[1]},{"name":"ChildMesh"}]}
                    """);
                var assetScene = new ManaScene(context) { Name = "Asset collision" };
                world.Scenes.Add(assetScene);
                var assetLevel = new ManaLevel
                {
                    Nodes = [new ManaLevelNode { Kind = ManaLevelSourceKind.Mesh, Name = "Model", Asset = assetPath }]
                };
                var assetRoot = ManaLevelLoader.Load(assetLevel, context, assetScene, replaceAuthoredNodes: false).Nodes.Single();
                ManaSceneNode FindChild(ManaSceneNode parent) => parent.Name == "ChildMesh" ? parent
                    : parent.Children.SelectMany(Descendants).First(child => child.Name == "ChildMesh");
                var assetChild = FindChild(assetRoot);
                assetChild.Components.Add(new ManaBoxColliderComponent(assetChild)
                    { Size = new Vector3(2, 3, 4), Center = new Vector3(0, 1, 0) });
                var capturedAsset = ManaLevel.FromJson(ManaLevelCapture.CaptureScene(world, assetScene, assetScene.Name).ToJson());
                Check("colliders on imported children are saved as scene overrides",
                    capturedAsset.Nodes.Single().ChildColliders?.Single().Boxes.Single().Size == new Vector3(2, 3, 4));
                var restoredScene = new ManaScene(context);
                world.Scenes.Add(restoredScene);
                var restoredAsset = ManaLevelLoader.Load(capturedAsset, context, restoredScene, replaceAuthoredNodes: false);
                var restoredBox = FindChild(restoredAsset.Nodes.Single()).Components.OfType<ManaBoxColliderComponent>().Single();
                Check("reloading a model restores its child collider", restoredAsset.Warnings.Count == 0
                    && restoredBox.Size == new Vector3(2, 3, 4) && restoredBox.Center == new Vector3(0, 1, 0));
                assetChild.Name = "Renamed child";
                var renamedSave = ManaLevel.FromJson(ManaLevelCapture.CaptureScene(world, assetScene, assetScene.Name).ToJson());
                var renamedLoad = ManaLevelLoader.Load(renamedSave, context, new ManaScene(context), replaceAuthoredNodes: false);
                Check("renaming imported children preserves collider identity", renamedLoad.Warnings.Count == 0
                    && Descendants(renamedLoad.Nodes.Single()).Single(n => n.Name == "Renamed child")
                        .Components.OfType<ManaBoxColliderComponent>().Single().Size == new Vector3(2, 3, 4));
                capturedAsset.Nodes.Single().ChildColliders![0].Names[^1] = "No longer exists";
                var changedHierarchy = ManaLevelLoader.Load(capturedAsset, context, new ManaScene(context), replaceAuthoredNodes: false);
                Check("changed mesh hierarchies report unmatched colliders instead of assigning them elsewhere",
                    changedHierarchy.Warnings.Any(warning => warning.Contains("hierarchy has changed")));
            }
            finally
            {
                File.Delete(assetPath);
            }
        }
        finally
        {
            physics.Simulation.Dispose();
            physics.ThreadDispatcher.Dispose();
            physics.BufferPool.Clear();
        }
    }

    private static IEnumerable<ManaSceneNode> Descendants(ManaSceneNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
