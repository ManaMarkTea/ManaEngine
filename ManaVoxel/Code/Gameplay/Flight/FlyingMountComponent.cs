using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Silk.NET.Input;

namespace ManaVoxel.Code.Gameplay.Flight
{
    /// <summary>
    /// Jump, then jump again in mid-air, and a flying mount catches you -- Dragonflight-style. Attach to a
    /// <see cref="ManaPawnNode"/>; it does nothing until that second jump.
    /// <para>
    /// That gesture is free, which is why it is the one used: with <see cref="ManaPawnNode.MaxJumpCount"/>
    /// at its default of 1, a second jump press while airborne already does nothing whatsoever. So Space
    /// means "up" the whole way through -- it jumps, then it takes off, then it is Skyward Ascent -- and
    /// nothing had to be unbound to make room for it.
    /// </para>
    /// <para>
    /// <b>The flight model is an energy trade, not a throttle.</b> There is no "fly faster" key. Pitching
    /// down converts altitude into speed and pitching up spends speed to climb, exactly as gravity along
    /// the direction of travel would: <c>dv = -g * sin(pitch)</c>. That single line is what makes diving to
    /// build speed and then pulling up to convert it into height the whole game, rather than a detail. Drag
    /// bleeds it off at the top end, and a gentle flap keeps level flight from decaying to nothing, so
    /// level cruise settles at a speed well under what a dive can reach.
    /// </para>
    /// <para>
    /// <b>Steering is the camera.</b> Heading comes from the boom's yaw and pitch, so the existing look
    /// controls fly the mount with nothing new bound, and -- the part that matters -- the mount is never
    /// the thing the camera is derived from. The coupling stays one-way, the same rule the on-foot controls
    /// live by (ManaSpringArmComponent.FollowTargetYaw): the camera leads, the ridden thing follows.
    /// </para>
    /// <para>
    /// While mounted the pawn's <see cref="ManaPawnNode.LocomotionEnabled"/> is off and its capsule is
    /// gone, so this component is the only thing writing its transform. Both are restored on dismount, and
    /// dismounting in mid-air drops you -- which is survivable, because the pawn gets its capsule back
    /// before it starts falling.
    /// </para>
    /// </summary>
    public sealed class FlyingMountComponent : ManaComponent
    {
        public FlyingMountComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Flying Mount";
        }

        /// <summary>
        /// Written before the scene graph builds its matrices, because this drives its owner's transform
        /// and a write afterwards would not be seen until the next frame.
        /// </summary>
        public override bool UpdatesBeforeNodeTransform => true;

        // ---- Flight tuning ---------------------------------------------------------------------------
        // Units are roughly WoW yards per second (a character is 2 units tall), so these read against
        // Dragonflight's own numbers: it cruises near 30 and tops out around 90 in a dive.

        /// <summary>
        /// Gravity for the altitude-into-speed trade. Deliberately not the world's own gravity, which is
        /// tuned for a snappy platformer jump (-64) and would slam the mount from stall to terminal in
        /// about a second.
        /// </summary>
        /// <para>
        /// Sized for drama rather than realism: at a 60-degree dive this is nearly 40 units per second per
        /// second, which takes a cruise into a full-speed dive in about two seconds and bleeds it back out
        /// just as fast on the way up. A gentler value makes the trade technically present but far too
        /// subtle to feel, which is the whole point of it.
        /// </para>
        public float GravityAcceleration { get; set; } = 45.0f;

        /// <summary>Linear, not quadratic: the speed cap is explicit, so drag only has to shape the curve.</summary>
        public float DragCoefficient { get; set; } = 0.09f;

        /// <summary>
        /// A gentle flap, strongest from a standstill and fading to nothing at <see cref="CruiseSpeed"/>.
        /// Without it level flight decays to a stall, and managing energy stops being a choice.
        /// </summary>
        public float FlapAcceleration { get; set; } = 20.0f;

        /// <summary>
        /// What the flap pulls toward, not where level flight settles -- drag takes its cut too, so the
        /// actual equilibrium lands somewhat below this.
        /// <para>
        /// Set high enough that level flight banks real energy, because with gravity this punchy a climb
        /// spends it fast: cruise is how long you get to hold a pull-up before the wings give out, so a low
        /// cruise makes looking up stall almost at once rather than trading for height.
        /// </para>
        /// </summary>
        public float CruiseSpeed { get; set; } = 38.0f;

