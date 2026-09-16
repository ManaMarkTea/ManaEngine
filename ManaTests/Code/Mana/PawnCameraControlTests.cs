using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The WoW-style third-person controls: <see cref="ManaSpringArmComponent"/> as the authority on where the
/// view points, and <see cref="ManaPawnNode"/> turning itself to match.
/// <para>
/// What this suite exists to protect is one invariant: <b>the coupling between boom and character runs one
/// way at a time</b>. Facing may be slaved to the boom, or the boom may copy the character's own turns, but
/// never both at once. Both-at-once shipped twice, and both times it looked like a bug somewhere else -- the
/// character walking backward "jittered", and strafing "spun" -- because the symptom is a turn that never
/// arrives rather than anything obviously wrong at the call site.
/// </para>
/// <para>
/// The mechanism is worth stating because it is not visible in either component alone. A strafe heading is
/// measured off the camera; if the camera is then re-derived from the character, the heading moves with every
/// degree the character turns, so the gap it is closing never shrinks. The character spins at its full turn
/// rate forever and the camera spins with it. The last check drives exactly that configuration to prove
/// these checks can tell the difference, rather than passing because the simulation is too gentle to break.
/// </para>
/// </summary>
public static class PawnCameraControlChecks
{
    // Long enough for a 90-degree turn at the default 540 deg/s to finish many times over, so "still
    // turning at the end" can only mean it is never going to stop.
    private const int FrameCount = 240;
    private const float FrameSeconds = 1.0f / 60.0f;

    private static ManaContext CreateContext()
    {
        var world = new ManaWorld();
        return new ManaContext(null!, null!, world) { DeferGpuUploads = true };
    }

    private static (ManaPawnNode Pawn, ManaSpringArmComponent Arm) CreateRig(float pawnYaw)
    {
        var pawn = new ManaPawnNode(CreateContext(), Matrix4x4.Identity)
        {
            Rotation = new Vector3(0.0f, pawnYaw, 0.0f),

            // No skeleton and no clips: this suite is about headings, and the locomotion step reaches
            // the animation component either way.
            SkinningEnabled = false
        };

        var arm = new ManaSpringArmComponent(pawn)
        {
            // Behind the character to start, the same as ManaPawnController sets up.
            YawRadians = pawnYaw + MathF.PI,

            // The probe raycasts against the physics world, which a headless context has none of.
            ProbeForCollision = false,

            // 1:1, so the checks below read in the raw look input rather than through a feel constant.
            LookYawSensitivity = 1.0f
        };

        pawn.Components.Add(arm);
        return (pawn, arm);
    }

    // The horizontal direction the boom looks along, which is what the controller builds its headings from.
    private static Vector3 CameraForward(ManaSpringArmComponent arm)
    {
        var forward = arm.GetForward();
        return Vector3.Normalize(new Vector3(forward.X, 0.0f, forward.Z));
    }

    // Matches ManaPawnController.ApplyMouselookDrive: the camera's own right-hand side on the ground.
    private static Vector3 CameraRight(ManaSpringArmComponent arm)
    {
        return Vector3.Cross(CameraForward(arm), Vector3.UnitY);
    }

