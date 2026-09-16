using Mana.Code.Nodes;
using Silk.NET.Input;

namespace Mana.Code
{
    // Possession and input glue for ManaPawnNode: click a pawn to take control, click the world to
    // send it there, WASD (or the arrow keys) to drive it directly.
    //
    // The drive keys are WoW's, and split on the look button: held, A/D strafe and the character's
    // facing is locked to the camera; released, A/D turn in place and the camera swings around with
    // them. Forward/back walks along the character's own facing either way. See UpdateDriveInput and
    // ApplyMouselookDrive, and ManaSpringArmComponent.FollowTargetYaw for the rule that keeps the
    // boom and the character from steering each other in circles.
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

        // Third-person boom locked to the character: right-drag steers both together, the turn keys
        // swing the camera around with the character, and it always sits behind whichever way the
        // character ends up looking.
        Follow,

        // The same boom, but free of the character's facing -- right-drag orbits without steering it.
        // Better for looking at the character, or for lining up a jump you are about to make.
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

        // How fast the left/right arrows swing the rig round. WoW's keyboard turn is far slower than a
        // mouse flick and deliberately so -- it is meant to be a steady aim, not a snap.
        public float KeyboardTurnDegreesPerSecond { get; set; } = 180.0f;

        /// <summary>
        /// Turns on the play-mode mouse scheme: a left-drag orbits the camera without turning the
        /// character and the pointer is grabbed for a look drag (see
        /// ManaSpringArmComponent.MouseLookControls), and click-to-move gets out of the way so that a
        /// left-press is not also a walk order.
        /// </summary>
        public bool MouseLookControlsEnabled
        {
            get => mouseLookControlsEnabled;
            set
            {
                mouseLookControlsEnabled = value;
                SyncCameraMode();
            }
        }

        private bool mouseLookControlsEnabled;

        /// <summary>
        /// Whether Escape releases the possessed pawn. Play mode turns this off and claims the key for
        /// itself: while playing, Escape means "stop playing", and letting it also unpossess would leave
        /// the character standing there under nobody's control for the frame before play ended.
        /// </summary>
        public bool ReleaseOnEscape { get; set; } = true;

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

            // Refined per frame in UpdateDriveInput, which knows whether a look drag is in progress.
            SpringArm.FollowTargetYaw = CameraMode == ManaCameraMode.Follow;
            SpringArm.MouseLookControls = MouseLookControlsEnabled;

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
                IgnoreBody = pawn.Body
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

            // Suppressed rather than turning ClickToMoveEnabled off, which would overwrite -- and on the
            // way back out of play, forget -- whatever the host had set it to.
            if (!ClickToMoveEnabled || MouseLookControlsEnabled || PossessedPawn == null)
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