        public float MaxSpeed { get; set; } = 90.0f;

        /// <summary>Below this the wings stop carrying and the nose drops -- see <see cref="IsStalling"/>.</summary>
        public float StallSpeed { get; set; } = 9.0f;
        public float StallSinkRate { get; set; } = 18.0f;

        /// <summary>
        /// How far past <see cref="StallSpeed"/> a recovery has to get before the mount flies again, as a
        /// multiple of it. Above 1 deliberately: recovering at exactly the stall speed leaves the two
        /// conditions touching, and a player still asking for a climb then gets a flutter -- the nose drops,
        /// clears the threshold by a whisker, snaps back up to the camera, and stalls again within a frame.
        /// The gap makes the recovery commit to building real speed before handing control back.
        /// </summary>
        public float StallRecoveryFactor { get; set; } = 1.7f;

        public float MinPitchDegrees { get; set; } = -85.0f;
        public float MaxPitchDegrees { get; set; } = 75.0f;

        /// <summary>How fast the mount's own pitch chases where the camera is looking.</summary>
        public float PitchRateDegreesPerSecond { get; set; } = 150.0f;

        public float MaxBankDegrees { get; set; } = 45.0f;
        public float BankRateDegreesPerSecond { get; set; } = 140.0f;

        // ---- Vigor ------------------------------------------------------------------------------------

        /// <summary>Boost charges. Fractional while regenerating; a boost costs exactly one.</summary>
        public float MaxCharges { get; set; } = 3.0f;
        public float ChargeRegenPerSecond { get; set; } = 0.22f;

        /// <summary>
        /// Dragonflight's Thrill of the Skies: charges come back far faster while genuinely moving, which
        /// is what rewards staying fast instead of hovering.
        /// </summary>
        public float ThrillSpeed { get; set; } = 40.0f;
        public float ThrillRegenPerSecond { get; set; } = 0.75f;

        /// <summary>Skyward Ascent: a burst of lift and speed for one charge.</summary>
        public float BoostLift { get; set; } = 30.0f;
        public float BoostSpeedGain { get; set; } = 16.0f;

        /// <summary>
        /// How quickly the boost's lift bleeds away. Gentle on purpose: the altitude a boost is worth is
        /// roughly <see cref="BoostLift"/> divided by this, so a fast decay turns a launch into a hop.
        /// </summary>
        public float BoostLiftDecayPerSecond { get; set; } = 1.5f;

        /// <summary>
        /// A boost rears the nose up this far and holds it there for <see cref="BoostClimbSeconds"/>,
        /// overriding where the camera is looking.
        /// <para>
        /// Without this, Space only goes up when you are already looking up: the lift is vertical but the
        /// speed gain follows the nose, so boosting on a level or downward look spent most of the charge
        /// going forwards. Pointing the nose up for a moment puts both behind the same direction, which is
        /// what makes it read as a launch.
        /// </para>
        /// </summary>
        public float BoostClimbDegrees { get; set; } = 55.0f;
        public float BoostClimbSeconds { get; set; } = 0.55f;

        /// <summary>
        /// Surge Forward: one charge for a shove along the nose, with no lift and no pitch of its own. The
        /// other half of the vigor pair -- Skyward Ascent buys height, this buys ground.
        /// </summary>
        public float SurgeSpeedGain { get; set; } = 32.0f;

        // ---- Mounting ---------------------------------------------------------------------------------

        /// <summary>Clearance kept above whatever the ground probe finds.</summary>
        public float GroundClearance { get; set; } = 1.6f;

        /// <summary>
        /// Speed the mount starts with. Near the cruise equilibrium rather than merely above the stall, so a
        /// takeoff can be turned straight into a climb instead of needing a dive first to become flyable.
        /// </summary>
        public float TakeoffSpeed { get; set; } = 30.0f;

        /// <summary>How much clear air a takeoff gives you, so the first frame is not a landing.</summary>
        public float TakeoffLift { get; set; } = 3.0f;

        // ---- State ------------------------------------------------------------------------------------

        public bool IsMounted { get; private set; }
        public float Speed { get; private set; }
        public float Charges { get; private set; }
        public bool IsStalling { get; private set; }

