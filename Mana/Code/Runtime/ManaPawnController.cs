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
