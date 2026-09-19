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
        private readonly List<ManaCommand> _commands;

        private int _index;
        private int _waitFramesRemaining;
        private Task? _pendingScreenshot;

        public ManaCommandRunner(
            string commandFilePath,
            ManaContext context,
            ManaWebGpuContext? webGpuContext,
            Action<string> openLevel)
        {
            _context = context;
            _webGpuContext = webGpuContext;
            _openLevel = openLevel;
            _commands = ManaCommandFile.Load(commandFilePath);
        }

        public void Tick()
        {
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
                        ApplyMoveCamera(command);
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

                        _pendingScreenshot = _webGpuContext.CaptureFrameAsync(RequirePath(command));
                        return;

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

        private static string RequirePath(ManaCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Path))
            {
                throw new InvalidDataException($"Command file: '{command.Type}' is missing its required 'path'.");
            }

            return command.Path;
        }

        private void ApplyMoveCamera(ManaCommand command)
        {
            var owner = _context.World.MainCamera.Owner;

            if (command.Position is { Length: 3 } position)
            {
                owner.Position = new Vector3(position[0], position[1], position[2]);
            }

            if (command.Target is { Length: 3 } target)
            {
                var direction = new Vector3(target[0], target[1], target[2]) - owner.Position;
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
}
