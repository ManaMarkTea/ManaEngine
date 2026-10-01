using Mana.Code.GameModes;
using Mana.Code.Nodes;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.UI;

namespace ManaVoxel.Code.Automation
{
    /// <summary>
    /// Drives the editor from a <c>--command-file</c> JSON script instead of interactive input: move the
    /// camera, open a level, save a screenshot, quit. Built so both of us can verify a visual change (grid,
    /// theme, camera default) without a human at a real desktop -- see the command file's own commands for
    /// the full list.
    /// <para>
    /// One command is "in flight" at a time; <see cref="Tick"/> is meant to be called once per rendered,
    /// past-loading frame (mirroring <c>VoxelEditorGameMode.OpenStartupLevelIfRequested</c>'s own
    /// first-frame-past-loading guarantee).
    /// </para>
    /// </summary>
    public sealed class ManaCommandRunner
    {
        private readonly ManaContext _context;
        private readonly ManaWebGpuContext? _webGpuContext;
        private readonly Action<string> _openLevel;
        private readonly Action<bool>? _setPlay;
        private readonly System.Diagnostics.Stopwatch _profileClock = new();
        private readonly List<ManaCommand> _commands;

        private int _index;
        private int _waitFramesRemaining;
        private Task? _pendingScreenshot;

        // Glides under way (move_camera or sky with frames): each steps once a frame with an eased 0..1, and the
        // script holds until the blocking ones finish; background ones run on alongside what follows.
        private readonly List<(Action<float> Apply, int Frames, bool Blocking)> _glides = new();
        private readonly List<int> _glideSteps = new();

        /// <summary>The editor's End key, for the command file's drop_to_floor.</summary>
        public Action? DropToFloor { get; init; }

        public ManaCommandRunner(
            string commandFilePath,
            ManaContext context,
            ManaWebGpuContext? webGpuContext,
            Action<string> openLevel,
            Action<bool>? setPlay = null)
        {
            _setPlay = setPlay;
            _context = context;
            _webGpuContext = webGpuContext;
            _openLevel = openLevel;
            _commands = ManaCommandFile.Load(commandFilePath);
        }