        // Forward/back and turning are always relative to the pawn's own facing, not the camera's --
        // see the WASD block below for the WoW-style strafe/turn split on the look button.
        public void UpdateDriveInput(bool keyboardCaptured)
        {
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();

            // Escape is the way back out. Without it, a possessed pawn swallows every subsequent world
            // click as a walk order and nothing else in the scene can be selected again.
            var releaseKeyDown = ReleaseOnEscape && !keyboardCaptured && keyboard?.IsKeyPressed(Key.Escape) == true;
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

            // Which look drag is running. Taken from the boom, whose value comes from the camera's own
            // drag latch, so a drag that started on the scene and wandered over a panel still counts;
            // the direct mouse read is only for Free mode, which has no boom.
            var springArm = SpringArm;
            var steerActive = springArm?.IsSteerGestureActive
                ?? (!GetUiWantsCaptureMouse() &&
                    Context.Input?.Mice.FirstOrDefault()?.IsButtonPressed(MouseButton.Right) == true);
            var orbitActive = springArm?.IsOrbitGestureActive ?? false;

            // Something else owns this pawn outright -- a flying mount, say. Everything below would be
            // writing a heading and a facing into a pawn that is not walking, and FollowTargetYaw in
            // particular would be actively harmful: whatever is flying it aims it from the camera, so a
            // camera that also followed it round would spin, exactly as a strafe used to.
            if (!pawn.LocomotionEnabled)
            {
                // Keep the edge detector current so landing with Space held cannot trigger a fresh jump.
                jumpKeyWasDown = keyboard?.IsKeyPressed(Key.Space) == true;
                if (!jumpKeyWasDown && pawn.IsJumpHeld)
                {
                    pawn.ReleaseJump();
                }
                pawn.DriveDirection = Vector3.Zero;
                pawn.DriveTurnInput = 0.0f;
                pawn.DesiredFacingDirection = null;

                if (springArm != null)
                {
                    springArm.FollowTargetYaw = false;
                }

                return;
            }

            if (!KeyboardDriveEnabled || keyboardCaptured || keyboard == null)
            {
                pawn.DriveDirection = Vector3.Zero;
                pawn.DriveTurnInput = 0.0f;
                pawn.DesiredFacingDirection = null;

                if (springArm != null)
                {
                    springArm.FollowTargetYaw = false;
                }

                return;
            }

            // WoW's bindings: W/S (or up/down) walk, A/D strafe with no modifier needed, and the left and
            // right arrows turn -- which here means turning the whole rig, camera and character together,
            // exactly as a right-drag does but at a fixed rate.
            float forwardInput = 0.0f;
            if (keyboard.IsKeyPressed(Key.W) || keyboard.IsKeyPressed(Key.Up))
            {
                forwardInput += 1.0f;
            }

            if (keyboard.IsKeyPressed(Key.S) || keyboard.IsKeyPressed(Key.Down))
            {
                forwardInput -= 1.0f;
            }

            float strafeInput = 0.0f;
            if (keyboard.IsKeyPressed(Key.D))
            {
                strafeInput += 1.0f;
            }

            if (keyboard.IsKeyPressed(Key.A))
            {
                strafeInput -= 1.0f;
            }

            float turnInput = 0.0f;
            if (keyboard.IsKeyPressed(Key.Right))
            {
                turnInput += 1.0f;
            }

            if (keyboard.IsKeyPressed(Key.Left))
            {
                turnInput -= 1.0f;
            }

            // Turning the rig is the boom's job, so the character can keep following it the one way it
            // already does. Only a pawn with no boom at all turns itself.
            var turningRig = turnInput != 0.0f && springArm != null;
            if (turningRig)
            {
                var deltaSeconds = (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
                springArm!.AddYawTurn(-turnInput * KeyboardTurnDegreesPerSecond.ToRadians() * deltaSeconds);
            }

            pawn.DriveTurnInput = springArm == null ? turnInput : 0.0f;

            var steering = steerActive || turningRig;
            ApplyCameraRelativeDrive(pawn, springArm, forwardInput, strafeInput, steering);

            // The camera may only ride along with turns the character makes on its own -- a walk order,
            // nothing else here -- and only while nothing is aiming the character from the camera. Both
            // ways at once and the two chase each other, which is what made a strafe spin forever.
            if (springArm != null)
            {
                var characterIsLed = steering || forwardInput != 0.0f || strafeInput != 0.0f || orbitActive;
                springArm.FollowTargetYaw = CameraMode == ManaCameraMode.Follow && !characterIsLed;
            }

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

        // Movement is camera-relative: forward is away from the viewer, strafe is across the view. The
        // character then turns to look wherever it is actually walking.
        //
        // Every heading here is built from the camera, never from the pawn's own facing. That is the
        // whole trick: the camera is only ever moved by the player, so a turn toward one of these
        // headings has a target that stays put long enough to reach. A heading measured off the pawn
        // instead -- "90 degrees to my right" -- moves with every degree of the turn and is never
        // arrived at, which is a character that spins instead of strafing.
        private void ApplyCameraRelativeDrive(
            ManaPawnNode pawn,
            ManaSpringArmComponent? springArm,
            float forwardInput,
            float strafeInput,
            bool steering)
        {
            var cameraForward = FlattenToGround(springArm?.GetForward() ?? Context.World.MainCamera.Owner.Forward);

            // Cross in this order, not the reverse: it has to match ManaSceneNode.Right, which for a
            // -Z-forward camera puts +X to the right.
            var cameraRight = Vector3.Cross(cameraForward, Vector3.UnitY);

            var drive = (cameraForward * forwardInput) + (cameraRight * strafeInput);
            pawn.DriveDirection = drive;

            if (strafeInput != 0.0f)
            {
                // Strafing looks where it steps, smoothly, including the diagonal when forward is held
                // too -- so sidestepping reads as stepping sideways rather than sliding.
                pawn.DesiredFacingDirection = drive;
                pawn.DesiredFacingIsInstant = false;
            }
            else if (steering)
            {
                // A right-drag or a turn key aims the character, even standing still, so the rig turns
                // as one. Instant, or a fast flick would visibly drag the character behind the view
                // instead of turning it with it.
                pawn.DesiredFacingDirection = cameraForward;
                pawn.DesiredFacingIsInstant = true;
            }
            else if (forwardInput != 0.0f)
            {
                // Walking with neither: face the way we are actually travelling, so running at the
                // camera turns round and runs at it rather than backpedalling the whole way. Eased
                // rather than snapped, so the about-face reads as a turn -- and so setting off after a
                // left-drag look-around is a turn rather than a jolt.
                //
                // Backpedalling is still there, under the right-drag: that sets steering above, which
                // pins facing to the view and walks the character out from under it.
                pawn.DesiredFacingDirection = drive;
                pawn.DesiredFacingIsInstant = false;
            }
            else
            {
                // Standing still with nothing aiming it -- a left-drag orbit lands here, which is what
                // lets you look your character over without it turning to follow the camera.
                pawn.DesiredFacingDirection = null;
            }
        }

        // A steeply-pitched camera's forward vector is mostly vertical; falling back to world -Z keeps
        // the strafe-turn heading usable when looking straight down instead of producing a zero-length
        // vector.
        private static Vector3 FlattenToGround(Vector3 direction)
        {
            var flattened = new Vector3(direction.X, 0.0f, direction.Z);
            if (flattened.LengthSquared() < 1e-6f)
            {
                return -Vector3.UnitZ;
            }

            return Vector3.Normalize(flattened);
        }

        /// <summary>
        /// Whether any UI layer owns the mouse -- same reasoning as CameraComponent's own check:
        /// routed through the game mode so a ManaUI drag counts too, not just ImGui.
        /// </summary>
        private bool GetUiWantsCaptureMouse()
        {
            try
            {
                return Context?.GameMode?.UiWantsMouse ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
