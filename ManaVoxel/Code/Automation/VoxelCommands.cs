using Mana.Code.Automation;
using ManaVoxel.Code.Gameplay.Course;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.UI;

namespace ManaVoxel.Code.Automation
{
    /// <summary>
    /// The command-file commands only the voxel editor can do (see <see cref="ManaCommandRunner"/> for the shared
    /// ones): levels, prefabs, panels, selection, grid maps, the bow, play mode.
    /// </summary>
    public static class VoxelCommands
    {
        public static void Register(
            ManaCommandRunner runner,
            Action<string> openLevel,
            Action<bool>? setPlay,
            Action? dropToFloor)
        {
            var context = runner.Context;

            runner.Register("open_level", command => openLevel(ManaCommandRunner.RequirePath(command)));

            runner.Register("transform_node", command =>
            {
                var node = runner.FindNode(command.Name ?? "")
                    ?? throw new InvalidDataException("transform_node: node not found.");
                if (command.Position is { Length: 3 } p) node.Position = new(p[0], p[1], p[2]);
                if (command.Rotation is { Length: 3 } r) node.Rotation = new(r[0], r[1], r[2]);
            });

            runner.Register("gpu_particles", command =>
            {
                var node = runner.FindNode(command.Name ?? "GPU Fountain")
                    ?? throw new InvalidDataException("gpu_particles: emitter node not found.");
                var emitter = node.Components.OfType<Mana.Code.Components.ManaGpuParticleEmitterComponent>().FirstOrDefault()
                    ?? throw new InvalidDataException("gpu_particles: node has no GPU emitter.");
                emitter.Count = Math.Clamp(command.Count, 0, Mana.Code.Components.ManaGpuParticleEmitterComponent.MaximumCount);
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: GPU particles count={emitter.Count}.");
            });

            runner.Register("play", _ => setPlay?.Invoke(true));
            runner.Register("stop_play", _ => setPlay?.Invoke(false));
            runner.Register("drop_to_floor", _ => dropToFloor?.Invoke());

            runner.Register("select_node", command =>
            {
                if (string.IsNullOrWhiteSpace(command.Name))
                {
                    throw new InvalidDataException("Command file: 'select_node' is missing its required 'name'.");
                }

                // Every scene, not just the default one: an opened level loads into a scene of its own.
                var node = runner.FindNode(command.Name);
                if (node == null)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: select_node found no node named '{command.Name}', skipping.");
                    return;
                }

                EditorSelectionSnapshot.SelectNode(context.World.Selection, node, sceneGraphChanged: true);
            });

            runner.Register("open_prefab", command =>
            {
                // Edit Prefab, as the Project panel's menu does it.
                var prefabs = ProjectAssetViewModel.For(context.World);
                prefabs.OpenPrefab(Path.GetFullPath(ManaCommandRunner.RequirePath(command)));
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: open_prefab: {prefabs.Status}");
            });

            runner.Register("open_asset", command =>
            {
                // An asset in its own editor, as the Project panel's Edit does it.
                ProjectAssetViewModel.For(context.World).OpenAsset(Path.GetFullPath(ManaCommandRunner.RequirePath(command)));
            });

            runner.Register("review_changes", command =>
            {
                // Review Changes on the named prefab instance, as its Actions menu opens it.
                if (runner.FindNode(command.Name) is { } reviewed)
                {
                    ProjectAssetViewModel.For(context.World).OpenReview(reviewed);
                }
            });

            runner.Register("bow", command =>
            {
                // Holds the bow's fire key (name "hold", "hold_up" for a high shot) or lets go ("release").
                BowComponent.SetHoldFire(context.World, command.Name is "hold" or "hold_up", command.Name == "hold_up");
            });

            runner.Register("import_filter", command =>
            {
                // Types into the Import panel's filter box, to bring one pack's models into view.
                ManaVoxel.Code.UI.ManaUi.ManaUiDockDemo.MeshAssets.Filter = command.Name ?? string.Empty;
            });