        public void Tick()
        {
            if (StepGlides())
            {
                return;
            }

            if (_waitFramesRemaining > 0)
            {
                _waitFramesRemaining--;
                return;
            }

            if (_pendingScreenshot != null)
            {
                if (!_pendingScreenshot.IsCompleted)
                {
                    return;
                }

                if (_pendingScreenshot.Exception != null)
                {
                    var error = _pendingScreenshot.Exception.InnerException?.Message
                        ?? _pendingScreenshot.Exception.Message;
                    ManaConsole.WriteDiagnostic($"Command file: screenshot failed: {error}");
                }

                _pendingScreenshot = null;
                Mana.Code.UI.ManaEditorGizmos.FullWindowView = false;
                _index++;
            }

            while (_index < _commands.Count)
            {
                var command = _commands[_index];
                switch (command.Type.ToLowerInvariant())
                {
                    case "open_level":
                        _openLevel(RequirePath(command));
                        _index++;
                        break;

                    case "wait":
                        _waitFramesRemaining = Math.Max(0, command.Frames);
                        _index++;
                        return;

                    case "move_camera":
                        _index++;
                        if (command.Frames > 0 && command.Position is { Length: 3 } && command.Target is { Length: 3 })
                        {
                            StartCameraGlide(command);
                            if (!command.Background)
                            {
                                return;
                            }
                            break;
                        }
                        ApplyMoveCamera(command);
                        break;

                    case "select_node":
                        ApplySelectNode(command);
                        _index++;
                        break;

                    case "grid_autotile":
                        ApplyGridAutoTile(command);
                        _index++;
                        break;

                    case "open_prefab":
                        // Edit Prefab, as the Project panel's menu does it.
                        var prefabs = ManaVoxel.Code.UI.ProjectAssetViewModel.For(_context.World);
                        prefabs.OpenPrefab(Path.GetFullPath(RequirePath(command)));
                        ManaConsole.WriteDiagnostic($"Command file: open_prefab: {prefabs.Status}");
                        _index++;
                        break;

                    case "teleport_player":
                        // Straight to a spot while playing, to look at a stretch of a level without walking there.
                        if (command.Position is { Length: 3 } spot && _context.World.GameState.Player is { } player)
                        {
                            player.TeleportTo(new Vector3(spot[0], spot[1], spot[2]));
                        }
                        _index++;
                        break;

                    case "log_player":
                        // Where the player is and whether it stands, for a scripted check of a level's footing.
                        if (_context.World.GameState.Player is { } logged)
                        {
                            ManaConsole.WriteDiagnostic($"Command file: player {command.Name} at {logged.Body?.FootPosition ?? logged.Position}, grounded {logged.IsGrounded}");
                        }
                        _index++;
                        break;

                    case "open_asset":
                        // An asset in its own editor, as the Project panel's Edit does it.
                        ManaVoxel.Code.UI.ProjectAssetViewModel.For(_context.World).OpenAsset(Path.GetFullPath(RequirePath(command)));
                        _index++;
                        break;

                    case "sky":
                        // The sky mode by name; a time of day holds the cycle still there, for a screenshot.
                        if (Enum.TryParse<ManaCore.Graphics.ManaSkyMode>(command.Name, true, out var skyMode))
                        {
                            _context.World.IrradianceSettings.Sky = skyMode;
                        }
                        if (command.TimeOfDay is { } timeOfDay)
                        {
                            var sky = _context.World.IrradianceSettings;
                            sky.TimeSpeed = 0.0f;
                            sky.RealTime = false;
                            if (command.Frames > 0)
                            {
                                // Always forward, as the day goes -- sunset to night, or on through dawn.
                                var from = sky.TimeOfDay;
                                var ahead = ((timeOfDay - from) % 1.0f + 1.0f) % 1.0f;
                                _glides.Add((eased => sky.TimeOfDay = (from + ahead * eased) % 1.0f, command.Frames, !command.Background));
                                _glideSteps.Add(0);
                                _index++;
                                if (!command.Background)
                                {
                                    return;
                                }
                                break;
                            }
                            sky.TimeOfDay = timeOfDay;
                        }
                        _index++;
                        break;

                    case "review_changes":
                        // Review Changes on the named prefab instance, as its Actions menu opens it.
                        if (_context.World.Scenes.Select(scene => FindNode(scene.Children, command.Name ?? "")).FirstOrDefault(found => found != null) is { } reviewed)
                        {
                            ManaVoxel.Code.UI.ProjectAssetViewModel.For(_context.World).OpenReview(reviewed);
                        }
                        _index++;
                        break;

                    case "emission":
                        // Emission on every mesh under the named node: colour from position, strength from frames/100.
                        if (_context.World.Scenes.Select(scene => FindNode(scene.Children, command.Name ?? "")).FirstOrDefault(found => found != null) is { } glowing
                            && command.Position is { Length: 3 } glow)
                        {
                            foreach (var renderer in ManaVoxel.Code.Assets.ManaPrefabRuntime.Walk(glowing).SelectMany(n => n.Components).OfType<global::Mana.MeshRendererComponent>())
                            {
                                renderer.Material.Emission.Value = new Vector4(glow[0], glow[1], glow[2], 1.0f);
                                renderer.Material.EmissionStrength = command.Frames / 100.0;
                            }
                        }
                        _index++;
                        break;

                    case "dump_environment":
                        // The reflection cube's six faces as <path>.face0..5.png, after the next frame.
                        _webGpuContext?.DumpEnvironmentFaces(RequirePath(command));
                        _index++;
                        break;

                    case "material":
                        // Metallic and roughness (position x, y) on every mesh under the named node -- a mirror is [1, 0].
                        if (_context.World.Scenes.Select(scene => FindNode(scene.Children, command.Name ?? "")).FirstOrDefault(found => found != null) is { } surfaced
                            && command.Position is { Length: >= 2 } surface)
                        {
                            foreach (var renderer in ManaVoxel.Code.Assets.ManaPrefabRuntime.Walk(surfaced).SelectMany(n => n.Components).OfType<global::Mana.MeshRendererComponent>())
                            {
                                renderer.Material.Metallic.Value = surface[0];
                                renderer.Material.Roughness.Value = surface[1];
                            }
                        }
                        _index++;
                        break;

                    case "dirty_report":
                        // Each opened level's saved snapshot beside a fresh capture, to diff what makes it read as changed.
                        foreach (var scene in _context.World.Scenes.Where(scene => !string.IsNullOrWhiteSpace(scene.SavedLevelContentJson)))
                        {
                            var fresh = ManaVoxel.Code.Levels.ManaLevelCapture.CaptureSceneContentJson(_context.World, scene, scene.Name ?? "");
                            File.WriteAllText(RequirePath(command) + ".saved.json", scene.SavedLevelContentJson);
                            File.WriteAllText(RequirePath(command) + ".now.json", fresh);
                            File.WriteAllLines(RequirePath(command) + ".colliders.txt", scene.Children.SelectMany(ManaVoxel.Code.Assets.ManaPrefabRuntime.Walk)
                                .SelectMany(n => n.Components.OfType<global::Mana.Code.Components.ManaCollisionComponent>().Select(c => $"{n.Name}: {c.GetType().Name} follows={c.FollowsVisualBounds} shape={c.Shape?.GetType().Name}")));
                        }
                        _index++;
                        break;

                    case "drop_to_floor":
                        DropToFloor?.Invoke();
                        _index++;
                        break;

                    case "open_tools":
                        if (ManaVoxel.Code.UI.ManaUi.ViewportToolsDrawer.Current is { } drawer)
                        {
                            drawer.IsOpen = true;
                        }
                        _index++;
                        break;

                    case "open_panel":
                        ManaVoxel.Code.UI.ManaUi.ManaUiDockDemo.RequestPanel(command.Name ?? "");
                        _index++;
                        break;

                    case "grid_generate":
                        ApplyGridGenerate(command);
                        _index++;
                        break;

                    case "screenshot":
                        if (_webGpuContext == null)
                        {
                            ManaConsole.WriteDiagnostic(
                                "Command file: screenshot skipped, no WebGPU context (only the WebGPU backend supports it).");
                            _index++;
                            break;
                        }

                        // A clean shot fills the window, so it is centred on the window, not the viewport the panels
                        // leave: switched over, then a frame for the camera to re-project before the capture.
                        if (command.Clean && !Mana.Code.UI.ManaEditorGizmos.FullWindowView)
                        {
                            Mana.Code.UI.ManaEditorGizmos.FullWindowView = true;
                            return;
                        }
                        _pendingScreenshot = _webGpuContext.CaptureFrameAsync(RequirePath(command), command.Clean);
                        return;

                    case "gizmos":
                        Mana.Code.UI.ManaEditorGizmos.Visible = command.Visible ?? !Mana.Code.UI.ManaEditorGizmos.Visible;
                        _index++;
                        break;

                    case "play":
                    case "stop_play":
                        _setPlay?.Invoke(command.Type.Equals("play", StringComparison.OrdinalIgnoreCase));
                        _index++;
                        break;

                    case "profile_reset":
                        ManaCore.Diagnostics.ManaFrameProfiler.Reset();
                        _profileClock.Restart();
                        _index++;
                        break;

                    case "profile_dump":
                        WriteProfileReport(RequirePath(command));
                        _index++;
                        break;

                    case "quit":
                        _context.Window.Close();
                        _index++;
                        return;

                    default:
                        ManaConsole.WriteDiagnostic($"Command file: unknown command type '{command.Type}', skipping.");
                        _index++;
                        break;
                }
            }
        }

