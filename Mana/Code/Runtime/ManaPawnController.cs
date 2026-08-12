using Mana.Code.Nodes;
using Silk.NET.Input;

namespace Mana.Code
{
    // Possession and input glue for ManaPawnNode: click a pawn to take control, click the world to
    // send it there, WASD (or the arrow keys) to drive it directly.
    //
    // WASD is also the camera fly control, so possessing a pawn takes those keys off the camera for
    // as long as it is possessed -- see IsCapturingMovementKeys, which the host wires to
    // CameraComponent.ShouldCaptureKeyboardInput. The camera keeps QE and its mouse orbit/pan, and
    // Escape hands the keys straight back.
    // Which camera is driving the view.
    public enum ManaCameraMode
    {
        // The editor's own fly camera. WASD/QE, right-drag to look. Unchanged behaviour.
        Free,

        // Third-person boom that swings around behind the character as it moves.
        Follow,

        // The same boom, but it stays wherever you leave it instead of auto-aligning -- better for
        // looking at the character, or for lining up a jump you are about to make.
        Orbit
    }

    public sealed class ManaPawnController : ManaObject
    {
        public enum ClickOutcome
        {
            // Nothing pawn-related happened; the host should fall through to its normal click handling.
            Ignored,

            // A pawn was clicked and is now possessed.
            Possessed,

            // The possessed pawn was given a destination; the host should leave its selection alone.
            Ordered
        }

        private readonly List<ManaPawnNode> pawns = new();
        private bool releaseKeyWasDown;
        private bool jumpKeyWasDown;
        private bool cameraKeyWasDown;
        private bool nextCharacterKeyWasDown;
        private bool previousCharacterKeyWasDown;

        // Raised on numpad +/- with the step direction. The controller has no idea what characters
        // exist -- that catalogue lives with the host's asset services -- so it asks rather than acts.
        public event Action<int>? ChangeCharacterRequested;

        public ManaPawnController(ManaContext context) : base(context)
        {
            Name = "Pawn Controller";
        }

        public ManaPawnNode? PossessedPawn { get; private set; }
        public IReadOnlyList<ManaPawnNode> Pawns => pawns;

        public bool ClickToMoveEnabled { get; set; } = true;
        public bool KeyboardDriveEnabled { get; set; } = true;

        // Wire this to CameraComponent.ShouldCaptureKeyboardInput. True means the pawn owns the
        // movement keys this frame and the camera's fly block must stand down; otherwise the camera
        // would consume WASD first and the character would never move.
        public bool IsCapturingMovementKeys => KeyboardDriveEnabled && PossessedPawn != null;

        // Height of the horizontal plane a click falls back to when the ray hits no geometry -- the
        // editor's ground planes are excluded from picking, so "click the empty floor" always lands
        // here rather than on a real hit.
        public float GroundPlaneHeight { get; set; }

        public HashSet<ManaSceneNode>? PickingExclusions { get; set; }

        public void Register(ManaPawnNode pawn)
        {
            if (!pawns.Contains(pawn))
            {
                pawns.Add(pawn);
            }
        }

        public void Unregister(ManaPawnNode pawn)
        {
            pawns.Remove(pawn);
            if (ReferenceEquals(PossessedPawn, pawn))
            {
                Possess(null);
            }
        }

        public void Possess(ManaPawnNode? pawn)
        {
            if (ReferenceEquals(PossessedPawn, pawn))
            {
                return;
            }

            if (PossessedPawn != null)
            {
                PossessedPawn.IsPossessed = false;
                PossessedPawn.StopMoving();
            }

            PossessedPawn = pawn;

            if (pawn != null)
            {
                pawn.IsPossessed = true;
            }

            // Re-resolve the boom against whoever is possessed now (or detach, if nobody is).
            SyncCameraMode();
        }

        // ---- Camera modes -------------------------------------------------------------------------

        public ManaCameraMode CameraMode { get; private set; } = ManaCameraMode.Free;

        // The boom in use, or null in Free mode / with nothing possessed. Exposed so the inspector can
        // show and tune it.
        public ManaSpringArmComponent? SpringArm { get; private set; }

        public void SetCameraMode(ManaCameraMode mode)
        {
            CameraMode = mode;
            SyncCameraMode();
        }