            runner.Register("dirty_report", command =>
            {
                // Each opened level's saved snapshot beside a fresh capture, to diff what makes it read as changed.
                var path = ManaCommandRunner.RequirePath(command);
                foreach (var scene in context.World.Scenes.Where(scene => !string.IsNullOrWhiteSpace(scene.SavedLevelContentJson)))
                {
                    var fresh = ManaVoxel.Code.Levels.ManaLevelCapture.CaptureSceneContentJson(context.World, scene, scene.Name ?? "");
                    File.WriteAllText(path + ".saved.json", scene.SavedLevelContentJson);
                    File.WriteAllText(path + ".now.json", fresh);
                    File.WriteAllLines(path + ".colliders.txt", scene.Children.SelectMany(ManaCommandRunner.Walk)
                        .SelectMany(n => n.Components.OfType<global::Mana.Code.Components.ManaCollisionComponent>().Select(c => $"{n.Name}: {c.GetType().Name} follows={c.FollowsVisualBounds} shape={c.Shape?.GetType().Name}")));
                }
            });

            runner.Register("open_tools", _ =>
            {
                if (ManaVoxel.Code.UI.ManaUi.ViewportToolsDrawer.Current is { } drawer)
                {
                    drawer.IsOpen = true;
                }
            });

            runner.Register("open_panel", command => ManaVoxel.Code.UI.ManaUi.ManaUiDockDemo.RequestPanel(command.Name ?? ""));

            runner.Register("grid_autotile", command => ApplyGridAutoTile(runner, command));
            runner.Register("grid_generate", command => ApplyGridGenerate(runner, command));
        }

        // Paints a road (or any preset set) the way a drag would, cell by cell along the path, so a screenshot
        // can show what the brush does without a hand on the mouse.
        private static void ApplyGridAutoTile(ManaCommandRunner runner, ManaCommand command)
        {
            var map = runner.FindNode(command.Name) as ManaGridMapNode;
            var preset = GridMapAutoTilePresets.All.FirstOrDefault(p => p.Name.Equals(command.Preset ?? "Road", StringComparison.OrdinalIgnoreCase));
            if (map == null || preset == null)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: grid_autotile found no grid map '{command.Name}' or preset '{command.Preset}', skipping.");
                return;
            }

            var set = GridMapAutoTilePresets.AddTo(map, preset);
            GridMapCell? last = null;
            foreach (var point in command.Cells ?? [])
            {
                var cell = new GridMapCell(point[0], 0, point[1]);
                foreach (var step in VoxelEditorGameMode.GridStrokePath(last, cell))
                {
                    map.PaintAutoTile(set, step);
                }

                last = cell;
            }

            GridMapEditing.Begin(map);
            GridMapEditing.AutoTile = set;
        }

        private static void ApplyGridGenerate(ManaCommandRunner runner, ManaCommand command)
        {
            if (runner.FindNode(command.Name) is not ManaGridMapNode map)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: grid_generate found no grid map '{command.Name}', skipping.");
                return;
            }

            var rules = GridMapWfcPresets.Dungeon();
            if (command.BigSize > 0 && rules.BigRooms != null)
            {
                rules.BigRooms.MinSize = rules.BigRooms.MaxSize = command.BigSize;
            }

            GridMapWfcEditing.Replace(rules, null);
            GridMapWfcEditing.WindowOpen = true;
            GridMapWfcEditing.Width = command.Size is { Length: >= 2 } size ? size[0] : 16;
            GridMapWfcEditing.Depth = command.Size is { Length: >= 2 } both ? both[1] : 16;
            GridMapWfcEditing.Seed = command.Seed;
            var result = GridMapWfcGenerator.Generate(map, GridMapWfcEditing.Rules,
                new GridMapWfcGenerator.Area(0, 0, GridMapWfcEditing.Width, GridMapWfcEditing.Depth, 0), command.Seed, keepPainted: true, connectedOnly: true,
                rooms: GridMapWfcEditing.Rooms, bigRooms: GridMapWfcEditing.BigRooms);
            GridMapWfcEditing.Status = result.Succeeded ? $"{result.Placements.Count} tiles" : result.Error ?? "";
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic("Command file: grid_generate big rooms: "
                + string.Join(", ", result.BigRooms.Select(room => $"({room.X},{room.Z}) {room.Width}x{room.Depth}")));
        }
    }
}
