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

#if !MANA_BROWSER
            runner.Register("planet_detail", command =>
            {
                var planet = runner.FindNode(command.Name ?? "Earth") as ManaGeoTools.Code.Planet.PlanetNode
                    ?? throw new InvalidDataException("planet_detail: planet not found.");
                if (command.Position is not { Length: >= 2 } coordinates)
                {
                    throw new InvalidDataException("planet_detail: position needs longitude and latitude.");
                }
                var zoom = command.Count > 0 ? command.Count : 7;
                planet.RequestDetailAt(coordinates[0], coordinates[1], zoom);
                Console.WriteLine($"Command file: planet detail {planet.Name} at {coordinates[0]}, {coordinates[1]}, zoom {zoom}.");
            });
            runner.Register("planet_status", command =>
            {
                var planet = runner.FindNode(command.Name ?? "Earth") as ManaGeoTools.Code.Planet.PlanetNode
                    ?? throw new InvalidDataException("planet_status: planet not found.");
                Console.WriteLine($"Command file: planet {planet.Name}: {planet.GetStreamingStatus()}, snapshots {planet.PendingChangedPatchCount}.");
            });
            runner.Register("planet_merge", command =>
            {
                var planet = runner.FindNode(command.Name ?? "Earth") as ManaGeoTools.Code.Planet.PlanetNode
                    ?? throw new InvalidDataException("planet_merge: planet not found.");
                if (command.Position is not { Length: >= 3 } cell)
                {
                    throw new InvalidDataException("planet_merge: position needs root face, row and column.");
                }
                var key = ManaGeoTools.Code.Planet.PatchKey.Root((int)cell[0], (int)cell[1], (int)cell[2]);
                Console.WriteLine($"Command file: planet merge {planet.Name}: {planet.TryMergeSiblingGroup(key)}.");
            });