        public void CycleCameraMode()
        {
            SetCameraMode(CameraMode switch
            {
                ManaCameraMode.Free => ManaCameraMode.Follow,
                ManaCameraMode.Follow => ManaCameraMode.Orbit,
                _ => ManaCameraMode.Free
            });
        }

        // Attaching the boom needs a pawn to hang it off, so an attached mode with nothing possessed
        // quietly behaves as Free until something is. The requested mode is remembered either way, so
        // possessing a character later lands straight in the mode that was asked for.
        private void SyncCameraMode()
        {
            var camera = Context.World.MainCamera;
            var pawn = PossessedPawn;

            if (CameraMode == ManaCameraMode.Free || pawn == null)
            {
                if (SpringArm != null)
                {
                    SpringArm.Owner.Components.Remove(SpringArm);
                    SpringArm = null;
                }

                camera.DetachFromSpringArm();
                return;
            }

            if (SpringArm == null || !ReferenceEquals(SpringArm.Owner, pawn))
            {
                if (SpringArm != null)
                {
                    SpringArm.Owner.Components.Remove(SpringArm);
                }

                SpringArm = CreateSpringArmFor(pawn);
                pawn.Components.Add(SpringArm);
            }

            SpringArm.AutoAlignBehindTarget = CameraMode == ManaCameraMode.Follow;

            // Refreshed rather than only set at creation: a capsule added or removed after the boom
            // exists would otherwise leave the probe colliding with the character it follows.
            SpringArm.IgnoreBody = pawn.Body;

            camera.AttachToSpringArm(SpringArm);
        }

        private ManaSpringArmComponent CreateSpringArmFor(ManaPawnNode pawn)
        {
            // Scaled off the character so the framing holds for any model: eye-height socket, boom
            // about four body-heights back.
            var characterHeight = pawn.TryGetBindPoseHeight(out var bindHeight) && bindHeight > 0.0f
                ? bindHeight * MathF.Abs(pawn.Scale.Y)
                : 2.0f;

            // Start behind the character rather than in its face. The model faces +Z, so its heading is
            // (sin yaw, 0, cos yaw); the camera looks along -Z, so the boom yaw that matches that
            // heading -- and therefore sits behind it -- is the pawn's yaw plus half a turn.
            var behindYaw = pawn.Rotation.Y + MathF.PI;

            return new ManaSpringArmComponent(pawn)
            {
                YawRadians = behindYaw,
                SocketOffset = new Vector3(0.0f, characterHeight * 0.8f, 0.0f),
                ArmLength = characterHeight * 4.0f,
                MinArmLength = characterHeight * 0.5f,
                MaxArmLength = characterHeight * 20.0f,
                IgnoreBody = pawn.Body,

                // Auto-align follows where the character is actually going, and only while it is
                // going somewhere -- reading the drive input instead would swing the camera on a
                // key press even when the character is jammed against a wall.
                AutoAlignSource = () => (pawn.Body != null
                    ? pawn.Body.Velocity
                    : pawn.DriveDirection, pawn.IsMoving)
            };
        }

        // Called by the host on a left-click that the UI didn't already swallow. pickedNode is whatever
        // the host's own scene pick returned, so this never re-runs the (expensive) triangle pick.
        public ClickOutcome HandleClick(ManaSceneNode? pickedNode, Vector3 rayOrigin, Vector3 rayDirection)
        {
            var clickedPawn = ManaPawnNode.FindOwningPawn(pickedNode);
            if (clickedPawn != null)
            {
                Possess(clickedPawn);
                return ClickOutcome.Possessed;
            }

            if (!ClickToMoveEnabled || PossessedPawn == null)
            {
                return ClickOutcome.Ignored;
            }

            if (!TryResolveWalkTarget(rayOrigin, rayDirection, out var target))
            {
                return ClickOutcome.Ignored;
            }

            PossessedPawn.WalkTo(target);
            return ClickOutcome.Ordered;
        }

        // Prefers real geometry (walk onto a voxel layer or an imported mesh), and falls back to the
        // ground plane when the ray hits nothing pickable.
        private bool TryResolveWalkTarget(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 target)
        {
            var hit = ManaScenePicker.PickWithHit(Context.World.Scenes, rayOrigin, rayDirection, PickingExclusions);
            if (hit.HasValue)
            {
                target = hit.Value.Position;
                return true;
            }

            return TryIntersectGroundPlane(rayOrigin, rayDirection, out target);
        }

