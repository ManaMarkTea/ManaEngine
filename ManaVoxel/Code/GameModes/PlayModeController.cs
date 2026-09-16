using Mana.Code.Nodes;
using ManaVoxel.Code.Gameplay.Flight;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// The editor's play state: spawn the character at the Player Start, possess it, put the camera on its
    /// boom, and get the editor out of the way -- then put everything back on stop.
    /// <para>
    /// In-process rather than a second window, because the world is already loaded. That is the whole point
    /// of it: pressing Play is a frame away, so it costs nothing to keep pressing it. The standalone
    /// <c>--play</c> run is the other half (see VoxelPlayGameMode) and shares this class's spawn rules, so
    /// what you tested in the editor is what launches.
    /// </para>
    /// <para>
    /// Restoring the camera on stop is not a nicety: the boom drives the camera node directly, so leaving
    /// play without putting the camera back drops the editor view wherever the character happened to be
    /// standing, looking wherever it happened to be looking.
    /// </para>
    /// </summary>
    public sealed class PlayModeController
    {
        private readonly ManaContext _context;
        private readonly ManaPawnController _pawnController;
        private readonly Func<Vector3, float, ManaPawnNode?> _spawnPlayer;
        private readonly Action<ManaPawnNode> _despawnPlayer;

        private ManaPawnNode? _player;
        private ManaPlayerStartNode? _startNode;

        private Vector3 _savedCameraPosition;
        private Vector3 _savedCameraRotation;
        private ManaCameraMode _savedCameraMode;
        private ManaPawnNode? _savedPossessedPawn;
        private bool _savedReleaseOnEscape;
        private bool _savedMouseLookControls;

        public PlayModeController(
            ManaContext context,
            ManaPawnController pawnController,
            Func<Vector3, float, ManaPawnNode?> spawnPlayer,
            Action<ManaPawnNode> despawnPlayer)
        {
            _context = context;
            _pawnController = pawnController;
            _spawnPlayer = spawnPlayer;
            _despawnPlayer = despawnPlayer;
        }

        public bool IsPlaying { get; private set; }

        /// <summary>The character being played, or null while editing.</summary>
        public ManaPawnNode? Player => _player;

        /// <summary>Why the last <see cref="Start"/> failed, for the UI to show.</summary>
        public string? LastError { get; private set; }

        /// <summary>Raised on both entering and leaving play, so the UI can hide or come back.</summary>
        public event Action<bool>? PlayStateChanged;

        /// <summary>
        /// Where the editor camera was when play started, or null while editing. Saving mid-play reads this
        /// rather than the live camera, which by then is hanging off the character's boom.
        /// </summary>
        public (Vector3 Position, Vector3 Rotation)? EditorCameraPose =>
            IsPlaying ? (_savedCameraPosition, _savedCameraRotation) : null;

        /// <summary>
        /// Enters play. <paramref name="spawnOverride"/> is "play from here" -- when given, the character
        /// starts there instead of at the Player Start.
        /// </summary>
        public bool Start(Vector3? spawnOverride = null)
        {
            if (IsPlaying)
            {
                return true;
            }

            LastError = null;

            // No Player Start falls back to the same spot Shift+F5 uses, not the world origin -- the
            // origin is a room's corner in every game mode that builds a square floor, and a capsule
            // dropped exactly onto a box collider's edge can land with no contact generated at all (the
            // narrow phase has nothing but degenerate boundary geometry to test against), which reads as
            // falling straight through a floor that is visibly right there. Wherever the camera is already
            // looking is always solid ground by comparison.
            var start = ManaPlayerStartNode.Find(_context.World);
            var spawnPosition = spawnOverride ?? start?.SpawnPosition ?? GetPlayFromCameraPosition();
            var spawnYaw = spawnOverride.HasValue ? 0.0f : start?.SpawnYawRadians ?? 0.0f;

            // Spawned a little clear of the floor so the capsule settles onto it rather than starting
            // interpenetrated, which the solver resolves as a shove. Same reasoning as SpawnCharacter.
            spawnPosition.Y += 0.5f;

            var player = _spawnPlayer(spawnPosition, spawnYaw);
            if (player == null)
            {
                LastError = "No character could be spawned -- check that a character pack is in Assets.";
                return false;
            }

            var camera = _context.World.CameraNode;
            _savedCameraPosition = camera?.Position ?? Vector3.Zero;
            _savedCameraRotation = camera?.Rotation ?? Vector3.Zero;
            _savedCameraMode = _pawnController.CameraMode;
            _savedPossessedPawn = _pawnController.PossessedPawn;
            _savedReleaseOnEscape = _pawnController.ReleaseOnEscape;
            _savedMouseLookControls = _pawnController.MouseLookControlsEnabled;

            // Flight is a play-mode affordance, added to the character rather than baked into the spawn so
            // an editor-spawned character stays a plain walker.
            player.Components.Add(new FlyingMountComponent(player));

            _player = player;
            _startNode = start;
            if (start != null)
            {
                start.IsMarkerVisible = false;
            }

            // Escape is ours while playing -- see ManaPawnController.ReleaseOnEscape.
            _pawnController.ReleaseOnEscape = false;
            _pawnController.KeyboardDriveEnabled = true;

            // Left-drag becomes a camera orbit for the duration of play, which it cannot be in the
            // editor -- there a left-press is a selection or a walk order.
            _pawnController.MouseLookControlsEnabled = true;
            _pawnController.Possess(player);
            _pawnController.SetCameraMode(ManaCameraMode.Follow);

            IsPlaying = true;
            _context.World.BeginPlay();
            PlayStateChanged?.Invoke(true);
            return true;
        }

        public void Stop()
        {
            if (!IsPlaying)
            {
                return;
            }

            IsPlaying = false;
            _context.World.EndPlay();

            // Released before the pawn is removed: unregistering a possessed pawn releases it anyway, but
            // does so after the boom has already been torn off a node that is on its way out of the scene.
            _pawnController.Possess(null);
            _pawnController.SetCameraMode(_savedCameraMode);
            _pawnController.ReleaseOnEscape = _savedReleaseOnEscape;
            _pawnController.MouseLookControlsEnabled = _savedMouseLookControls;

            if (_player != null)
            {
                _despawnPlayer(_player);
                _player = null;
            }

            if (_startNode != null)
            {
                _startNode.IsMarkerVisible = true;
                _startNode = null;
            }

            var camera = _context.World.CameraNode;
            if (camera != null)
            {
                camera.Position = _savedCameraPosition;
                camera.Rotation = _savedCameraRotation;
                camera.UpdateTransform();
            }

            // Whatever was possessed before Play is possessed again, so pressing Play and stopping leaves
            // the editor exactly as it was found.
            if (_savedPossessedPawn != null && _pawnController.Pawns.Contains(_savedPossessedPawn))
            {
                _pawnController.Possess(_savedPossessedPawn);
            }

            _savedPossessedPawn = null;
            PlayStateChanged?.Invoke(false);
        }

        public void Toggle(Vector3? spawnOverride = null)
        {
            if (IsPlaying)
            {
                Stop();
            }
            else
            {
                Start(spawnOverride);
            }
        }

        /// <summary>
        /// Where "play from here" starts the character: on the ground under the point the camera is looking
        /// at, a short way in front of it. Same rule the editor's Spawn Character uses, so the two agree.
        /// </summary>
        public Vector3 GetPlayFromCameraPosition()
        {
            var camera = _context.World.MainCamera;
            var forward = camera.Owner.Forward;
            var flatForward = new Vector3(forward.X, 0.0f, forward.Z);
            flatForward = flatForward.LengthSquared() < 1e-6f
                ? -Vector3.UnitZ
                : Vector3.Normalize(flatForward);

            var position = camera.Owner.Position + flatForward * 8.0f;
            position.Y = 0.0f;
            return position;
        }
    }
}