    private static float AngleBetweenDegrees(Vector3 a, Vector3 b)
    {
        var dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1.0f, 1.0f);
        return MathF.Acos(dot).ToDegrees();
    }

    /// <summary>
    /// One frame in the real order the engine runs them: the pawn moves and turns during the scene walk,
    /// then the boom is evaluated against where the pawn ended up (see ManaWorld.Update).
    /// </summary>
    private static void StepFrame(ManaPawnNode pawn, ManaSpringArmComponent arm)
    {
        ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(FrameSeconds);
        pawn.UpdateTransform();
        arm.Evaluate(FrameSeconds);
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------- the boom never writes its target
        // The regression itself: AddLookInput used to turn its owner and then re-derive its own yaw from
        // that owner's rotation, which is what closed the loop.
        var (lookPawn, lookArm) = CreateRig(0.0f);
        var pawnYawBeforeLook = lookPawn.Rotation.Y;
        var armYawBeforeLook = lookArm.YawRadians;

        lookArm.AddLookInput(0.5f, 0.0f);

        Check("a look input moves the boom", MathF.Abs(lookArm.YawRadians - armYawBeforeLook) > 1e-4f,
            $"{armYawBeforeLook.ToDegrees():0.#} -> {lookArm.YawRadians.ToDegrees():0.#} deg");

        Check("and leaves its target's rotation alone",
            MathF.Abs(lookPawn.Rotation.Y - pawnYawBeforeLook) < 1e-6f,
            "the boom is the authority on the view; the pawn turns itself");

        // ---------------------------------------------------------- a turn key is not a mouse delta
        // AddLookInput scales by LookYawSensitivity, which is a feel constant for raw mouse motion. A
        // turn key already knows the angle it wants (a rate times a frame), so putting it through the
        // same door would quietly retune the keyboard every time the mouse was retuned.
        var (_, turnArm) = CreateRig(0.0f);
        turnArm.LookYawSensitivity = 0.5f;

        var turnArmYawBefore = turnArm.YawRadians;
        turnArm.AddYawTurn(0.4f);

        Check("a turn key turns the boom by exactly what it asked for",
            MathF.Abs(WrapDegrees((turnArm.YawRadians - (turnArmYawBefore + 0.4f)).ToDegrees())) < 0.01f,
            "not scaled by the mouse sensitivity");

        turnArmYawBefore = turnArm.YawRadians;
        turnArm.AddLookInput(0.4f, 0.0f);

        Check("while a mouse look is scaled by the sensitivity",
            MathF.Abs(WrapDegrees((turnArm.YawRadians - (turnArmYawBefore + 0.2f)).ToDegrees())) < 0.01f,
            "0.4 at 0.5 sensitivity is 0.2");

        // ---------------------------------------------------------- yaw follow is one-way and opt-in
        var (followPawn, followArm) = CreateRig(0.0f);
        followArm.FollowTargetYaw = false;
        followArm.Evaluate(FrameSeconds);

        var pinnedArmYaw = followArm.YawRadians;
        followPawn.Rotation = new Vector3(0.0f, 1.0f, 0.0f);
        followArm.Evaluate(FrameSeconds);

        Check("with follow off, a target that turns does not drag the boom with it",
            MathF.Abs(followArm.YawRadians - pinnedArmYaw) < 1e-6f,
            "this is what pins the camera for the duration of a look drag");

        followArm.FollowTargetYaw = true;
        followPawn.Rotation = new Vector3(0.0f, 1.5f, 0.0f);
        followArm.Evaluate(FrameSeconds);

        // Compared the short way round: the boom wraps its yaw, so a raw subtraction against the
        // expected value reads a wrap as a whole extra turn.
        var followError = MathF.Abs(WrapDegrees((followArm.YawRadians - (pinnedArmYaw + 0.5f)).ToDegrees()));
        Check("with follow on, the boom copies the turn exactly once",
            followError < 0.01f, $"{followError:0.####} deg off a one-for-one copy");

        // ---------------------------------------------------------- strafing settles facing sideways
        // The controller's strafe case, frame for frame: drive and facing both along the camera's right,
        // boom pinned because a look drag is in progress.
        var (strafePawn, strafeArm) = CreateRig(0.0f);
        strafeArm.FollowTargetYaw = false;

        var strafeCameraForward = CameraForward(strafeArm);
        float lastStrafeTurn = 0.0f;
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var yawBefore = strafePawn.Rotation.Y;
            var strafeDirection = CameraRight(strafeArm);
            strafePawn.DriveDirection = strafeDirection;
            strafePawn.DesiredFacingDirection = strafeDirection;
            strafePawn.DesiredFacingIsInstant = false;

            StepFrame(strafePawn, strafeArm);
            lastStrafeTurn = MathF.Abs(strafePawn.Rotation.Y - yawBefore).ToDegrees();
        }

        Check("a held strafe stops turning instead of spinning",
            lastStrafeTurn < 0.01f, $"{lastStrafeTurn:0.####} deg on the final frame");

        Check("and settles looking along the strafe, not along the camera",
            MathF.Abs(AngleBetweenDegrees(strafePawn.FacingDirection, strafeCameraForward) - 90.0f) < 1.0f,
            $"{AngleBetweenDegrees(strafePawn.FacingDirection, strafeCameraForward):0.#} deg off the camera");

        Check("the camera stayed where the drag left it throughout",
            AngleBetweenDegrees(CameraForward(strafeArm), strafeCameraForward) < 0.01f,
            "a boom that followed the strafe round would never let the turn arrive");

        // ---------------------------------------------------------- an unforced drive never turns it
        // What makes a null DesiredFacingDirection safe, and so what a backpedal is built on: the drive
        // direction alone is not a reason to turn. Walking backward along the pawn's own facing is the
        // case that has to hold, because a heading measured off the facing it would be turning cannot be
        // converged on at all.
        var (backPawn, backArm) = CreateRig(0.0f);
        backArm.FollowTargetYaw = true;
        var backFacing = backPawn.FacingDirection;
        var backArmYaw = backArm.YawRadians;
        var worstBackTurn = 0.0f;

        for (var frame = 0; frame < FrameCount; frame++)
        {
            var yawBefore = backPawn.Rotation.Y;

            backPawn.DriveDirection = backPawn.FacingDirection * -1.0f;
            backPawn.DesiredFacingDirection = null;

            StepFrame(backPawn, backArm);
            worstBackTurn = MathF.Max(worstBackTurn, MathF.Abs(backPawn.Rotation.Y - yawBefore).ToDegrees());
        }

        Check("a drive heading with no facing asked for never turns the character",
            worstBackTurn < 1e-4f, $"worst frame {worstBackTurn:0.######} deg");

        Check("so the camera behind it never moves either",
            MathF.Abs(backArm.YawRadians - backArmYaw) < 1e-5f,
            $"{AngleBetweenDegrees(backPawn.FacingDirection, backFacing):0.#} deg of facing drift");

        Check("and it actually went backward", Vector3.Dot(backPawn.Position, backFacing) < -0.5f,
            $"{Vector3.Dot(backPawn.Position, backFacing):0.#} units along its own facing");

        // ---------------------------------------------------------- running at the camera turns round
        // With no look drag held, facing follows the direction of travel, so running toward the camera is
        // an about-face and then a run rather than a long backpedal.
        //
        // This is the 180-degree case, and the one worth a check: the character starts facing exactly
        // away from where it is asked to look, so the turn has two equally short ways round. It converges
        // only because the target is a fixed world direction taken from a camera that is not following --
        // the same reason the strafe above converges.
        var (aboutPawn, aboutArm) = CreateRig(0.0f);
        aboutArm.FollowTargetYaw = false;
        var aboutCameraForward = CameraForward(aboutArm);

        Check("the character starts facing exactly away from the camera",
            MathF.Abs(AngleBetweenDegrees(aboutPawn.FacingDirection, -aboutCameraForward) - 180.0f) < 0.01f,
            "so the turn below is the ambiguous half-turn, not an easy one");

        var lastAboutTurn = 0.0f;
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var yawBefore = aboutPawn.Rotation.Y;

            // What the controller sets for S with the look button up: drive and facing both toward the
            // camera, rebuilt from the camera every frame.
            var towardCamera = -CameraForward(aboutArm);
            aboutPawn.DriveDirection = towardCamera;
            aboutPawn.DesiredFacingDirection = towardCamera;
            aboutPawn.DesiredFacingIsInstant = false;

            StepFrame(aboutPawn, aboutArm);
            lastAboutTurn = MathF.Abs(WrapDegrees((aboutPawn.Rotation.Y - yawBefore).ToDegrees()));
        }

        Check("it turns round to face the camera it is running at",
            AngleBetweenDegrees(aboutPawn.FacingDirection, -aboutCameraForward) < 1.0f,
            $"{AngleBetweenDegrees(aboutPawn.FacingDirection, -aboutCameraForward):0.#} deg off");

        Check("and settles there rather than oscillating across the half-turn",
            lastAboutTurn < 0.01f, $"{lastAboutTurn:0.####} deg on the final frame");

        Check("with the camera still where it was left",
            AngleBetweenDegrees(CameraForward(aboutArm), aboutCameraForward) < 0.01f,
            "a camera that followed the about-face would never let it arrive");

        // ---------------------------------------------------------- a left-drag orbit leaves it alone
        // What separates the two drags: orbiting moves the view around a character that does not turn to
        // follow it, so you can look at it from any side. The controller expresses that by leaving
        // DesiredFacingDirection null, which is the case simulated here.
        var (orbitPawn, orbitArm) = CreateRig(0.0f);
        orbitArm.FollowTargetYaw = false;
        var orbitFacingBefore = orbitPawn.FacingDirection;

        for (var frame = 0; frame < FrameCount; frame++)
        {
            orbitPawn.DriveDirection = Vector3.Zero;
            orbitPawn.DesiredFacingDirection = null;

            // A steady drag all the way round the character.
            orbitArm.AddLookInput(0.02f, 0.0f);
            StepFrame(orbitPawn, orbitArm);
        }

        Check("orbiting all the way round never turns the character",
            AngleBetweenDegrees(orbitPawn.FacingDirection, orbitFacingBefore) < 0.01f,
            $"{AngleBetweenDegrees(orbitPawn.FacingDirection, orbitFacingBefore):0.###} deg of drift");

        Check("and the view really did move round it",
            AngleBetweenDegrees(CameraForward(orbitArm), CameraForward(strafeArm)) > 1.0f,
            "so the check above is not passing because nothing happened");

        // ---------------------------------------------------------- the coupling these checks rule out
        // Everything above with one thing changed: the boom also follows the pawn round, which is what
        // AddLookInput used to do implicitly by deriving its yaw from the pawn's rotation. If this
        // configuration settled too, the strafe checks above would prove nothing.
        var (spinPawn, spinArm) = CreateRig(0.0f);
        spinArm.FollowTargetYaw = true;

        var spinTotal = 0.0f;
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var yawBefore = spinArm.YawRadians;
            var strafeDirection = CameraRight(spinArm);
            spinPawn.DriveDirection = strafeDirection;
            spinPawn.DesiredFacingDirection = strafeDirection;
            spinPawn.DesiredFacingIsInstant = false;

            StepFrame(spinPawn, spinArm);
            spinTotal += MathF.Abs(WrapDegrees((spinArm.YawRadians - yawBefore).ToDegrees()));
        }

        Check("two-way coupling spins without end, which is the bug these checks catch",
            spinTotal > 720.0f, $"{spinTotal:0.} deg swept in {FrameCount} frames");
    }

    // The arm wraps its yaw, so a frame's delta has to be read the short way round or every wrap reads
    // as a full turn.
    private static float WrapDegrees(float degrees)
    {
        var wrapped = degrees % 360.0f;
        if (wrapped > 180.0f)
        {
            wrapped -= 360.0f;
        }
        else if (wrapped < -180.0f)
        {
            wrapped += 360.0f;
        }

        return wrapped;
    }
}
