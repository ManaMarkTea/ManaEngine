using Mana.Code.Nodes;
using ManaCore.Graphics;
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
        private bool _stopKeyWasDown;
        private bool _undoKeyWasDown;
        private bool _redoKeyWasDown;
        private bool _saveKeyWasDown;

        /// <summary>Remembered so a second window launches on the backend this one is running.</summary>
        private string? _requestedWebGpuBackend;

        /// <summary>Set by <c>--play</c>, so the editor opens straight into the level it was handed.</summary>
        public string? StartupLevelPath { get; set; }

        public bool IsPlaying => _playMode is { IsPlaying: true };

        private void InitializePlayMode()
        {
            if (_pawnController == null)
            {
                return;
            }

            _playMode = new PlayModeController(
                Context,
                _pawnController,
                (position, yaw) => _viewModel.SpawnCharacterAt(position, yaw),
                pawn => _viewModel.RemoveCharacter(pawn));

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

            var stopKeyDown = keyboard.IsKeyPressed(Key.Escape);
            if (stopKeyDown && !_stopKeyWasDown && _playMode.IsPlaying)
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
        }

        /// <summary>What the play HUD reads out, so the controls are on screen rather than in a menu.</summary>
        private string GetPlayHudText()
        {
            var player = _playMode?.Player;
            var name = player?.Name ?? "Character";
            return $"{name}  --  WASD move, Shift run, Space jump (again to fly; Space climb, Shift surge, A/D turn, S brake), C camera, Esc stop";
        }

        // ---- IManaLevelHost ---------------------------------------------------------------------------

        bool IManaLevelHost.IsPlaying => IsPlaying;

        public bool CanPlay => !IsLoading && _playMode != null;

        public bool HasPlayerStart => ManaPlayerStartNode.Find(Context.World) != null;

        public void NewLevel()
        {
            if (IsLoading)
            {
                return;
            }

            StopPlayBeforeEditing();
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

        public void OpenLevel(string path)
        {
            if (IsLoading)
            {
                return;
            }

            StopPlayBeforeEditing();

            try
            {
                var level = ManaLevel.Load(path);
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
        }

        public void SaveLevel(string path)
        {
            if (IsLoading)
            {
                return;
            }

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

        private void ResetLoadedPlanet()
        {
            DetachKmlAirspace();
            _planet = null;
            _countryBorders = null;
            _stateBorders = null;
            lock (_drapedOverlays)
            {
                _drapedOverlays.Clear();
            }
        }

        private void AdoptLoadedPlanet(ManaGeoTools.Code.Planet.PlanetNode? planet)
        {
            _planet = planet;
            if (_planet != null && _viewModel.ShowKmlAirspace)
            {
                AttachKmlAirspace();
            }
        }

        public void SaveScene(ManaScene scene)
        {
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
                return !string.Equals(
                    scene.SavedLevelContentJson,
                    CaptureSceneContentJson(scene, scene.LevelPath),
                    StringComparison.Ordinal);
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
            if (_playMode == null || IsLoading)
            {
                return;
            }

            if (_playMode.IsPlaying)
            {
                _playMode.Stop();
                _viewModel.Levels.SetStatus("Stopped.");
                return;
            }

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
            if (_playMode == null || IsLoading || _playMode.IsPlaying)
            {
                return;
            }

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
            if (IsLoading)
            {
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
    }
}
