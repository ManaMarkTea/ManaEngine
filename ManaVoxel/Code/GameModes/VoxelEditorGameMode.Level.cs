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

        /// <summary>What the play HUD reads out, so the controls are on screen rather than in a menu.</summary>
        private string GetPlayHudText()
        {
            var player = _playMode?.Player;
            var name = player?.Name ?? "Character";
            return $"{name}  --  WASD move, Shift run, Space jump, C camera, Esc stop";
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
            ManaLevelLoader.RemoveAuthoredNodes(Context);
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
                var result = ManaLevelLoader.Load(level, Context, Context.World.DefaultScene);

                _viewModel.ResetVoxelLayersAfterLevelChange();
                if (result.VoxelsNode != null)
                {
                    _viewModel.AttachVoxelData(result.VoxelsNode.VoxelShape, result.VoxelsNode);
                }

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
                var level = ManaLevelCapture.Capture(
                    Context.World,
                    ManaLevelLibrary.DisplayName(path),
                    _playMode?.EditorCameraPose);
                level.Save(path);
                _viewModel.Levels.MarkSaved(path);
                _viewModel.Levels.SetStatus($"Saved {level.Nodes.Count} object(s) to {Path.GetFileName(path)}.");
            }
            catch (Exception exception)
            {
                _viewModel.Levels.SetStatus($"Could not save '{Path.GetFileName(path)}': {exception.Message}");
                Trace.TraceWarning($"Failed to save level '{path}': {exception}");
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
            lock (Context.World.SceneLock)
            {
                Context.World.DefaultScene.Children.Add(start);
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