        /// <summary>Raised on mount and dismount, so a HUD can appear and the vigor readout can bind.</summary>
        public event Action<bool>? MountStateChanged;

        private FlyingMountModel? model;

        /// <summary>
        /// Where the mount is, owned here rather than read back off the node each frame. The node's position
        /// is the saddle -- the flight position plus an offset -- so integrating from it re-applied that
        /// offset every frame and sank the mount at a steady twelve units a second that no term in the
        /// flight model accounted for.
        /// </summary>
        private Vector3 flightPosition;

        private float pitchRadians;
        private float bankRadians;
        private float boostLift;
        private float boostClimbTimer;
        private bool jumpKeyWasDown;
        private bool surgeKeyWasDown;
        private bool surgeRightKeyWasDown;
        private float wingPhase;

        private float savedArmLength;
        private bool hadPhysics;

        private ManaPawnNode Pawn => (ManaPawnNode)Owner;

        public override void UpdateComponent()
        {
            var deltaSeconds = (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
            if (deltaSeconds <= 0.0f)
            {
                return;
            }

            // Space means "up" in whichever state we are in: a second airborne jump takes off, and once
            // flying it is Skyward Ascent. Shift is the sprint key on foot, so it is the dash in the air.
            var jumpPressed = ReadPressed(Key.Space, ref jumpKeyWasDown);
            var surgePressed = ReadPressed(Key.ShiftLeft, ref surgeKeyWasDown)
                | ReadPressed(Key.ShiftRight, ref surgeRightKeyWasDown);

            if (IsMounted)
            {
                if (jumpPressed)
                {
                    TryBoost();
                }

                if (surgePressed)
                {
                    TrySurge();
                }

                UpdateFlight(deltaSeconds);
            }
            else if (jumpPressed && CanTakeOff())
            {
                Mount();
            }
        }

        /// <summary>
        /// Edge-detects one key against its own remembered state. Callers must keep a separate field per key
        /// and must call this every frame for each of them -- a key only read some frames latches.
        /// </summary>
        private bool ReadPressed(Key key, ref bool wasDown)
        {
            var keyboardFree = Context.GameMode?.UiWantsKeyboard != true;
            var down = keyboardFree && Context.Input?.Keyboards.FirstOrDefault()?.IsKeyPressed(key) == true;

            var pressed = down && !wasDown;
            wasDown = down;
            return pressed;
        }

        /// <summary>
        /// Whether a jump press should become a takeoff: only in mid-air, so the first jump off the ground
        /// stays a jump and the second one -- which a single-jump pawn otherwise wastes -- is the one that
        /// flies. A pawn with no capsule reports itself permanently grounded, so it never takes off this way
        /// and has to be mounted by calling <see cref="Mount"/>.
        /// </summary>
        private bool CanTakeOff()
        {
            return Pawn.Body != null && !Pawn.IsGrounded;
        }

        public override void EndPlay()
        {
            // Leaving play with a mount out would strand the pawn with no capsule and no locomotion.
            if (IsMounted)
            {
                Dismount();
            }
        }

        // ---- Mount lifecycle --------------------------------------------------------------------------

        public void Mount()
        {
            if (IsMounted)
            {
                return;
            }

            var pawn = Pawn;
            var springArm = FindSpringArm();

            var riderHeight = pawn.TryGetBindPoseHeight(out var height) && height > 0.0f
                ? height * MathF.Abs(pawn.Scale.Y)
                : 2.0f;

            model = FlyingMountModel.Build(Context, Context.World.DefaultScene, riderHeight);
            Context.World.DefaultScene.Children.Add(model.Root);

            // The capsule goes rather than being told to ignore gravity: a kinematic body dragged through
            // the world by a transform write still resolves contacts, so it would shove everything it flew
            // past. It comes back on dismount, before the fall starts.
            hadPhysics = pawn.Body != null;
            pawn.DisablePhysics();
            pawn.LocomotionEnabled = false;

            // The boom's probe must never be left holding a capsule that no longer exists -- see the
            // matching refresh in Dismount, which is where getting this wrong actually bites.
            if (springArm != null)
            {
                springArm.IgnoreBody = null;
            }

            // Started at speed and slightly clear of the ground, or the first frame would read as a stalled
            // landing and dismount immediately.
            Speed = TakeoffSpeed;
            pitchRadians = 0.0f;
            bankRadians = 0.0f;
            boostLift = 0.0f;
            boostClimbTimer = 0.0f;
            IsStalling = false;
            Charges = MaxCharges;

            var position = pawn.Position + (Vector3.UnitY * TakeoffLift);

            if (springArm != null)
            {
                savedArmLength = springArm.ArmLength;

                // Pulled back, because the mount is several times the character's size and a boom framed
                // for a person puts the viewer inside the wings.
                springArm.ArmLength = MathF.Min(savedArmLength * 2.2f, springArm.MaxArmLength);
                springArm.SnapToTarget();
            }

            ApplyTransforms(position);

            IsMounted = true;
            MountStateChanged?.Invoke(true);
        }

        /// <summary>
        /// Skyward Ascent: spends one charge for a burst of lift and speed. Returns false if there is
        /// nothing in the bar or nothing is being ridden.
        /// <para>
        /// Public because the key that fires it is just one caller -- a HUD button or a gamepad trigger is
        /// as entitled to it, and it makes the cost testable without synthesising a keypress.
        /// </para>
        /// </summary>
        public bool TryBoost()
        {
            if (!IsMounted || Charges < 1.0f)
            {
                return false;
            }

            Charges -= 1.0f;
            Speed = MathF.Min(Speed + BoostSpeedGain, MaxSpeed);
            boostLift = BoostLift;

            // Nose up for a moment, so the speed this just added is spent climbing rather than wherever the
            // camera happened to be pointing. See BoostClimbDegrees.
            boostClimbTimer = BoostClimbSeconds;
            return true;
        }

        /// <summary>
        /// Surge Forward: spends one charge to shove the mount along its nose. Returns false on an empty bar
        /// or on foot.
        /// <para>
        /// Deliberately does not touch the pitch, unlike <see cref="TryBoost"/>. That is what makes the two
        /// abilities a choice rather than a strictly better one: a surge going down converts straight into a
        /// screaming dive, and a surge going up spends itself climbing.
        /// </para>
        /// </summary>
        public bool TrySurge()
        {
            if (!IsMounted || Charges < 1.0f)
            {
                return false;
            }

            Charges -= 1.0f;
            Speed = MathF.Min(Speed + SurgeSpeedGain, MaxSpeed);
            return true;
        }

        public void Dismount()
        {
            if (!IsMounted)
            {
                return;
            }

            IsMounted = false;

            if (model != null)
            {
                Context.World.DefaultScene.Children.Remove(model.Root);
                model = null;
            }

            var pawn = Pawn;

            // Levelled before handing the pawn back: it keeps whatever rotation was last written, and a
            // character left pitched over from a dive would walk around lying on its side.
            pawn.Rotation = new Vector3(0.0f, pawn.Rotation.Y, 0.0f);
            pawn.LocomotionEnabled = true;

            if (hadPhysics)
            {
                pawn.EnablePhysics();
            }

            var springArm = FindSpringArm();
            if (springArm != null && savedArmLength > 0.0f)
            {
                springArm.ArmLength = savedArmLength;
                springArm.SnapToTarget();
            }

            Speed = 0.0f;
            boostLift = 0.0f;
            IsStalling = false;

            MountStateChanged?.Invoke(false);
        }

        // ---- Flight -----------------------------------------------------------------------------------

        private void UpdateFlight(float deltaSeconds)
        {
            var springArm = FindSpringArm();

            // Heading is the camera's. The boom's yaw is a camera yaw, and a camera looks down its own -Z
            // while this rig faces +Z, so the two are half a turn apart -- the same conversion the walking
            // controls make.
            var yawRadians = (springArm?.YawRadians ?? Owner.Rotation.Y) + MathF.PI;

            var targetPitch = Math.Clamp(
                springArm?.PitchRadians ?? 0.0f,
                MinPitchDegrees.ToRadians(),
                MaxPitchDegrees.ToRadians());

            // Hysteresis, not a single threshold -- see StallRecoveryFactor for why the two edges have to
            // sit apart.
            IsStalling = IsStalling
                ? Speed < StallSpeed * StallRecoveryFactor
                : Speed < StallSpeed;

            boostClimbTimer = MathF.Max(boostClimbTimer - deltaSeconds, 0.0f);

            if (IsStalling)
            {
                // Nose drops of its own accord, which is what lets a stall recover: falling buys back the
                // speed the wings need, so it resolves itself rather than needing a rescue key.
                targetPitch = MinPitchDegrees.ToRadians() * 0.5f;
            }
            else if (boostClimbTimer > 0.0f)
            {
                // A boost owns the nose briefly, beating the camera. Below the stall it does not, because a
                // stall that could be held nose-up by mashing Space would never recover.
                targetPitch = MathF.Max(targetPitch, BoostClimbDegrees.ToRadians());
            }

            pitchRadians = MoveToward(pitchRadians, targetPitch, PitchRateDegreesPerSecond.ToRadians() * deltaSeconds);

            // The trade. Everything else here is shaping; this line is the flight model.
            var gravityTerm = -GravityAcceleration * MathF.Sin(pitchRadians);
            var dragTerm = -DragCoefficient * Speed;
            var flapTerm = FlapAcceleration * MathF.Max(0.0f, 1.0f - (Speed / MathF.Max(CruiseSpeed, 0.01f)));

            Speed = Math.Clamp(Speed + ((gravityTerm + dragTerm + flapTerm) * deltaSeconds), 0.0f, MaxSpeed);

            Charges = MathF.Min(
                MaxCharges,
                Charges + ((Speed >= ThrillSpeed ? ThrillRegenPerSecond : ChargeRegenPerSecond) * deltaSeconds));

            boostLift = boostLift > 0.0f
                ? boostLift * MathF.Exp(-BoostLiftDecayPerSecond * deltaSeconds)
                : 0.0f;

            var forward = DirectionFrom(yawRadians, pitchRadians);
            var velocity = forward * Speed;
            velocity.Y += boostLift;

            if (IsStalling)
            {
                // Sink grows as speed falls away, so hanging at zero drops like a stone rather than
                // hovering there. Clamped because the recovery band reaches above StallSpeed, where this
                // fraction goes negative and would otherwise turn the sink into lift.
                var lostLift = Math.Clamp(1.0f - (Speed / MathF.Max(StallSpeed, 0.01f)), 0.0f, 1.0f);
                velocity.Y -= StallSinkRate * lostLift;
            }

            var position = flightPosition + (velocity * deltaSeconds);

            // Bank into the turn, from how fast the heading is actually changing. Presentation only -- it
            // never feeds back into where the mount goes.
            var yawRate = WrapRadians(yawRadians - (Owner.Rotation.Y)) / deltaSeconds;
            var targetBank = Math.Clamp(-yawRate * 0.35f, -MaxBankDegrees.ToRadians(), MaxBankDegrees.ToRadians());
            bankRadians = MoveToward(bankRadians, targetBank, BankRateDegreesPerSecond.ToRadians() * deltaSeconds);

            // Touching down is landing, at any speed. Skimming the floor at pace used to only clamp the
            // height, which meant scraping along the ground still counted as flying.
            if (TryResolveGround(position, out var groundHeight) && position.Y <= groundHeight + GroundClearance)
            {
                position.Y = groundHeight + GroundClearance;
                ApplyTransforms(position);
                Dismount();
                return;
            }

            // Nothing under us and falling out of the world: hand the pawn back so its own respawn can
            // catch it (ManaPawnNode.FallResetHeight), instead of gliding into the void forever.
            if (position.Y < Pawn.FallResetHeight)
            {
                ApplyTransforms(position);
                Dismount();
                return;
            }

            ApplyTransforms(position);
            AnimateModel(deltaSeconds, yawRadians);
        }

        /// <summary>
        /// Writes the one pose both the rider and the mount share, and is the only place
        /// <see cref="flightPosition"/> is assigned. The rider sits at the mount's origin, so there is a
        /// single position to integrate; the saddle drop is along the mount's own up-axis so the rider stays
        /// seated through a roll rather than sliding off the side of it.
        /// </summary>
        private void ApplyTransforms(Vector3 position)
        {
            flightPosition = position;

            var springArm = FindSpringArm();
            var yawRadians = (springArm?.YawRadians ?? Owner.Rotation.Y) + MathF.PI;

            // Pitch is negated going onto the node, and must be. Rotation.X spins a node about +X, which
            // carries its +Z toward -Y -- so for a rig that faces +Z, a positive Rotation.X is nose-DOWN.
            // The camera convention this pitch comes from is the opposite (negative is looking down, see
            // ManaSpringArmComponent.GetForward), because a camera faces its own -Z. Handing one straight to
            // the other pointed the mount's nose up while it dived.
            var rotation = new Vector3(-pitchRadians, yawRadians, bankRadians);

            if (model != null)
            {
                model.Root.Position = position;
                model.Root.Rotation = rotation;
                model.Root.UpdateTransform();
            }

            var saddle = model != null
                ? Vector3.Transform(Vector3.UnitY * model.SaddleOffset, rotation.ToQuaternionFromEuler())
                : Vector3.Zero;

            Owner.Position = position + saddle;
            Owner.Rotation = rotation;
        }

        private void AnimateModel(float deltaSeconds, float yawRadians)
        {
            if (model == null)
            {
                return;
            }

            // Flapping hard when slow and settling into a glide when fast, which reads as the wings doing
            // the work only when there is no speed to carry it.
            var effort = 1.0f - Math.Clamp(Speed / MathF.Max(CruiseSpeed, 0.01f), 0.0f, 1.0f);
            var flapsPerSecond = 0.7f + (effort * 1.6f);
            wingPhase += flapsPerSecond * MathF.Tau * deltaSeconds;

            var sweep = ((6.0f + (effort * 24.0f)) * MathF.Sin(wingPhase)).ToRadians();

            // Outstretched, plus the flap, plus the bank shared between them so the low wing drops.
            model.LeftWing.Rotation = new Vector3(0.0f, 0.0f, -sweep - bankRadians * 0.5f);
            model.RightWing.Rotation = new Vector3(0.0f, 0.0f, sweep - bankRadians * 0.5f);

            // Tail counter-swings the bank, which is most of what makes a turn read as steered.
            model.Tail.Rotation = new Vector3(0.0f, bankRadians * 0.6f, 0.0f);
        }

        /// <summary>
        /// Ground height under a point, from the physics world. Returns false where there is nothing to
        /// probe against, which leaves flight unbounded rather than landing the mount on an imagined floor.
        /// </summary>
        private bool TryResolveGround(Vector3 position, out float groundHeight)
        {
            groundHeight = 0.0f;

            var physics = Context.Physics;
            if (physics == null)
            {
                return false;
            }

            // -1 is never a live handle, so nothing is excluded; the pawn has no capsule while mounted.
            var handler = new ExcludeBodyRayHandler(new BepuPhysics.BodyHandle(-1));
            const float probeDistance = 4000.0f;
            var origin = position + (Vector3.UnitY * GroundClearance);

            physics.Simulation.RayCast(origin, -Vector3.UnitY, probeDistance, physics.BufferPool, ref handler, 0);

            if (!handler.Hit)
            {
                return false;
            }

            groundHeight = origin.Y - handler.Distance;
            return true;
        }

        private ManaSpringArmComponent? FindSpringArm()
        {
            foreach (var component in Owner.Components)
            {
                if (component is ManaSpringArmComponent springArm)
                {
                    return springArm;
                }
            }

            return null;
        }

        /// <summary>
        /// The direction a yaw/pitch pair points, in the +Z-facing convention the pawn and the mount rig
        /// share -- not the camera's -Z one.
        /// </summary>
        public static Vector3 DirectionFrom(float yawRadians, float pitchRadians)
        {
            var cosPitch = MathF.Cos(pitchRadians);
            return new Vector3(
                MathF.Sin(yawRadians) * cosPitch,
                MathF.Sin(pitchRadians),
                MathF.Cos(yawRadians) * cosPitch);
        }

        private static float MoveToward(float value, float target, float maxStep)
        {
            var difference = target - value;
            if (MathF.Abs(difference) <= maxStep)
            {
                return target;
            }

            return value + (MathF.Sign(difference) * maxStep);
        }

        private static float WrapRadians(float radians)
        {
            var wrapped = MathF.IEEERemainder(radians, MathF.Tau);
            return float.IsNaN(wrapped) ? 0.0f : wrapped;
        }
    }
}