#endif

            runner.Register("camera_settings", _ => ManaVoxel.Code.UI.ManaUi.ViewportToolbarManaView.OpenCameraOptions());

            runner.Register("camera_projection", command =>
            {
                var camera = context.World.MainCamera;
                camera.SetProjectionMode(string.Equals(command.Name, "Ortho", StringComparison.OrdinalIgnoreCase));
                if (command.Position is { Length: > 0 } settings)
                {
                    camera.OrthographicHeight = settings[0];
                    camera.UpdateProjection();
                }
                if (Enum.TryParse<global::Mana.CameraViewPreset>(command.Preset, true, out var preset))
                {
                    camera.SetViewPreset(preset);
                }
                Console.WriteLine($"Command file: camera {command.Name} at {camera.Owner.Position}, ortho height {camera.OrthographicHeight}");
            });

            runner.Register("open_level", (command, _) =>
            {
                openLevel(ManaCommandRunner.RequirePath(command));
                // Let the host hold the next step while asynchronous downloads/imports finish.
                return ManaCommandFlow.Pause;
            });

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
                    // A scene of that name instead: what brings up its Inspector (level settings, game modes).
                    var scene = context.World.Scenes.FirstOrDefault(s => string.Equals(s.Name, command.Name, StringComparison.OrdinalIgnoreCase));
                    if (scene != null)
                    {
                        context.World.Selection.SelectedNode = null;
                        context.World.Selection.SelectedComponent = null;
                        context.World.Selection.SelectedScene = scene;
                        return;
                    }

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

            runner.Register("designer_select", command =>
            {
                // An element of the layout open in the UI Designer, as a click on it does.
                var element = ManaVoxel.Code.UI.ManaUi.UiDesignerDocument.FindNamed(command.Name ?? string.Empty);
                ManaVoxel.Code.UI.ManaUi.UiDesignerDocument.Select(element);
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: designer_select {command.Name}: {element != null}");
            });

            runner.Register("designer_pick", command =>
            {
                // The path picker for the selected element, opened under the preview.
                var kind = Enum.Parse<ManaVoxel.Code.UI.ManaUi.UiDesignerPathKind>(command.Name ?? "Property", ignoreCase: true);
                IEnumerable<ManaUI.UiElement> Walk(ManaUI.UiElement element)
                {
                    yield return element;
                    foreach (var inner in element.VisualChildren.SelectMany(Walk))
                    {
                        yield return inner;
                    }
                }

                if (context.GameMode?.ManaUi?.Root is { } uiRoot && Walk(uiRoot).OfType<ManaVoxel.Code.UI.ManaUi.UiDesignerPreview>().FirstOrDefault() is { } preview)
                {
                    ManaVoxel.Code.UI.ManaUi.UiDesignerManaView.OpenPickerForSelected(preview, kind);
                }
            });

            runner.Register("designer_bind", command =>
            {
                // Binds a property of the selected element to `path`; with no path, its bind button's picker opens.
                IEnumerable<ManaUI.UiElement> Walk(ManaUI.UiElement element)
                {
                    yield return element;
                    foreach (var inner in element.VisualChildren.SelectMany(Walk))
                    {
                        yield return inner;
                    }
                }

                var key = command.Name ?? string.Empty;
                if (context.GameMode?.ManaUi?.Root is { } uiRoot && Walk(uiRoot).FirstOrDefault(element => element.Name == "bind:" + key) is { } button)
                {
                    ManaVoxel.Code.UI.ManaUi.UiDesignerManaView.BindSelected(button, key, command.Path);
                }

                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: designer_bind {key} {command.Path}");
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
                ManaVoxel.Code.Assets.EditorAssetServices.For(context.World).Library.Filter = command.Name ?? string.Empty;
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

            runner.Register("open_tools", _ => ManaVoxel.Code.UI.ManaUi.ViewportToolbarManaView.OpenSnapOptions());

            runner.Register("open_panel", command => ManaVoxel.Code.UI.ManaUi.EditorWorkspace.RequestPanel(context.World, command.Name ?? ""));

            runner.Register("desktop_panel", command =>
            {
                var controller = ManaVoxel.Code.UI.ManaUi.EditorWorkspace.For(context.GameMode?.ManaUi?.Root
                    ?? throw new InvalidOperationException("desktop_panel: editor UI is unavailable.")).DragController
                    ?? throw new InvalidOperationException("desktop_panel: editor docking is unavailable.");
                var root = context.GameMode?.ManaUi?.Root
                    ?? throw new InvalidOperationException("desktop_panel: editor UI is unavailable.");
                IEnumerable<ManaUI.UiElement> WalkUi(ManaUI.UiElement element)
                {
                    yield return element;
                    foreach (var child in element.VisualChildren)
                    {
                        foreach (var nested in WalkUi(child))
                        {
                            yield return nested;
                        }
                    }
                }
                var id = command.Name ?? "showcase";
                var desktop = controller.FindDesktop(id);
                if (desktop == null)
                {
                    var item = WalkUi(root).OfType<ManaUI.DockHost>().SelectMany(host => host.AllItems())
                        .FirstOrDefault(item => item.Id == id)
                        ?? throw new InvalidDataException($"desktop_panel: panel '{id}' is not open.");
                    desktop = controller.PopOut(item)
                        ?? throw new InvalidOperationException("desktop_panel: native windows are unavailable.");
                }
                if (command.Position is { Length: >= 2 } size)
                {
                    if (command.Preset == "drag")
                    {
                        var panelRoot = desktop.Root;
                        panelRoot.UpdateLayout(desktop.Size / panelRoot.ScreenScale);
                        var start = desktop.Frame.Bounds.Size - new Vector2(2);
                        panelRoot.Input.InjectPointerMoved(start);
                        panelRoot.Input.InjectPointerPressed(start, ManaUI.PointerButton.Left, 0);
                        var end = start + new Vector2(size[0], size[1]) - desktop.Size;
                        for (var step = 1; step <= 20; step++)
                        {
                            panelRoot.Input.InjectPointerMoved(Vector2.Lerp(start, end, step / 20f));
                        }
                        panelRoot.Input.InjectPointerReleased(end, ManaUI.PointerButton.Left);
                    }
                    else
                    {
                        desktop.Size = new Vector2(size[0], size[1]);
                    }
                }
                // Moved there as a drop would, unchecked -- to test that a window put out of reach comes back.
                if (command.Target is { Length: >= 2 } target)
                {
                    desktop.MoveTo(new Vector2(target[0], target[1]));
                }
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: panel '{id}' window at {desktop.Position}, {desktop.Size}");
            });

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