        // Frames per second since the last profile_reset, then every timed scope by average cost -- what makes
        // "the editor is slow" a list of names instead of a feeling.
        private void WriteProfileReport(string path)
        {
            var seconds = _profileClock.Elapsed.TotalSeconds;
            var frames = ManaCore.Diagnostics.ManaFrameProfiler.FrameCount;
            var lines = new List<string>
            {
                $"seconds={seconds:0.00} frames={frames} fps={(seconds > 0.0 ? frames / seconds : 0.0):0.0} worstFrameMs={ManaCore.Diagnostics.ManaFrameProfiler.WorstFrameMs:0.0} hitches={ManaCore.Diagnostics.ManaFrameProfiler.HitchCount}",
                $"nodes={CountNodes(_context.World.DefaultScene.Children)} meshes={_context.World.LastMeshCount} playing={_context.World.IsPlaying}",
                "scope | avgMs | peakMs | calls"
            };

            var entries = new List<(string Name, ManaCore.Diagnostics.ManaFrameProfiler.Sample Sample)>();
            foreach (var name in ManaCore.Diagnostics.ManaFrameProfiler.Names)
            {
                if (ManaCore.Diagnostics.ManaFrameProfiler.TryGetSample(name, out var sample))
                {
                    entries.Add((name, sample));
                }
            }

            foreach (var (name, sample) in entries.OrderByDescending(entry => entry.Sample.AverageMs))
            {
                lines.Add($"{name} | {sample.AverageMs:0.00} | {sample.PeakMs:0.00} | {sample.LastCalls}");
            }

            File.WriteAllLines(path, lines);
        }

        private static int CountNodes(IEnumerable<ManaSceneNode> nodes)
        {
            var count = 0;
            foreach (var node in nodes)
            {
                count += 1 + CountNodes(node.Children);
            }

            return count;
        }

        private static string RequirePath(ManaCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Path))
            {
                throw new InvalidDataException($"Command file: '{command.Type}' is missing its required 'path'.");
            }