        private bool TryIntersectGroundPlane(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 target)
        {
            target = Vector3.Zero;
            if (MathF.Abs(rayDirection.Y) < 1e-6f)
            {
                return false;
            }

            var distance = (GroundPlaneHeight - rayOrigin.Y) / rayDirection.Y;
            if (distance <= 0.0f)
            {
                return false;
            }

            target = rayOrigin + rayDirection * distance;
            return true;
        }

        // Drive input is camera-relative so "up" always means "away from the viewer", which is the only
        // reading that stays intuitive as the camera orbits.
        public void UpdateDriveInput(bool keyboardCaptured)
        {
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();

            // Escape is the way back out. Without it, a possessed pawn swallows every subsequent world
            // click as a walk order and nothing else in the scene can be selected again.
            var releaseKeyDown = !keyboardCaptured && keyboard?.IsKeyPressed(Key.Escape) == true;
            if (releaseKeyDown && !releaseKeyWasDown)
            {
                Possess(null);
            }

            releaseKeyWasDown = releaseKeyDown;

            var cameraKeyDown = !keyboardCaptured && keyboard?.IsKeyPressed(Key.C) == true;
            if (cameraKeyDown && !cameraKeyWasDown)
            {
                CycleCameraMode();
            }

            cameraKeyWasDown = cameraKeyDown;

            // Numpad +/- steps through the character catalogue. The host owns the swap itself (it needs
            // the asset catalogue and the scene), so this only raises the request.
            var nextCharacterKeyDown = !keyboardCaptured && keyboard?.IsKeyPressed(Key.KeypadAdd) == true;
            if (nextCharacterKeyDown && !nextCharacterKeyWasDown)
            {
                ChangeCharacterRequested?.Invoke(1);
            }

            nextCharacterKeyWasDown = nextCharacterKeyDown;

            var previousCharacterKeyDown = !keyboardCaptured && keyboard?.IsKeyPressed(Key.KeypadSubtract) == true;
            if (previousCharacterKeyDown && !previousCharacterKeyWasDown)
            {
                ChangeCharacterRequested?.Invoke(-1);
            }

            previousCharacterKeyWasDown = previousCharacterKeyDown;

            var pawn = PossessedPawn;
            if (pawn == null)
            {
                return;
            }

            if (!KeyboardDriveEnabled || keyboardCaptured || keyboard == null)
            {
                pawn.DriveDirection = Vector3.Zero;
                return;
            }

            var camera = Context.World.MainCamera;
            var cameraForward = camera.Owner.Forward;
            var cameraRight = camera.Owner.Right;

            var forward = FlattenToGround(cameraForward);
            var right = FlattenToGround(cameraRight);

            // Arrow keys stay live alongside WASD -- they cost nothing and the camera never wanted them.
            var drive = Vector3.Zero;
            if (keyboard.IsKeyPressed(Key.W) || keyboard.IsKeyPressed(Key.Up))
            {
                drive += forward;
            }

            if (keyboard.IsKeyPressed(Key.S) || keyboard.IsKeyPressed(Key.Down))
            {
                drive -= forward;
            }

            if (keyboard.IsKeyPressed(Key.D) || keyboard.IsKeyPressed(Key.Right))
            {
                drive += right;
            }

            if (keyboard.IsKeyPressed(Key.A) || keyboard.IsKeyPressed(Key.Left))
            {
                drive -= right;
            }

            pawn.DriveDirection = drive;
            pawn.WantsToRun = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

            // Edge-triggered on both sides: the press is what gets buffered, and the release is what
            // cuts the jump short for a variable-height hop.
            var jumpKeyDown = keyboard.IsKeyPressed(Key.Space);
            if (jumpKeyDown && !jumpKeyWasDown)
            {
                pawn.PressJump();
            }
            else if (!jumpKeyDown && jumpKeyWasDown)
            {
                pawn.ReleaseJump();
            }

            jumpKeyWasDown = jumpKeyDown;
        }

        // A steeply-pitched camera's forward vector is mostly vertical; falling back to world -Z keeps
        // the drive keys usable when looking straight down instead of producing a zero-length heading.
        private static Vector3 FlattenToGround(Vector3 direction)
        {
            var flattened = new Vector3(direction.X, 0.0f, direction.Z);
            if (flattened.LengthSquared() < 1e-6f)
            {
                return -Vector3.UnitZ;
            }

            return Vector3.Normalize(flattened);
        }
    }
}
