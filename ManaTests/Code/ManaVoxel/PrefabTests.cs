using System.Numerics;
using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

public static class PrefabChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool ok, string detail = "") => check(name, ok, detail);
        static ManaLevelNode Clone(ManaLevelNode node) => ManaLevel.FromJson(new ManaLevel { Nodes = [node] }.ToJson()).Nodes[0];
        static ManaLevelNode Group(string id, string name) => new() { Id = id, Name = name, Kind = ManaLevelSourceKind.Group };
        static IEnumerable<UiElement> Elements(UiElement element)
        {
            yield return element;
            foreach (var child in element.VisualChildren)
            {
                foreach (var descendant in Elements(child)) { yield return descendant; }
            }
        }
        static (ManaWorld World, ManaContext Context) World()
        {
            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            world.Context = context;
            world.DefaultScene = new ManaScene(context);
            world.Scenes.Add(world.DefaultScene);
            return (world, context);
        }

        var source = Group("root", "Door");
        source.Children = [Group("hinge", "Hinge"), Group("frame", "Frame")];
        source.Children[0].Scripts = [new() { Id = "spring", Type = "SpringPad", Properties = new() { ["speed"] = JsonSerializer.SerializeToElement(5) } }];
        var modified = Clone(source);
        modified.Children![0].Position = [3, 0, 0];
        modified.Children[0].Scripts![0].Properties["speed"] = JsonSerializer.SerializeToElement(12);
        var overrides = ManaPrefabOverrides.Diff(ManaPrefabOverrides.Flatten(source), ManaPrefabOverrides.Flatten(modified));
        var updated = Clone(source);
        updated.Children!.Reverse();
        updated.Children[1].Name = "Renamed Hinge";
        updated.Children[0].Scale = [2, 2, 2];
        var resolved = ManaPrefabOverrides.Resolve(updated, overrides);
        Check("overrides follow persistent IDs through source rename and reorder", resolved.Children![1].Name == "Renamed Hinge" && resolved.Children[1].Position[0] == 3);
        Check("unmodified fields inherit source updates", resolved.Children![0].Scale[0] == 2);
        Check("component overrides use component IDs", resolved.Children![1].Scripts![0].Properties["speed"].GetInt32() == 12);
        Check("resolving does not mutate the source", updated.Children[1].Position[0] == 0);

        var added = Clone(source);
        added.Children!.Add(Group("extra", "Local Child"));
        added.Children.RemoveAt(1);
        var structural = ManaPrefabOverrides.Diff(ManaPrefabOverrides.Flatten(source), ManaPrefabOverrides.Flatten(added));
        var structuralResult = ManaPrefabOverrides.Resolve(updated, structural);
        Check("added and removed instance children survive source updates", structuralResult.Children!.Any(n => n.Id == "extra") && structuralResult.Children!.All(n => n.Id != "frame"));
        var removedSource = Clone(source);
        removedSource.Children!.RemoveAt(0);
        var warnings = new List<string>();
        var missing = ManaPrefabOverrides.Resolve(removedSource, overrides, warnings);
        Check("deleted source nodes are not resurrected by property overrides", missing.Children!.Count == 1 && warnings.Count == 2);
        var prefab = new ManaPrefab { Root = source };
        prefab.Validate();
        var duplicate = new ManaPrefab { Root = Clone(source) };
        duplicate.Root.Children![1].Id = "hinge";
        try { duplicate.Validate(); Check("duplicate node identities rejected", false); }
        catch (InvalidDataException) { Check("duplicate node identities rejected", true); }
        var nested = new ManaPrefab { Root = new() { Kind = ManaLevelSourceKind.Prefab } };
        try { nested.Validate(); Check("nested prefab cycles cannot enter the asset format", false); }
        catch (InvalidDataException) { Check("nested prefab cycles cannot enter the asset format", true); }

        var previousDirectory = Environment.CurrentDirectory;
        var scratch = Path.Combine(Path.GetTempPath(), "mana-prefab-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(scratch, "Content"));
        try
        {
            Environment.CurrentDirectory = scratch;
            var (world, context) = World();
            var editor = new VoxelEditorViewModel();
            editor.AttachScene(context, world.DefaultScene);
            var model = ProjectAssetViewModel.For(world, editor);
            var parent = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Door", LevelSource = ManaLevelSource.Group, Position = new(8, 0, 2) };
            var child = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Hinge", LevelSource = ManaLevelSource.Group, Position = new(0, 2, 0) };
            parent.AddChild(child);
            world.DefaultScene.Children.Add(parent);
            world.Selection.SelectedNode = parent;
            model.AssetName = "Door";
            model.CreatePrefab(parent);
            var instance = world.Selection.SelectedNode!;
            Check("make prefab on a tree node makes an asset and a linked instance", instance.PrefabLink != null && model.Entries.Count == 1, model.Status);
            Check("creation preserves world placement and the full tree", instance.Position.X == 8 && instance.Children[0].Position == Vector3.Zero && instance.Children[0].Children.Count == 1);
            Check("fresh instance has no false overrides", ManaPrefabRuntime.CaptureReference(instance).Overrides.Count == 0);
            var path = model.Entries.Single().Path;
            var saved = ManaPrefab.Load(path);
            Check("saved prefab retains persistent node IDs", saved.Root.Id == parent.AuthoredId && saved.Root.Children![0].Id == child.AuthoredId);

            child.Position = new(0, 4, 0);
            var level = ManaLevelCapture.Capture(world);
            Check("level stores reference plus overrides, not expanded prefab children", level.Nodes[0].Prefab?.Overrides.Count == 1 && level.Nodes[0].Children == null);
            var (reloadedWorld, reloadedContext) = World();
            var load = ManaLevelLoader.Load(ManaLevel.FromJson(level.ToJson()), reloadedContext, reloadedWorld.DefaultScene, false);
            var reloaded = reloadedWorld.DefaultScene.Children.Single();
            Check("level round trip rebuilds instance placement and overrides", load.Warnings.Count == 0 && reloaded.Position.X == 8 && reloaded.Children[0].Children[0].Position.Y == 4, string.Join(';', load.Warnings));
            Check("loaded hierarchy has no duplicate top-level children", reloadedWorld.DefaultScene.Children.Count == 1 && reloaded.Children.Count == 1);

            saved.Root.Children![0].Name = "Updated Hinge";
            saved.Root.Children.Add(Group("new-child", "Handle"));
            saved.Save(path);
            model.RefreshSelected();
            instance = world.Selection.SelectedNode!;
            Check("source refresh propagates hierarchy and names while retaining instance overrides", instance.Children[0].Children.Count == 2 && instance.Children[0].Children[0].Name == "Updated Hinge" && instance.Children[0].Children[0].Position.Y == 4, model.Status);
            model.Revert(instance);
            instance = world.Selection.SelectedNode!;
            Check("revert restores source defaults and retains placement", instance.Position.X == 8 && instance.Children[0].Children[0].Position.Y == 2);

            var originalScene = world.DefaultScene;
            model.OpenPrefab(path);
            Check("prefab editing isolates the level", model.IsEditing && !world.Scenes.Contains(originalScene), model.Status);
            world.Selection.SelectedNode = world.DefaultScene.Children.Single().Children[0];
            var choice = model.PropertyChoices().Single(c => c.Property == "name");
            model.Expose(choice, "Hinge label", null, null);
            world.Selection.SelectedNode.Name = "Source Edit";
            model.SavePrefab(true);
            Check("save and close returns to the original level", !model.IsEditing && world.DefaultScene == originalScene, model.Status);
            instance = world.DefaultScene.Children.Single();
            Check("saving isolated source updates existing instances", instance.Children[0].Children[0].Name == "Source Edit");
            saved = ManaPrefab.Load(path);
            Check("exposed property definitions persist", saved.Properties.Single().Name == "Hinge label");
            model.SetProperty(instance, saved.Properties.Single(), "Instance Edit");
            instance = world.DefaultScene.Children.Single();
            Check("exposed property edits target the correct descendant", instance.Children[0].Children[0].Name == "Instance Edit", model.Status);
            model.Revert(instance, saved.Properties.Single());
            instance = world.DefaultScene.Children.Single();
            Check("individual exposed property revert restores source", instance.Children[0].Children[0].Name == "Source Edit");

            model.SetProperty(instance, saved.Properties.Single(), "Source Edit");
            instance = world.DefaultScene.Children.Single();
            Check("an explicit override equal to the default stays pinned", ManaPrefabRuntime.CaptureReference(instance).Overrides.Count == 1);
            saved.Root.Children![0].Name = "New Default";
            saved.Save(path);
            model.RefreshSelected();
            instance = world.DefaultScene.Children.Single();
            Check("pinned override survives a later source default change", instance.Children[0].Children[0].Name == "Source Edit");
            model.Revert(instance);
            instance = world.DefaultScene.Children.Single();
            Check("revert uses the latest source default", instance.Children[0].Children[0].Name == "New Default");

            var intact = instance;
            var healthySource = File.ReadAllText(path);
            File.WriteAllText(path, "invalid json");
            model.RefreshSelected();
            Check("a broken source refresh retains the old instance", world.DefaultScene.Children.Single() == intact);
            File.WriteAllText(path, healthySource);

            model.OpenPrefab(path);
            var externalEdit = ManaPrefab.Load(path);
            externalEdit.Name = "Externally edited";
            externalEdit.Save(path);
            model.SavePrefab();
            Check("saving refuses to overwrite concurrent source edits", model.Status.Contains("changed on disk") && ManaPrefab.Load(path).Name == "Externally edited");
            model.DiscardEditing();
            File.WriteAllText(path, healthySource);

            var moved = Path.Combine(scratch, "Content", "Moved", "Renamed.manaprefab");
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.Move(path, moved);
            model.RefreshSelected();
            instance = world.DefaultScene.Children.Single();
            Check("asset identity survives file and folder renames", instance.PrefabLink!.AssetPath == "Moved/Renamed.manaprefab", model.Status);
            var referenceBeforeMissing = ManaLevelCapture.Capture(world);
            File.Move(moved, moved + ".backup");
            var (brokenWorld, brokenContext) = World();
            var broken = ManaLevelLoader.Load(referenceBeforeMissing, brokenContext, brokenWorld.DefaultScene, false);
            Check("missing source keeps a recoverable reference and reports the problem", broken.Warnings.Count > 0 && ManaLevelCapture.Capture(brokenWorld).Nodes[0].Prefab?.AssetId == saved.Id);
            File.Move(moved + ".backup", moved);
            model.OpenPrefab(moved);
            world.DefaultScene.Children.Single().Name = "Discard me";
            model.DiscardEditing();
            Check("discard leaves both source and original instance unchanged", ManaPrefab.Load(moved).Root.Name == "Door" && world.DefaultScene.Children.Single().Children[0].Name == "Door");
            instance = world.DefaultScene.Children.Single();
            model.Unpack(instance);
            Check("unpack retains the full tree without a source link", instance.LevelSource?.Kind == ManaLevelSourceKind.Group && instance.PrefabLink == null && instance.Children[0].Children.Count == 2);

            model.Place(moved, new Vector3(15, 0, 0));
            var firstPlacement = world.Selection.SelectedNode!;
            model.Place(moved, new Vector3(25, 0, 0));
            var secondPlacement = world.Selection.SelectedNode!;
            Check("placements have independent roots and instance identities", firstPlacement != secondPlacement && firstPlacement.AuthoredId != secondPlacement.AuthoredId && firstPlacement.Position.X == 15 && secondPlacement.Position.X == 25);
            secondPlacement.Children[0].Children[0].Name = "Published";
            model.ApplyToSource(secondPlacement);
            var linked = world.DefaultScene.Children.Where(n => n.PrefabLink != null).ToArray();
            Check("apply publishes source changes to all placed instances", linked.Length == 2 && linked.All(n => n.Children[0].Children[0].Name == "Published"), model.Status);
            Check("apply preserves separate placements", linked.Select(n => n.Position.X).Order().SequenceEqual(new[] { 15f, 25f }));
            var missingRoot = linked.Single(n => n.Position.X == 25);
            missingRoot.Children.Clear();
            model.Revert(missingRoot);
            Check("revert can recover an accidentally deleted instance content root", world.DefaultScene.Children.Single(n => n.PrefabLink != null && n.Position.X == 25).Children.Count == 1, model.Status);

            model.Refresh();
            var rootUi = new UiRoot(fonts) { Content = ProjectManaView.Build(UiTheme.Create(UiThemeKind.Dark), world, editor) };
            rootUi.UpdateLayout(new Vector2(1100, 300));
            Check("project panel builds and lays out headlessly", rootUi.Render().Instances.Length > 0);

            Check("project browser has a folder tree and thumbnail tiles", Elements(rootUi).OfType<TreeView>().Any()
                && Elements(rootUi).OfType<UiImage>().Any() && Elements(rootUi).OfType<MeshAssetRow>().Any());
            var tile = Elements(rootUi).OfType<MeshAssetRow>().First();
            rootUi.UpdateLayout(new Vector2(1100, 300));
            Check("browser retains tile identity between frames", Elements(rootUi).OfType<MeshAssetRow>().Contains(tile));
            var start = tile.Bounds.Center;
            MeshImportRequests.Clear();
            rootUi.Input.InjectPointerMoved(start);
            rootUi.Input.InjectPointerPressed(start, PointerButton.Left, 0);
            rootUi.Input.InjectPointerMoved(start + new Vector2(50, 10));
            Check("prefab tile starts an Import placement drag", MeshImportRequests.Dragging == moved);
            var drop = new Vector2(1200, 500);
            rootUi.Input.InjectPointerMoved(drop);
            rootUi.Input.InjectPointerReleased(drop, PointerButton.Left);
            var droppedPath = MeshImportRequests.TakeDrag();
            Check("viewport drop leaves the request for the placement controller", droppedPath == moved && !MeshImportRequests.PointerOverUi,
                $"path={droppedPath}; over UI={MeshImportRequests.PointerOverUi}; hit={rootUi.HitTest(drop)?.GetType().Name}");
            var menu = ProjectManaView.AssetMenu(model, model.Entries.Single());
            menu.Single(item => item.Text == "Place").Invoked!();
            Check("Place uses the viewport preview request", MeshImportRequests.TakePlacement() == moved);
            var detached = new ManaScene(context);
            editor.AttachScene(context, detached);
            var placedInWorld = model.InstantiateForPlacement(moved);
            Check("placement recovers from a detached active scene", world.DefaultScene.Children.Contains(placedInWorld) && placedInWorld.PrefabLink != null);
            world.DefaultScene.Children.Remove(placedInWorld);
            editor.AttachScene(context, world.DefaultScene);

            var thumbnailPath = Path.Combine(scratch, "Content", "Prefabs", "Thumbnail.manaprefab");
            new ManaPrefab
            {
                Name = "Thumbnail", Root = new ManaLevelNode
                {
                    Id = "thumb-root", Kind = ManaLevelSourceKind.Group, Children =
                    [new() { Id = "thumb-cube", Kind = ManaLevelSourceKind.Shape, Shape = "Cube", Size = [1, 2, 1], Color = [0.2f, 0.5f, 1, 1] }]
                }
            }.Save(thumbnailPath);
            var nodeCount = world.DefaultScene.Children.Count;
            var pixels = MeshThumbnailCache.GenerateViaCpuStandalone(context, thumbnailPath);
            Check("prefab hierarchy produces a visible thumbnail", pixels != null && pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0));
            Check("thumbnail loading leaves the live world untouched", world.DefaultScene.Children.Count == nodeCount);
            model.Refresh();
            model.OpenPrefab(thumbnailPath);
            model.DeleteAsset(thumbnailPath);
            Check("the currently edited source cannot be deleted", File.Exists(thumbnailPath) && model.Status.Contains("Close"));
            model.DiscardEditing();
            model.DeleteAsset(thumbnailPath);
            Check("asset deletion removes the source and refreshes the list", !File.Exists(thumbnailPath) && model.Entries.All(entry => entry.Path != thumbnailPath));
            menu.Single(item => item.Text == "Delete").Invoked!();
            Check("deleting a prefab keeps placed scene instances intact", !File.Exists(moved) && world.DefaultScene.Children.Count == nodeCount
                && world.DefaultScene.Children.Any(node => node.PrefabLink != null));
            MeshImportRequests.Clear();

            try { ManaContentLibrary.ResolvePrefab(saved.Id, "../outside.manaprefab"); Check("references cannot escape Content", false); }
            catch (InvalidDataException) { Check("references cannot escape Content", true); }

            var (physicalWorld, physicalContext) = World();
            physicalContext.Physics = new ManaPhysics(physicalContext);
            try
            {
                var box = new ManaSceneNode(physicalContext, Matrix4x4.Identity) { LevelSource = ManaLevelSource.Group, Name = "Collider Prefab" };
                physicalWorld.DefaultScene.Children.Add(box);
                box.Components.Add(new ManaBoxColliderComponent(box));
                var physicalModel = ProjectAssetViewModel.For(physicalWorld);
                physicalWorld.Selection.SelectedNode = box;
                physicalModel.AssetName = "Collider";
                physicalModel.CreatePrefab();
                var count = physicalContext.Physics.PhysicsMap.Count;
                physicalModel.RefreshSelected();
                Check("refresh replaces colliders without leaking bodies or mutating an enumerated list", physicalContext.Physics.PhysicsMap.Count == count && physicalModel.Status == "Instance refreshed from its source.", physicalModel.Status);
                var physicalInstance = physicalWorld.Selection.SelectedNode!;
                var physicalPath = physicalModel.Entries.Single(e => e.Name == "Collider").Path;
                var physicalAsset = ManaPrefab.Load(physicalPath);
                var size = new ManaPrefabProperty
                {
                    Name = "Collision size", NodeId = physicalAsset.Root.Id!, ComponentId = physicalAsset.Root.Scripts!.Single().Id,
                    Property = "size", Minimum = 0.1, Maximum = 10
                };
                physicalAsset.Properties.Add(size);
                physicalAsset.Save(physicalPath);
                physicalModel.SetProperty(physicalInstance, size, "20,1,1");
                Check("exposed vector bounds reject invalid values without replacing the instance", physicalWorld.Selection.SelectedNode == physicalInstance && physicalModel.Status.Contains("range"));
                physicalModel.SetProperty(physicalInstance, size, "2,3,4");
                physicalInstance = physicalWorld.Selection.SelectedNode!;
                Check("exposed component vectors update the live component", physicalInstance.Children[0].Components.OfType<ManaBoxColliderComponent>().Single().Size == new Vector3(2, 3, 4), physicalModel.Status);
                Check("component overrides remain bound to their persistent ID", physicalInstance.Children[0].Components.OfType<ManaBoxColliderComponent>().Single().AuthoredId == size.ComponentId);
                var inspectorUi = new UiRoot(fonts);
                inspectorUi.Content = ProjectManaView.BuildInspector(inspectorUi.Theme, physicalInstance);
                inspectorUi.UpdateLayout(new Vector2(380, 800));
                Check("typed exposed-property inspector renders", inspectorUi.Render().Instances.Length > 0
                    && Elements(inspectorUi).OfType<VectorBox>().Any());
            }
            finally
            {
                physicalContext.Physics.Simulation.Dispose();
                physicalContext.Physics.ThreadDispatcher.Dispose();
                physicalContext.Physics.BufferPool.Clear();
            }
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(scratch, true);
        }

        var (_, mapContext) = World();
        var oldLoader = ManaGridMapNode.LoadAsset;
        try
        {
            ManaGridMapNode.LoadAsset = (_, w, _) => new ManaSceneNode(w.Context, Matrix4x4.Identity);
            var map = new ManaGridMapNode(mapContext);
            map.Load(Vector3.One, ["a.glb", "b.glb"], new[] { new KeyValuePair<GridMapCell, GridMapTile>(new(1, 0, 2), new(0, 2)) }, false);
            var preset = new ManaGridMapPreset { Settings = new() { Palette = ["b.glb", "a.glb"], CellSize = [2, 1, 2], Collision = false } };
            preset.Apply(map);
            var state = map.CaptureState();
            Check("preset palette reorder remaps painted cells by asset identity", state.Cells.Single().Value.Item == 1 && state.Cells.Single().Value.Orientation == 2);
            Check("preset applies grid settings", map.CellSize == new Vector3(2, 1, 2));
            var captured = ManaGridMapPreset.Capture(map, "Palette");
            Check("grid preset excludes cells and layer content", captured.Settings.Count == 0 && captured.Settings.Data.Length == 0 && captured.Settings.Layers == null);
            new ManaGridMapPreset { Settings = new() { Palette = ["c.glb"], Collision = false } }.Apply(map);
            state = map.CaptureState();
            Check("preset retains painted assets absent from its palette", state.Palette[state.Cells.Single().Value.Item] == "a.glb");
        }
        finally { ManaGridMapNode.LoadAsset = oldLoader; }
    }
}
