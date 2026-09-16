using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Gameplay.Flight;

namespace ManaTests;

/// <summary>
/// <see cref="FlyingMountComponent"/>'s flight model -- the Dragonflight energy trade.
/// <para>
/// What makes this worth testing headlessly is that the whole feel rests on one equation rather than on
/// anything visual: <c>dv = -g * sin(pitch)</c>, plus drag and a flap. Whether diving actually buys speed,
/// whether level flight settles instead of decaying to nothing, and whether a stall digs itself out are all
/// numeric questions with right answers, and all of them are things you would otherwise only discover by
/// flying into a hill.
/// </para>
/// <para>
/// The last section is the one that matters most structurally: the mount takes its heading from the camera,
/// so it must never write the camera's yaw. That is the same one-way rule the on-foot controls live by (see
/// the pawn camera controls suite), and getting it wrong here would reintroduce the same runaway spin in a
/// place nobody would think to look for it.
/// </para>
/// </summary>
public static class FlyingMountChecks
{
    private const float FrameSeconds = 1.0f / 60.0f;

    private static ManaContext CreateContext()
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };

        // Initialize() wants a real graphics context, so the one scene the mount needs to be built into is
        // stood up by hand.
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        return context;
    }

    private static (ManaPawnNode Pawn, ManaSpringArmComponent Arm, FlyingMountComponent Mount) CreateRider()
    {
        var context = CreateContext();
        var pawn = new ManaPawnNode(context, Matrix4x4.Identity)
        {
            SkinningEnabled = false,

            // A headless world has no floor, so a long dive falls forever. Left at its default the mount
            // would cross it and hand the pawn back mid-test, which reads as flight having stopped working.
            // The rule itself is checked on its own below.
            FallResetHeight = float.NegativeInfinity
        };

        context.World.DefaultScene.Children.Add(pawn);

        var arm = new ManaSpringArmComponent(pawn)
        {
            // A headless context has no physics world to probe against.
            ProbeForCollision = false
        };

        pawn.Components.Add(arm);

        var mount = new FlyingMountComponent(pawn);
        pawn.Components.Add(mount);

        return (pawn, arm, mount);
    }

    /// <summary>
    /// Flies one frame. The component reads the boom for its heading, so setting the boom's pitch is how a
    /// test says "the player is looking down".
    /// </summary>
    /// <summary>
    /// What happened across a stretch of flight, rather than what was true at the end of it. Stalls and
    /// recoveries are passing states -- the mount enters a stall, drops its nose, recovers, pulls up and
    /// stalls again -- so anything sampled once at the end of a long climb catches an arbitrary point in
    /// that cycle and reads as a flake.
    /// </summary>
    private readonly record struct FlightTrace(bool EverStalled, float PeakSpeed, float LowestSpeed);

    private static FlightTrace Fly(ManaPawnNode pawn, ManaSpringArmComponent arm, float pitchDegrees, int frames)
    {
        var mount = pawn.Components.OfType<FlyingMountComponent>().First();
        var everStalled = false;
        var peak = 0.0f;
        var lowest = float.MaxValue;

        for (var frame = 0; frame < frames; frame++)
        {
            ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(FrameSeconds);

            // Re-asserted every frame: the component eases its own pitch toward this, and a real camera
            // would be holding the look steady.
            arm.PitchRadians = pitchDegrees.ToRadians();
            pawn.UpdateTransform();

            everStalled |= mount.IsStalling;
            peak = MathF.Max(peak, mount.Speed);
            lowest = MathF.Min(lowest, mount.Speed);
        }

        return new FlightTrace(everStalled, peak, lowest == float.MaxValue ? 0.0f : lowest);
    }

    /// <summary>
    /// Charges gained in one second of flight held at <paramref name="pitchDegrees"/>, after long enough at
    /// that pitch for the speed to settle into whichever regime it implies. The bar is raised out of the way
    /// so the figure is the regen rate itself rather than a clamp.
    /// </summary>
    private static float MeasureVigorGain(float pitchDegrees)
    {
        var (pawn, arm, mount) = CreateRider();
        mount.Mount();

        // After Mount, which fills the bar to whatever MaxCharges was at the time.
        mount.MaxCharges = 1000.0f;

        Fly(pawn, arm, pitchDegrees, 240);
        var before = mount.Charges;
        Fly(pawn, arm, pitchDegrees, 60);
        return mount.Charges - before;
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- mounting takes the pawn over
        var (pawn, arm, mount) = CreateRider();

        Check("a fresh rider is on foot", !mount.IsMounted && pawn.LocomotionEnabled);

        var armLengthBefore = arm.ArmLength;
        mount.Mount();

        Check("mounting puts a vehicle in the scene",
            pawn.Context.World.DefaultScene.Children.Any(node => node.Name == "Flying Mount"),
            $"{pawn.Context.World.DefaultScene.Children.Count} scene children");

        Check("and takes locomotion off the pawn", !pawn.LocomotionEnabled,
            "or the walk step would pin it back to the floor every frame");

        Check("it starts at flying speed rather than in a stall",
            mount.Speed >= mount.StallSpeed, $"{mount.Speed:0.#} u/s");

        Check("the boom pulls back for something this size",
            arm.ArmLength > armLengthBefore, $"{armLengthBefore:0.#} -> {arm.ArmLength:0.#}");

        // ---------------------------------------------------------------- the nose points where it goes
        // The rig faces +Z but Rotation.X pitches a +Z-facing node nose-DOWN for positive values, while the
        // camera pitch this is driven from is negative for looking down. Handing one straight to the other
        // pointed the mount's nose skyward while it dived -- correct motion, inverted model. Checked as the
        // sign of the nose against the sign of the travel, since that is the thing that has to agree.
        foreach (var lookDegrees in new[] { -45.0f, 45.0f })
        {
            var (nosePawn, noseArm, noseMount) = CreateRider();
            noseMount.Mount();

            var heightAtStart = nosePawn.Position.Y;

            // Half a second: long enough for the pitch to reach the look, short enough that a climb has not
            // yet spent its speed and stalled, which would drop the nose again and make this measure the
            // recovery instead of the sign.
            Fly(nosePawn, noseArm, lookDegrees, 30);

            var nose = Vector3.Transform(Vector3.UnitZ, nosePawn.Rotation.ToQuaternionFromEuler());
            var travelled = nosePawn.Position.Y - heightAtStart;
            var looking = lookDegrees < 0.0f ? "down" : "up";

            Check($"looking {looking} sends the mount {looking}",
                lookDegrees < 0.0f ? travelled < -2.0f : travelled > 2.0f,
                $"{travelled:0.#} units of altitude");

            Check($"and points its nose {looking} too, not the other way",
                lookDegrees < 0.0f ? nose.Y < -0.3f : nose.Y > 0.3f,
                $"nose Y {nose.Y:0.##}");
        }

        // ---------------------------------------------------------------- the energy trade
        // The whole flight model in two checks: down buys speed, up spends it.
        var (divePawn, diveArm, diveMount) = CreateRider();
        diveMount.Mount();
        var diveStartSpeed = diveMount.Speed;
        var diveStartHeight = divePawn.Position.Y;
        Fly(divePawn, diveArm, -55.0f, 90);

        Check("diving trades altitude for speed",
            diveMount.Speed > diveStartSpeed + 10.0f && divePawn.Position.Y < diveStartHeight,
            $"{diveStartSpeed:0.#} -> {diveMount.Speed:0.#} u/s, fell {diveStartHeight - divePawn.Position.Y:0.#}");

        // Straight from the dive into a climb, so the speed it spends is the speed the dive just bought.
        var speedEnteringClimb = diveMount.Speed;
        var heightEnteringClimb = divePawn.Position.Y;
        Fly(divePawn, diveArm, 55.0f, 90);

        Check("and climbing spends it back for altitude",
            diveMount.Speed < speedEnteringClimb && divePawn.Position.Y > heightEnteringClimb,
            $"{speedEnteringClimb:0.#} -> {diveMount.Speed:0.#} u/s, climbed {divePawn.Position.Y - heightEnteringClimb:0.#}");

        // ---------------------------------------------------------------- level flight is sustainable
        var (levelPawn, levelArm, levelMount) = CreateRider();
        levelMount.Mount();
        Fly(levelPawn, levelArm, 0.0f, 600);
        var settled = levelMount.Speed;
        Fly(levelPawn, levelArm, 0.0f, 120);

        Check("level flight settles at a steady cruise",
            MathF.Abs(levelMount.Speed - settled) < 0.5f,
            $"{settled:0.#} -> {levelMount.Speed:0.#} u/s");

        Check("which is above a stall, so it does not sink out of the sky",
            levelMount.Speed > levelMount.StallSpeed * 1.5f,
            $"{levelMount.Speed:0.#} u/s against a {levelMount.StallSpeed:0.#} stall");

        Check("and below what a dive can reach, so diving is still worth it",
            levelMount.Speed < levelMount.MaxSpeed * 0.6f,
            $"cruise {levelMount.Speed:0.#} of a {levelMount.MaxSpeed:0.#} maximum");

        // ---------------------------------------------------------------- the cap holds
        var (capPawn, capArm, capMount) = CreateRider();
        capMount.Mount();
        Fly(capPawn, capArm, -85.0f, 1200);

        Check("a sustained dive never exceeds the speed cap",
            capMount.Speed <= capMount.MaxSpeed + 0.01f,
            $"{capMount.Speed:0.#} of {capMount.MaxSpeed:0.#}");

        Check("but does reach it", capMount.Speed > capMount.MaxSpeed * 0.9f, $"{capMount.Speed:0.#} u/s");

        // ---------------------------------------------------------------- a stall digs itself out
        // Entered by climbing until the speed is gone, which is how a player finds one.
        var (stallPawn, stallArm, stallMount) = CreateRider();
        stallMount.Mount();
        var climbTrace = Fly(stallPawn, stallArm, 80.0f, 240);

        Check("climbing until the speed is gone stalls", climbTrace.EverStalled,
            $"dropped to {climbTrace.LowestSpeed:0.#} u/s");

        var stallHeight = stallPawn.Position.Y;

        // The camera is still asking for a climb: recovery has to come from the model dropping the nose
        // itself, not from the player knowing to let go.
        var recoveryTrace = Fly(stallPawn, stallArm, 80.0f, 240);

        Check("and it recovers on its own with the player still pulling up",
            recoveryTrace.PeakSpeed > stallMount.StallSpeed * stallMount.StallRecoveryFactor,
            $"got back to {recoveryTrace.PeakSpeed:0.#} u/s from a {stallMount.StallSpeed:0.#} stall");

        // What the stall is for: pitch alone must not be a way to gain height indefinitely, or the energy
        // trade has no teeth and vigor is pointless. Four seconds of holding a hard climb should be worth
        // almost nothing -- far less than the single boost measured below.
        var heldClimbGain = stallPawn.Position.Y - stallHeight;

        Check("holding a climb is not a free elevator -- it porpoises instead of ascending",
            heldClimbGain < 10.0f,
            $"{heldClimbGain:0.#} units in four seconds of full pull-up");

        // ---------------------------------------------------------------- vigor
        var (vigorPawn, vigorArm, vigorMount) = CreateRider();
        vigorMount.Mount();

        Check("mounting starts with a full bar", vigorMount.Charges >= vigorMount.MaxCharges,
            $"{vigorMount.Charges:0.##} of {vigorMount.MaxCharges:0.#}");

        Fly(vigorPawn, vigorArm, 0.0f, 120);

        Check("and the bar never overfills", vigorMount.Charges <= vigorMount.MaxCharges + 0.001f,
            $"{vigorMount.Charges:0.##}");

        var chargesBeforeBoost = vigorMount.Charges;
        var speedBeforeBoost = vigorMount.Speed;

        Check("a boost is available on a full bar", vigorMount.TryBoost());

        Check("and costs exactly one charge",
            MathF.Abs((chargesBeforeBoost - vigorMount.Charges) - 1.0f) < 1e-4f,
            $"{chargesBeforeBoost:0.##} -> {vigorMount.Charges:0.##}");

        Check("while adding speed", vigorMount.Speed > speedBeforeBoost,
            $"{speedBeforeBoost:0.#} -> {vigorMount.Speed:0.#} u/s");

        // Drained, then asked for one more, so the bar's floor is checked too.
        while (vigorMount.TryBoost())
        {
        }

        Check("an empty bar refuses", !vigorMount.TryBoost() && vigorMount.Charges < 1.0f,
            $"{vigorMount.Charges:0.##} left");

        Check("and refuses a dash as well, off the same bar", !vigorMount.TrySurge(),
            "both abilities spend the one pool");

        // ---------------------------------------------------------------- the dash
        // Surge Forward. The distinction from a boost is that it spends itself along the nose rather than
        // upward, so the two are a choice: pointed level it is pure ground covered.
        var (dashPawn, dashArm, dashMount) = CreateRider();
        dashMount.Mount();
        Fly(dashPawn, dashArm, 0.0f, 120);

        var dashSpeedBefore = dashMount.Speed;
        var dashChargesBefore = dashMount.Charges;
        var dashHeightBefore = dashPawn.Position.Y;

        Check("a dash is available", dashMount.TrySurge());

        Check("and adds a lot of speed at once",
            dashMount.Speed > dashSpeedBefore + 20.0f,
            $"{dashSpeedBefore:0.#} -> {dashMount.Speed:0.#} u/s");

        Check("for one charge", MathF.Abs((dashChargesBefore - dashMount.Charges) - 1.0f) < 1e-4f,
            $"{dashChargesBefore:0.##} -> {dashMount.Charges:0.##}");

        var dashDistanceStart = new Vector3(dashPawn.Position.X, 0.0f, dashPawn.Position.Z);
        Fly(dashPawn, dashArm, 0.0f, 30);
        var dashDistance = Vector3.Distance(new Vector3(dashPawn.Position.X, 0.0f, dashPawn.Position.Z), dashDistanceStart);

        Check("spending itself on ground rather than height, unlike a boost",
            MathF.Abs(dashPawn.Position.Y - dashHeightBefore) < 2.0f && dashDistance > 20.0f,
            $"{dashDistance:0.#} units forward, {dashPawn.Position.Y - dashHeightBefore:0.#} up");

        // ---------------------------------------------------------------- a boost actually goes up
        // The complaint this was written for: Space only climbed when you were already looking up, because
        // the lift was vertical but the speed it added followed the nose. Checked against a level look and
        // a downward one, since those are the cases that used to spend the charge going forwards.
        foreach (var lookDegrees in new[] { 0.0f, -35.0f })
        {
            var (boostPawn, boostArm, boostMount) = CreateRider();
            boostMount.Mount();
            Fly(boostPawn, boostArm, lookDegrees, 120);

            var heightBefore = boostPawn.Position.Y;
            Fly(boostPawn, boostArm, lookDegrees, 90);
            var heightWithout = boostPawn.Position.Y - heightBefore;

            var (liftPawn, liftArm, liftMount) = CreateRider();
            liftMount.Mount();
            Fly(liftPawn, liftArm, lookDegrees, 120);

            var liftBefore = liftPawn.Position.Y;
            liftMount.TryBoost();
            Fly(liftPawn, liftArm, lookDegrees, 90);
            var heightWithBoost = liftPawn.Position.Y - liftBefore;

            Check($"a boost climbs while looking {(lookDegrees < 0.0f ? "down" : "level")}",
                heightWithBoost > 8.0f,
                $"gained {heightWithBoost:0.#} units in 1.5s");

            Check($"which is far more than not boosting at all, looking {(lookDegrees < 0.0f ? "down" : "level")}",
                heightWithBoost > heightWithout + 8.0f,
                $"{heightWithBoost:0.#} against {heightWithout:0.#} units");
        }

        // Thrill of the Skies: the same second of flight refills faster when genuinely moving.
        //
        // The bar is uncapped for the measurement rather than drained with boosts, for two reasons: a full
        // bar clamps and reports no gain at all, and every boost dumps its speed gain into the very thing
        // being varied -- draining three charges adds nearly 50 u/s, which would have carried the slow case
        // up past the thrill threshold and measured the fast rate twice.
        var slowGain = MeasureVigorGain(80.0f);
        var fastGain = MeasureVigorGain(-85.0f);

        Check("flying fast refills vigor faster than crawling does",
            fastGain > slowGain * 1.5f,
            $"{fastGain:0.###} against {slowGain:0.###} charges per second");

        // ---------------------------------------------------------------- falling out of the world
        // With no ground under it a dive has nothing to land on, so the pawn's own reset height is what
        // catches it -- handing control back rather than gliding into the void forever. Checked explicitly
        // because the shared rider above disables it to be able to dive at all.
        var (voidPawn, voidArm, voidMount) = CreateRider();
        voidPawn.FallResetHeight = -40.0f;
        voidMount.Mount();
        Fly(voidPawn, voidArm, -85.0f, 240);

        Check("diving past the world's floor with nothing to land on gives the pawn back",
            !voidMount.IsMounted && voidPawn.LocomotionEnabled,
            $"dismounted below {voidPawn.FallResetHeight:0.#}");

        // ---------------------------------------------------------------- the camera is never followed
        // The rule the on-foot controls live by, applied here: the mount reads the boom for its heading, so
        // if it also wrote the boom the two would chase each other into a spin.
        var (couplePawn, coupleArm, coupleMount) = CreateRider();
        coupleMount.Mount();
        var cameraYawBefore = coupleArm.YawRadians;

        coupleArm.FollowTargetYaw = true;
        Fly(couplePawn, coupleArm, -40.0f, 240);

        Check("flying never moves the camera's yaw, even with follow left on",
            MathF.Abs(coupleArm.YawRadians - cameraYawBefore) < 1e-4f,
            "the mount takes its heading from the boom, so it must not write it back");

        // ---------------------------------------------------------------- dismount puts it all back
        var (backPawn, backArm, backMount) = CreateRider();
        var backArmLength = backArm.ArmLength;
        backMount.Mount();
        Fly(backPawn, backArm, -30.0f, 60);
        backMount.Dismount();

        Check("dismounting hands the pawn back its own legs",
            backPawn.LocomotionEnabled && !backMount.IsMounted);

        Check("takes the vehicle out of the scene",
            !backPawn.Context.World.DefaultScene.Children.Any(node => node.Name == "Flying Mount"));

        Check("restores the boom", MathF.Abs(backArm.ArmLength - backArmLength) < 0.01f,
            $"{backArm.ArmLength:0.#} back to {backArmLength:0.#}");

        Check("and leaves the character standing upright, not lying where the dive left it",
            MathF.Abs(backPawn.Rotation.X) < 1e-6f && MathF.Abs(backPawn.Rotation.Z) < 1e-6f,
            $"pitch {backPawn.Rotation.X.ToDegrees():0.#}, roll {backPawn.Rotation.Z.ToDegrees():0.#} deg");
    }
}