            return command.Path;
        }

        // Paints a road (or any preset set) the way a drag would, cell by cell along the path, so a screenshot
        // can show what the brush does without a hand on the mouse.
        private void ApplyGridAutoTile(ManaCommand command)
        {
            var map = _context.World.Scenes.Select(scene => FindNode(scene.Children, command.Name ?? "")).FirstOrDefault(found => found != null) as ManaGridMapNode;
            var preset = GridMapAutoTilePresets.All.FirstOrDefault(p => p.Name.Equals(command.Preset ?? "Road", StringComparison.OrdinalIgnoreCase));
            if (map == null || preset == null)
            {
                ManaConsole.WriteDiagnostic($"Command file: grid_autotile found no grid map '{command.Name}' or preset '{command.Preset}', skipping.");
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

        private void ApplyGridGenerate(ManaCommand command)
        {
            if (_context.World.Scenes.Select(scene => FindNode(scene.Children, command.Name ?? "")).FirstOrDefault(found => found != null) is not ManaGridMapNode map)
            {
                ManaConsole.WriteDiagnostic($"Command file: grid_generate found no grid map '{command.Name}', skipping.");
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
            ManaConsole.WriteDiagnostic("Command file: grid_generate big rooms: "
                + string.Join(", ", result.BigRooms.Select(room => $"({room.X},{room.Z}) {room.Width}x{room.Depth}")));
        }

        private void ApplySelectNode(ManaCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidDataException("Command file: 'select_node' is missing its required 'name'.");
            }

            var world = _context.World;
            // Every scene, not just the default one: an opened level loads into a scene of its own.
            var node = world.Scenes.Select(scene => FindNode(scene.Children, command.Name)).FirstOrDefault(found => found != null);
            if (node == null)
            {
                ManaConsole.WriteDiagnostic($"Command file: select_node found no node named '{command.Name}', skipping.");
                return;
            }

            EditorSelectionSnapshot.SelectNode(world.Selection, node, sceneGraphChanged: true);
        }

        private static ManaSceneNode? FindNode(IEnumerable<ManaSceneNode> nodes, string name)
        {
            // "Red Lamp/Bulb": the Bulb under Red Lamp, where every lamp has one.
            var slash = name.IndexOf('/');
            if (slash > 0)
            {
                return FindNode(nodes, name[..slash]) is { } parent ? FindNode(parent.Children, name[(slash + 1)..]) : null;
            }

            foreach (var node in nodes)
            {
                if (node.Name == name)
                {
                    return node;
                }

                if (FindNode(node.Children, name) is { } child)
                {
                    return child;
                }
            }

            return null;
        }

        private void StartCameraGlide(ManaCommand command)
        {
            var owner = _context.World.MainCamera.Owner;
            var toPosition = ToVector3(command.Position!);
            var toTarget = ToVector3(command.Target!);
            // The start's look point sits as far ahead as the end's, so the aim swings evenly rather than lurching.
            var reach = MathF.Max(Vector3.Distance(toPosition, toTarget), 0.01f);
            var fromPosition = owner.Position;
            var fromTarget = owner.Position + owner.Forward * reach;
            _glides.Add((eased =>
            {
                owner.Position = Vector3.Lerp(fromPosition, toPosition, eased);
                LookAt(owner, Vector3.Lerp(fromTarget, toTarget, eased));
            }, command.Frames, !command.Background));
            _glideSteps.Add(0);
        }

        /// <summary>Moves every glide on a frame; true while a blocking one is still going.</summary>
        private bool StepGlides()
        {
            var blocking = false;
            for (var i = _glides.Count - 1; i >= 0; i--)
            {
                var (apply, frames, blocks) = _glides[i];
                var step = ++_glideSteps[i];
                var t = Math.Clamp((float)step / frames, 0.0f, 1.0f);
                apply(t * t * (3.0f - 2.0f * t));
                if (step >= frames)
                {
                    _glides.RemoveAt(i);
                    _glideSteps.RemoveAt(i);
                }
                else
                {
                    blocking |= blocks;
                }
            }
            return blocking;
        }

        private static Vector3 ToVector3(float[] values) => new(values[0], values[1], values[2]);

        private void ApplyMoveCamera(ManaCommand command)
        {
            var owner = _context.World.MainCamera.Owner;

            if (command.Position is { Length: 3 } position)
            {
                owner.Position = new Vector3(position[0], position[1], position[2]);
            }

            if (command.Target is { Length: 3 } target)
            {
                LookAt(owner, ToVector3(target));
            }
        }

        private static void LookAt(ManaSceneNode owner, Vector3 target)
        {
            var direction = target - owner.Position;
            if (direction.LengthSquared() < 1e-8f)
            {
                return;
            }

            var forward = Vector3.Normalize(direction);

            // Inverse of ManaSceneNode.Forward's own mapping (CreateFromYawPitchRoll(yaw=Rotation.Y,
            // pitch=Rotation.X, roll=0) applied to local -Z): forward = (-cos(pitch)*sin(yaw),
            // sin(pitch), -cos(pitch)*cos(yaw)).
            var pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
            var yaw = MathF.Atan2(-forward.X, -forward.Z);
            owner.Rotation = new Vector3(pitch, yaw, 0.0f);
        }
    }
}
