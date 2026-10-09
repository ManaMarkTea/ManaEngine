using Mana.Code.Nodes;

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
        private Vector3 _spawnPosition;
        private float _spawnYaw;

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

        /// <summary>The level game modes running this play (see ManaLevelGameMode), and their HUD layer.</summary>
        public ManaGameModeRunner Modes { get; init; } = new();

        /// <summary>Makes the session a mode is handed; the host fills in its view of input and the command runner.</summary>
        public Func<Action, ManaPlaySession>? CreateSession { get; set; }

        private bool _exitRequested;
        private bool _savedLensShift;

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
            _exitRequested = false;

            // Every open level's game modes run together. The character comes only when one of them asks for it
            // (the Character mode); otherwise play is the running world and a free debug camera.
            var modes = _context.World.Scenes.SelectMany(scene => scene.GameModes).ToList();
            if (!ManaGameModeRunner.AnyWantsPlayerCharacter(modes))
            {
                return StartWithoutPlayer(modes);
            }

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
            _spawnPosition = spawnPosition;
            _spawnYaw = spawnYaw;

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

            // A play-mode affordance, set on the character rather than baked into the spawn so an
            // editor-spawned character stays a single jumper. FlyingMountComponent is parked: re-adding it
            // here means dropping this back to 1, since it takes off on the air jump.
            player.MaxJumpCount = JumpsFor(modes);

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
            CoverWindow();

            // Before BeginPlay: the game state resets there, and course pieces read the player from it.
            _context.World.GameState.Player = player;
            _context.World.BeginPlay();
            BeginModes(modes);
            PlayStateChanged?.Invoke(true);
            return true;
        }

        /// <summary>The Character mode's jump count (the most any asks for), or a double jump.</summary>
        public static int JumpsFor(IEnumerable<ManaLevelGameMode> modes) =>
            modes.OfType<ManaCharacterGameMode>().Select(mode => Math.Max(1, mode.MaxJumps)).DefaultIfEmpty(2).Max();

        // No character wanted: the world runs and the editor camera flies free, or a mode drives it.
        private bool StartWithoutPlayer(List<ManaLevelGameMode> modes)
        {
            var camera = _context.World.CameraNode;
            _savedCameraPosition = camera?.Position ?? Vector3.Zero;
            _savedCameraRotation = camera?.Rotation ?? Vector3.Zero;
            _savedCameraMode = _pawnController.CameraMode;
            _savedPossessedPawn = _pawnController.PossessedPawn;
            _savedReleaseOnEscape = _pawnController.ReleaseOnEscape;
            _savedMouseLookControls = _pawnController.MouseLookControlsEnabled;
            _pawnController.Possess(null);
            _pawnController.ReleaseOnEscape = false;

            _startNode = ManaPlayerStartNode.Find(_context.World);
            if (_startNode != null)
            {
                _startNode.IsMarkerVisible = false;
            }

            IsPlaying = true;
            CoverWindow();
            _context.World.BeginPlay();
            BeginModes(modes);
            PlayStateChanged?.Invoke(true);
            return true;
        }

        // The editor's panels fold away while playing, so the picture fills the window instead of the old viewport.
        private void CoverWindow()
        {
            var camera = _context.World.MainCamera;
            _savedLensShift = camera.LensShiftToViewport;
            camera.LensShiftToViewport = false;
        }

        private void BeginModes(List<ManaLevelGameMode> modes)
        {
            if (modes.Count == 0)
            {
                return;
            }

            var session = CreateSession?.Invoke(() => _exitRequested = true)
                ?? new ManaPlaySession(_context, () => false, () => false, () => _exitRequested = true, standalone: false);
            Modes.Begin(modes, session);
        }

        /// <summary>Once a frame while playing: runs the level's game modes, and leaves play if one asked to.</summary>
        public void Tick(float deltaSeconds)
        {
            if (!IsPlaying)
            {
                return;
            }

            Modes.Tick(deltaSeconds);
            if (_exitRequested)
            {
                _exitRequested = false;
                Stop();
            }
        }

        public void Stop()
        {
            if (!IsPlaying)
            {
                return;
            }

            IsPlaying = false;
            Modes.End();
            _context.World.MainCamera.LensShiftToViewport = _savedLensShift;
            _context.World.EndPlay();
            _context.World.GameState.Player = null;

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

        /// <summary>Plays the level again from the spawn without leaving play -- the results screen's Play Again, and R.</summary>
        public void Restart()
        {
            if (IsPlaying && !Modes.HandlesExitKeys)
            {
                _context.World.GameState.Restart(_context.World, _spawnPosition, _spawnYaw);
            }
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
