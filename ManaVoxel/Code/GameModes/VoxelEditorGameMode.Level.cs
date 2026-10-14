using Mana.Code.Nodes;
using ManaCore.Graphics;
using Mana.Code.Automation;
using ManaVoxel.Code.Automation;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI;
using Silk.NET.Input;
using System.Diagnostics;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// Levels and Play. Kept apart from the editor's own 2,800 lines because none of it is voxel editing:
    /// this half is about what gets written to disk and what happens when you press F5.
    /// </summary>
    public partial class VoxelEditorGameMode : IManaLevelHost
    {
        private bool _playKeyWasDown;
        private bool _ejectKeyWasDown;
        private bool _stopKeyWasDown;
        private bool _restartKeyWasDown;
        private bool _undoKeyWasDown;
        private bool _redoKeyWasDown;
        private bool _saveKeyWasDown;
        private bool _deleteKeyWasDown;

        /// <summary>Remembered so a second window launches on the backend this one is running.</summary>
        private string? _requestedWebGpuBackend;

        /// <summary>Set by <c>--play</c>, so the editor opens straight into the level it was handed.</summary>
        public string? StartupLevelPath { get; set; }

        /// <summary>Set by <c>--command-file</c>, so the editor is driven by a script instead of input.</summary>
        public string? CommandFilePath { get; set; }

        private ManaCommandRunner? _commandRunner;

        // Ejected to inspect a frozen match counts as editing: the panels, picking and the camera are the editor's.
        public bool IsPlaying => _playMode is { IsPlaying: true, IsEjected: false };

        /// <summary>A match is running, though perhaps ejected from for inspection.</summary>
        public bool IsMatchRunning => _playMode is { IsPlaying: true };

        public bool IsEjected => _playMode is { IsEjected: true };

        public void ToggleEject() => _playMode?.ToggleEject();

        private void InitializePlayMode()
        {
            if (_pawnController == null)
            {
                return;
            }

            _playMode = new PlayModeController(
                Context,
                _pawnController,
                (position, yaw) => _viewModel.SpawnCharacterAt(position, yaw, ManaPlayerStartNode.Find(Context.World)),
                pawn => _viewModel.RemoveCharacter(pawn))
            {
                Modes = _gameModes,
                CreateSession = exit => new ManaPlaySession(Context, () => UiWantsMouse, () => UiWantsKeyboard, exit, standalone: false)
                {
                    Commands = _commandRunner
                }
            };

            _viewModel.Levels.Host = this;

            // Selection is editor state and means nothing while playing; left set, the outline pass keeps
            // drawing a yellow cage around whatever was clicked last before Play.
            _playMode.PlayStateChanged += playing =>
            {
                if (playing)
                {
                    Context.World.Selection.SelectedScene = null;
                    Context.World.Selection.SelectedNode = null;
                    Context.World.Selection.SelectedComponent = null;
                }
            };
        }

        // ---- Shortcuts --------------------------------------------------------------------------------

        /// <summary>
        /// F5 plays and stops, Shift+F5 plays from where the camera is looking, Ctrl+F5 launches a second
        /// window, and Escape stops. Escape is claimed from the pawn controller for the duration -- see
        /// <see cref="ManaPawnController.ReleaseOnEscape"/>.
        /// </summary>
        private void UpdatePlayModeShortcuts()
        {
            if (_playMode == null || UiWantsKeyboard)
            {
                _playKeyWasDown = false;
                _stopKeyWasDown = false;
                _restartKeyWasDown = false;
                return;
            }

            var keyboard = Context.Input?.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var playKeyDown = keyboard.IsKeyPressed(Key.F5);
            if (playKeyDown && !_playKeyWasDown)
            {
                var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
                var control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

                if (control)
                {
                    PlayInNewWindow();
                }
                else if (shift && !_playMode.IsPlaying)
                {
                    PlayFromCamera();
                }
                else
                {
                    TogglePlay();
                }
            }

            _playKeyWasDown = playKeyDown;

            var ejectKeyDown = keyboard.IsKeyPressed(Key.F8);
            if (ejectKeyDown && !_ejectKeyWasDown && _playMode.IsPlaying)
            {
                _playMode.ToggleEject();
            }

            _ejectKeyWasDown = ejectKeyDown;

            var restartKeyDown = keyboard.IsKeyPressed(Key.R);
            // A level game mode that takes Escape and R (pause menus, its own replay) gets them instead.
            var modeKeys = _gameModes.HandlesExitKeys;
            if (restartKeyDown && !_restartKeyWasDown && IsPlaying && !modeKeys)
            {
                _playMode.Restart();
            }

            _restartKeyWasDown = restartKeyDown;

            var stopKeyDown = keyboard.IsKeyPressed(Key.Escape);
            // Ejected (F8) counts: IsPlaying is false then, and Escape left the match running under the editor.
            if (stopKeyDown && !_stopKeyWasDown && (IsPlaying || _playMode.IsEjected) && !modeKeys)
            {
                _playMode.Stop();
            }

            _stopKeyWasDown = stopKeyDown;
        }

        /// <summary>
        /// Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z, and Ctrl+S.
        /// <para>
        /// Save writes to whatever name the toolbar's level box holds, exactly as the Save button does --
        /// this is the same command, not a second path to disk. It was the one everyday editor action with
        /// no key at all, which meant the only way to not lose work was to go and find a button.
        /// </para>
        /// </summary>
        private void UpdateEditShortcuts()
        {
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                _undoKeyWasDown = false;
                _redoKeyWasDown = false;
                _saveKeyWasDown = false;
                _deleteKeyWasDown = false;
                return;
            }

            var control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
            var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
            var z = keyboard.IsKeyPressed(Key.Z);
            var y = keyboard.IsKeyPressed(Key.Y);
            var undoKeyDown = control && z && !shift;
            var redoKeyDown = control && (y || (shift && z));
            var canEdit = !UiWantsKeyboard && _playMode is not { IsPlaying: true };

            if (canEdit && undoKeyDown && !_undoKeyWasDown)
            {
                _viewModel.History.Undo();
            }

            if (canEdit && redoKeyDown && !_redoKeyWasDown)
            {
                _viewModel.History.Redo();
            }

            var saveKeyDown = control && keyboard.IsKeyPressed(Key.S);
            if (canEdit && saveKeyDown && !_saveKeyWasDown && !IsLoading)
            {
                _viewModel.Levels.Save();
            }

            _undoKeyWasDown = undoKeyDown;
            _redoKeyWasDown = redoKeyDown;
            _saveKeyWasDown = saveKeyDown;

            // Same "Delete Node" command the outliner's context menu and the inspector's delete button
            // already use -- see SceneEditorOperations.DeleteNode.
            var deleteKeyDown = keyboard.IsKeyPressed(Key.Delete);
            if (canEdit && deleteKeyDown && !_deleteKeyWasDown)
            {
                var selection = Context.World.Selection;
                Mana.Code.UI.SceneEditorOperations.DeleteNode(Context.World, selection, selection.SelectedNode);
            }

            _deleteKeyWasDown = deleteKeyDown;
        }

        /// <summary>What the play HUD reads out, so the controls are on screen rather than in a menu.</summary>
        private string GetPlayHudText()
        {
            var player = _playMode?.Player;
            if (player == null)
            {
                return "Debug camera  --  WASD and Q/E fly, hold right mouse to look, Esc stop  (add a Character game mode to the level to play as one)";
            }

            return $"{player.Name}  --  WASD move, Shift run, Space jump (again in the air to double jump), C camera, R restart, Esc stop";
        }

        // ---- IManaLevelHost ---------------------------------------------------------------------------

        bool IManaLevelHost.IsPlaying => IsPlaying;

        private ProjectAssetViewModel ProjectAssets => ProjectAssetViewModel.For(Context.World, _viewModel);

        public bool CanPlay => !IsLoading && !_openingLevel && _playMode != null && !ProjectAssets.IsEditing;

        private bool PrefabEditingBlocksLevelAction()
        {
            if (!ProjectAssets.IsEditing)
            {
                return false;
            }
            _viewModel.Levels.SetStatus("Save and close, or discard the prefab edit in the Project panel first.");
            return true;
        }

        public bool HasPlayerStart => ManaPlayerStartNode.Find(Context.World) != null;

        public void NewLevel()
        {
            if (PrefabEditingBlocksLevelAction()) { return; }
            if (IsLoading || _openingLevel)
            {
                return;
            }

            StopPlayBeforeEditing();
            CancelMeshPickerPlacementPreview();
            _viewModel.History.Clear();
            ResetLoadedPlanet();
            _water = null;
            ManaLevelLoader.RemoveAuthoredNodes(Context);
            RemoveOpenedLevelScenes();
            _viewModel.AttachScene(Context, Context.World.DefaultScene);
            _viewModel.ResetVoxelLayersAfterLevelChange();
            _viewModel.Levels.MarkNew();
            _viewModel.Levels.SetStatus("New level.");
        }

        private bool _openingLevel;

        public async void OpenLevel(string path)
        {
            if (PrefabEditingBlocksLevelAction()) { return; }
            if (IsLoading || _openingLevel)
            {
                return;
            }

            StopPlayBeforeEditing();

            try
            {
                _openingLevel = true;
                CancelMeshPickerPlacementPreview();
                var level = ManaLevel.Load(path);
                _viewModel.Levels.SetStatus("Preparing level assets…");
                await ManaLevelLoader.EnsureAssetsAsync(level);
                CancelMeshPickerPlacementPreview();
                _viewModel.History.Clear();
                ResetLoadedPlanet();
                _water = null;
                ManaLevelLoader.RemoveAuthoredNodes(Context);
                RemoveOpenedLevelScenes();
                _viewModel.ResetVoxelLayersAfterLevelChange();

                var levelScene = new ManaScene(Context)
                {
                    Name = string.IsNullOrWhiteSpace(level.Name) ? ManaLevelLibrary.DisplayName(path) : level.Name,
                    LevelPath = path
                };

                lock (Context.World.SceneLock)
                {
                    Context.World.Scenes.Add(levelScene);
                }

                var result = ManaLevelLoader.Load(level, Context, levelScene, replaceAuthoredNodes: false);
                MarkSceneSaved(levelScene, path);
                _viewModel.AttachScene(Context, levelScene);
                if (result.VoxelsNode != null)
                {
                    _viewModel.AttachVoxelData(result.VoxelsNode.VoxelShape, result.VoxelsNode);
                }

#if !MANA_BROWSER
                AdoptLoadedPlanet(result.Planet);
#endif
                _water = result.Water;

                // Excluded from picking for the same reason the grid planes are: clicking the marker should
                // place a voxel on the ground it stands on, not on the marker.
                if (result.PlayerStart != null)
                {
                    _pickingExclusions.Add(result.PlayerStart);
                }

                _viewModel.Levels.MarkOpened(path);
                _viewModel.Levels.SetStatus(result.Warnings.Count == 0
                    ? $"Opened {ManaLevelLibrary.DisplayName(path)} ({result.Nodes.Count} objects)."
                    : $"Opened {ManaLevelLibrary.DisplayName(path)} with {result.Warnings.Count} problem(s): {result.Warnings[0]}");

                ManaConsole.WriteDiagnostic(
                    $"Opened level '{path}': {result.Nodes.Count} node(s), {result.Warnings.Count} warning(s).");

                foreach (var warning in result.Warnings)
                {
                    ManaConsole.WriteDiagnostic($"Level load warning: {warning}");
                }
            }
            catch (Exception exception)
            {
                _viewModel.Levels.SetStatus($"Could not open '{Path.GetFileName(path)}': {exception.Message}");
                Trace.TraceWarning($"Failed to open level '{path}': {exception}");
            }
            finally { _openingLevel = false; }
        }

        public void SaveLevel(string path)
        {
            if (ProjectAssets.IsEditing)
            {
                ProjectAssets.SavePrefab();
                _viewModel.Levels.SetStatus(ProjectAssets.Status);
                return;
            }
            if (IsLoading)
            {
                return;
            }

            // Saving while ejected from a match would capture the match's world: end the match first.
            StopPlayBeforeEditing();

            try
            {
                var currentScene = CurrentLevelScene();
                var levelName = currentScene != null && !string.IsNullOrWhiteSpace(currentScene.Name)
                    ? currentScene.Name
                    : ManaLevelLibrary.DisplayName(path);
                var level = ManaLevelCapture.Capture(
                    Context.World,
                    levelName,
                    _playMode?.EditorCameraPose);
                level.Save(path);
                if (currentScene != null)
                {
                    MarkSceneSaved(currentScene, path);
                }

                _viewModel.Levels.MarkSaved(path);
                _viewModel.Levels.SetStatus($"Saved {level.Nodes.Count} object(s) to {Path.GetFileName(path)}.");
            }
            catch (Exception exception)
            {
                _viewModel.Levels.SetStatus($"Could not save '{Path.GetFileName(path)}': {exception.Message}");
                Trace.TraceWarning($"Failed to save level '{path}': {exception}");
            }
        }

        private void ResetLoadedPlanet() => _planetScenery.Reset();

        private void AdoptLoadedPlanet(ManaGeoTools.Code.Planet.PlanetNode? planet) => _planetScenery.Adopt(planet);

        public void SaveScene(ManaScene scene)
        {
            if (ProjectAssets.IsEditing)
            {
                ProjectAssets.SavePrefab();
                return;
            }
            if (IsLoading)
            {
                return;
            }

            var path = scene.LevelPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = ManaLevelLibrary.ResolvePath(string.IsNullOrWhiteSpace(scene.Name) ? "Scene" : scene.Name);
            }

            try
            {
                var levelName = string.IsNullOrWhiteSpace(scene.Name)
                    ? ManaLevelLibrary.DisplayName(path)
                    : scene.Name;
                var level = ManaLevelCapture.CaptureScene(
                    Context.World,
                    scene,
                    levelName,
                    _playMode?.EditorCameraPose);
                level.Save(path);
                scene.LevelPath = path;
                scene.Name = levelName;
                MarkSceneSaved(scene, path);
                _viewModel.Levels.MarkSaved(path);
                _viewModel.Levels.SetStatus($"Saved scene '{scene.Name}' ({level.Nodes.Count} objects).");
            }
            catch (Exception exception)
            {
                _viewModel.Levels.SetStatus($"Could not save scene '{scene.Name}': {exception.Message}");
                Trace.TraceWarning($"Failed to save scene '{scene.Name}' to '{path}': {exception}");
            }
        }

        public bool SceneHasChanges(ManaScene scene)
        {
            if (string.IsNullOrWhiteSpace(scene.LevelPath) || string.IsNullOrWhiteSpace(scene.SavedLevelContentJson))
            {
                return false;
            }

            try
            {
                // Cached for half a second: this is polled every frame and capturing the scene is not cheap.
                return SceneDirtyCache.IsDirty(scene, () => !string.Equals(
                    scene.SavedLevelContentJson,
                    CaptureSceneContentJson(scene, scene.LevelPath),
                    StringComparison.Ordinal));
            }
            catch (Exception exception)
            {
                Trace.TraceWarning($"Could not compute dirty state for scene '{scene.Name}': {exception}");
                return true;
            }
        }

        /// <summary>
        /// Drops the Player Start where the camera is looking, or moves the one that already exists --
        /// there is only ever one, because a level with two of them has no answer to where Play starts.
        /// </summary>
        public void PlacePlayerStart()
        {
            if (IsLoading || _playMode == null)
            {
                return;
            }

            var position = _playMode.GetPlayFromCameraPosition();
            var existing = ManaPlayerStartNode.Find(Context.World);

            if (existing != null)
            {
                existing.Position = position;
                existing.UpdateTransform();
                _viewModel.Levels.SetStatus("Moved the Player Start here.");
                return;
            }

            var start = new ManaPlayerStartNode(Context, position);
            var targetScene = CurrentLevelScene() ?? Context.World.DefaultScene;
            lock (Context.World.SceneLock)
            {
                targetScene.Children.Add(start);
            }

            // A start marker is scenery to click through, exactly like the grid planes: picking it would
            // mean placing a voxel on top of it rather than on the ground it is standing on.
            _pickingExclusions.Add(start);
            _viewModel.Levels.SetStatus("Placed a Player Start.");
        }

        public void TogglePlay()
        {
            if (PrefabEditingBlocksLevelAction()) { return; }
            if (_playMode == null || IsLoading || _openingLevel)
            {
                return;
            }

            if (_playMode.IsPlaying)
            {
                _playMode.Stop();
                _viewModel.Levels.SetStatus("Stopped.");
                return;
            }

            CancelMeshPickerPlacementPreview();
            HideGameModePreviews();
            if (!_playMode.Start())
            {
                _viewModel.Levels.SetStatus(_playMode.LastError ?? "Could not start play mode.");
                return;
            }

            _viewModel.Levels.SetStatus(HasPlayerStart
                ? "Playing from the Player Start."
                : "Playing from the world origin -- place a Player Start to choose where.");
        }

        public void PlayFromCamera()
        {
            if (PrefabEditingBlocksLevelAction()) { return; }
            if (_playMode == null || IsLoading || _openingLevel || _playMode.IsPlaying)
            {
                return;
            }

            CancelMeshPickerPlacementPreview();
            HideGameModePreviews();
            if (!_playMode.Start(_playMode.GetPlayFromCameraPosition()))
            {
                _viewModel.Levels.SetStatus(_playMode.LastError ?? "Could not start play mode.");
                return;
            }

            _viewModel.Levels.SetStatus("Playing from the camera.");
        }

        /// <summary>
        /// Saves the level to a scratch file and starts a second process on it. The level goes through disk
        /// rather than through the command line because that is exactly what a standalone run has to be able
        /// to do anyway -- if the level cannot survive a save and a load, the launch would only be hiding it.
        /// </summary>
        public void PlayInNewWindow()
        {
            if (PrefabEditingBlocksLevelAction()) { return; }
            if (IsLoading)
            {
                return;
            }

            if (OperatingSystem.IsBrowser())
            {
                _viewModel.Levels.SetStatus("A browser tab can't open a second play window; press F5 to play here.");
                return;
            }

            try
            {
                var scratchPath = Path.Combine(Path.GetTempPath(), "ManaVoxel", "playtest" + ManaLevel.FileExtension);
                var level = ManaLevelCapture.Capture(Context.World, "Playtest", _playMode?.EditorCameraPose);
                level.Save(scratchPath);

                var executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable))
                {
                    _viewModel.Levels.SetStatus("Could not work out which executable to launch.");
                    return;
                }

                var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
                startInfo.ArgumentList.Add("--play");
                startInfo.ArgumentList.Add(scratchPath);
                startInfo.ArgumentList.Add("--graphics");
                startInfo.ArgumentList.Add(GraphicsBackend == ManaGraphicsBackendKind.WebGpu ? "webgpu" : "opengl");

                if (!string.IsNullOrWhiteSpace(_requestedWebGpuBackend))
                {
                    startInfo.ArgumentList.Add("--wgpu-backend");
                    startInfo.ArgumentList.Add(_requestedWebGpuBackend);
                }

                Process.Start(startInfo);
                _viewModel.Levels.SetStatus("Launched a play window.");
            }
            catch (Exception exception)
            {
                _viewModel.Levels.SetStatus($"Could not launch a play window: {exception.Message}");
                Trace.TraceWarning($"Failed to launch play window: {exception}");
            }
        }

        /// <summary>
        /// Editing the world underneath a running game is how a level ends up saved with a character in it
        /// and the camera welded to a boom, so anything that rebuilds the scene stops play first.
        /// </summary>
        private void StopPlayBeforeEditing()
        {
            if (_playMode is { IsPlaying: true })
            {
                _playMode.Stop();
            }
        }

        private ManaScene? CurrentLevelScene()
        {
            var currentPath = _viewModel.Levels.CurrentPath;
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return null;
            }

            lock (Context.World.SceneLock)
            {
                foreach (var scene in Context.World.Scenes)
                {
                    if (string.Equals(scene.LevelPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return scene;
                    }
                }
            }

            return null;
        }

        private void MarkSceneSaved(ManaScene scene, string path)
        {
            scene.LevelPath = path;
            scene.SavedLevelContentJson = CaptureSceneContentJson(scene, path);
            SceneDirtyCache.Invalidate(scene);
        }

        private string CaptureSceneContentJson(ManaScene scene, string path)
        {
            var levelName = string.IsNullOrWhiteSpace(scene.Name)
                ? ManaLevelLibrary.DisplayName(path)
                : scene.Name;
            return ManaLevelCapture.CaptureSceneContentJson(Context.World, scene, levelName);
        }

        private void RemoveOpenedLevelScenes()
        {
            var removed = new List<ManaScene>();
            lock (Context.World.SceneLock)
            {
                for (var i = Context.World.Scenes.Count - 1; i >= 0; i--)
                {
                    var scene = Context.World.Scenes[i];
                    if (string.IsNullOrWhiteSpace(scene.LevelPath))
                    {
                        continue;
                    }

                    removed.Add(scene);
                    Context.World.Scenes.RemoveAt(i);
                }
            }

            if (removed.Count == 0)
            {
                return;
            }

            var selection = Context.World.Selection;
            if (selection.SelectedScene != null && removed.Contains(selection.SelectedScene))
            {
                selection.SelectedScene = null;
            }

            if (selection.SelectedNode != null && removed.Any(scene => ContainsNode(scene, selection.SelectedNode)))
            {
                selection.SelectedNode = null;
                selection.SelectedComponent = null;
            }

            _pickingExclusions.RemoveWhere(node => removed.Any(scene => ContainsNode(scene, node)));
        }

        private static bool ContainsNode(ManaScene scene, ManaSceneNode node)
        {
            foreach (var child in scene.Children)
            {
                if (ContainsNode(child, node))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsNode(ManaSceneNode root, ManaSceneNode node)
        {
            if (ReferenceEquals(root, node))
            {
                return true;
            }

            foreach (var child in root.Children)
            {
                if (ContainsNode(child, node))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Opens the level named by <c>--play</c>, once the world has finished loading.</summary>
        private void OpenStartupLevelIfRequested()
        {
            if (string.IsNullOrEmpty(StartupLevelPath))
            {
                return;
            }

            var path = StartupLevelPath;
            StartupLevelPath = null;

            if (!File.Exists(path))
            {
                _viewModel.Levels.SetStatus($"Level '{path}' does not exist.");
                return;
            }

            OpenLevel(path);
        }

        /// <summary>Drives <see cref="_commandRunner"/> once <see cref="CommandFilePath"/> is set, one tick
        /// per past-loading frame -- same guarantee <see cref="OpenStartupLevelIfRequested"/> relies on.</summary>
        private void TickCommandFileIfRequested()
        {
            if (_commandRunner == null)
            {
                if (string.IsNullOrEmpty(CommandFilePath))
                {
                    return;
                }

                _commandRunner = new ManaCommandRunner(
                    CommandFilePath,
                    Context,
                    _webGpuContext == null ? null : _webGpuContext.CaptureFrameAsync,
                    _webGpuContext == null ? null : _webGpuContext.DumpEnvironmentFaces);
                VoxelCommands.Register(
                    _commandRunner,
                    OpenLevel,
                    on =>
                    {
                        if (on != IsMatchRunning)
                        {
                            TogglePlay();
                        }
                    },
                    DropSelectedToFloor);
                // Steps out of (or back into) the running match, as F8 does.
                _commandRunner.Register("eject", _ => ToggleEject());
                _commandRunner.Register("measure", command => RunMeasureCommand(command.Name, command.Position));
                _commandRunner.Register("gizmo_mode", command =>
                    _viewModel.TransformGizmoMode = Enum.Parse<SceneTransformGizmoMode>(command.Name ?? nameof(SceneTransformGizmoMode.Translate), true));
                _commandRunner.Register("scale_box", command => RunScaleBoxCommand(command.Name, command.Position));
#if !MANA_BROWSER
                _commandRunner.Register("live_scan", command => RunLiveScanCommand(command.Name, command.Path));
#endif
            }

            _commandRunner.Tick();
        }

        // A Scale box handle of the selection dragged to a world point, then where that left the node.
        private void RunScaleBoxCommand(string? name, float[]? position)
        {
            var handle = SceneScaleBoxHandle.Parse(name)
                ?? throw new InvalidDataException($"scale_box: '{name}' is not a handle (e.g. +x, -y, +x-y).");
            if (position is not { Length: 3 } || Context.World.Selection.SelectedNode is not { } node || _transformGizmo == null)
            {
                throw new InvalidDataException("scale_box: needs a selected node and a world 'position' to drag to.");
            }

            var dragged = _transformGizmo.DragBoxHandle(node, handle, new Vector3(position[0], position[1], position[2]));
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                $"Command file: scale_box {handle} {(dragged ? "dragged" : "missed")}; {node.Name} at {node.Position}, scale {node.Scale}");
        }
    }
}
