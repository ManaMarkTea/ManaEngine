using Mana.Code.Nodes;

namespace Mana
{
    // A camera boom: holds a camera at a fixed distance from a socket on its owner, orbiting on a
    // yaw/pitch the user drives, and pulling in when geometry gets between the two so the view never
    // ends up inside a wall.
    //
    // The component itself never touches the camera. It publishes a desired pose each frame and
    // CameraComponent reads it (see CameraComponent.AttachedSpringArm), which keeps the boom usable
    // for anything else that wants a follow position and keeps the camera the only thing that writes
    // camera state.
    public class ManaSpringArmComponent : ManaComponent
    {
        public ManaSpringArmComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Spring Arm";
            CurrentArmLength = ArmLength;
        }

        // World-space offset from the owner's origin to the point the camera looks at. Deliberately
        // world-axis-aligned rather than rotated into the owner's frame: a shoulder offset that spun
        // with the character would swing the whole view every time it turned.
        public Vector3 SocketOffset { get; set; } = new(0.0f, 1.5f, 0.0f);

        public float ArmLength { get; set; } = 8.0f;
        public float MinArmLength { get; set; } = 1.0f;
        public float MaxArmLength { get; set; } = 40.0f;

        public float YawRadians { get; set; }
        public float PitchRadians { get; set; } = -12.0f.ToRadians();
        public float MinPitchDegrees { get; set; } = -80.0f;
        public float MaxPitchDegrees { get; set; } = 70.0f;

        // Pulls the camera in when something is between it and the socket. Probed against the physics
        // world, so it only sees geometry that has a collider -- platforms and the ground planes do,
        // decorative meshes do not.
        public bool ProbeForCollision { get; set; } = true;
        public float ProbeBuffer { get; set; } = 0.4f;

        // Higher is snappier; 0 disables the smoothing entirely. Applied to the socket the arm hangs
        // from, not to the final camera position -- lagging the final position would fight the
        // collision probe and let the camera drift back into a wall it had just been pulled out of.
        public float SocketLagSpeed { get; set; } = 14.0f;

        // Swings the boom around behind the character as it moves, so forward is always away from the
        // viewer. Off, the arm stays wherever the mouse left it.
        public bool AutoAlignBehindTarget { get; set; }
        public float AutoAlignSpeed { get; set; } = 2.5f;

        // Optional. Excluded from the collision probe so the arm never collides with the character it
        // is following.
        public ManaCharacterBody? IgnoreBody { get; set; }

        // Supplies the heading the boom aligns behind, and whether it is worth aligning at all --
        // auto-align should not spin the camera while the character is standing still.
        public Func<(Vector3 Direction, bool Active)>? AutoAlignSource { get; set; }

        public Vector3 CameraPosition { get; private set; }
        public Vector3 CameraTarget { get; private set; }
        public float CurrentArmLength { get; private set; }
        public bool IsProbeShortened { get; private set; }

        private Vector3 smoothedSocket;
        private bool hasSmoothedSocket;

        public void AddLookInput(float yawRadians, float pitchRadians)
        {
            YawRadians += yawRadians;
            PitchRadians = Math.Clamp(
                PitchRadians + pitchRadians,
                MinPitchDegrees.ToRadians(),
                MaxPitchDegrees.ToRadians());
        }

        // Multiplicative so a notch feels the same whether you are close in or far out.
        public void AddZoomInput(float notches)
        {
            const float zoomPerNotch = 1.15f;
            ArmLength = Math.Clamp(ArmLength * MathF.Pow(zoomPerNotch, -notches), MinArmLength, MaxArmLength);
        }

        // Drops the smoothing for one frame. Call when the arm is first attached or the target
        // teleports, so the camera arrives immediately instead of flying across the level.
        public void SnapToTarget()
        {
            hasSmoothedSocket = false;
            CurrentArmLength = ArmLength;
        }

        // The direction the camera looks, from yaw/pitch. Matches ManaSceneNode.Forward's convention
        // (local -Z rotated by yaw then pitch), so the resulting angles can be written straight onto a
        // camera node's Rotation with no conversion.
        public Vector3 GetForward()
        {
            var cosPitch = MathF.Cos(PitchRadians);
            return new Vector3(
                -MathF.Sin(YawRadians) * cosPitch,
                MathF.Sin(PitchRadians),
                -MathF.Cos(YawRadians) * cosPitch);
        }

        public void Evaluate(float deltaSeconds)
        {
            var socket = Owner.WorldTransform.Translation + SocketOffset;

            if (SocketLagSpeed > 0.0f && hasSmoothedSocket && deltaSeconds > 0.0f)
            {
                // Exponential smoothing, framerate independent: the same responsiveness at 30fps and
                // 240fps, unlike a raw per-frame lerp factor.
                var blend = 1.0f - MathF.Exp(-SocketLagSpeed * deltaSeconds);
                smoothedSocket = Vector3.Lerp(smoothedSocket, socket, blend);
            }
            else
            {
                smoothedSocket = socket;
                hasSmoothedSocket = true;
            }

            if (AutoAlignBehindTarget && deltaSeconds > 0.0f)
            {
                ApplyAutoAlign(deltaSeconds);
            }

            var forward = GetForward();
            var desiredLength = ArmLength;

            IsProbeShortened = false;
            if (ProbeForCollision && Context.Physics != null)
            {
                var probedLength = ProbeArmLength(smoothedSocket, forward, desiredLength);
                IsProbeShortened = probedLength < desiredLength - 1e-3f;
                desiredLength = probedLength;
            }

            // Pulling in is instant (or the camera clips through the wall it is avoiding); easing back
            // out is smoothed, so brushing past a pillar does not fling the view outward.
            if (desiredLength < CurrentArmLength || deltaSeconds <= 0.0f || SocketLagSpeed <= 0.0f)
            {
                CurrentArmLength = desiredLength;
            }
            else
            {
                var blend = 1.0f - MathF.Exp(-SocketLagSpeed * 0.5f * deltaSeconds);
                CurrentArmLength += (desiredLength - CurrentArmLength) * blend;
            }

            CameraTarget = smoothedSocket;
            CameraPosition = smoothedSocket - (forward * CurrentArmLength);
        }

        private void ApplyAutoAlign(float deltaSeconds)
        {
            if (AutoAlignSource == null)
            {
                return;
            }

            var (direction, active) = AutoAlignSource();
            var horizontal = new Vector3(direction.X, 0.0f, direction.Z);
            if (!active || horizontal.LengthSquared() < 1e-6f)
            {
                return;
            }

            horizontal = Vector3.Normalize(horizontal);

            // The yaw whose forward matches the movement direction, from GetForward's convention.
            var targetYaw = MathF.Atan2(-horizontal.X, -horizontal.Z);
            var difference = WrapRadians(targetYaw - YawRadians);
            var blend = 1.0f - MathF.Exp(-AutoAlignSpeed * deltaSeconds);
            YawRadians = WrapRadians(YawRadians + (difference * blend));
        }

        private float ProbeArmLength(Vector3 socket, Vector3 forward, float desiredLength)
        {
            var simulation = Context.Physics.Simulation;
            var direction = -forward;

            // -1 is never a live handle, so this excludes nothing -- default(BodyHandle) would be
            // handle 0, which is a real body.
            var handler = IgnoreBody != null && IgnoreBody.Exists
                ? new ExcludeBodyRayHandler(IgnoreBody.Handle)
                : new ExcludeBodyRayHandler(new BepuPhysics.BodyHandle(-1));

            simulation.RayCast(socket, direction, desiredLength, Context.Physics.BufferPool, ref handler, 0);

            if (!handler.Hit)
            {
                return desiredLength;
            }

            return Math.Clamp(handler.Distance - ProbeBuffer, MinArmLength, desiredLength);
        }

        private static float WrapRadians(float radians)
        {
            var wrapped = MathF.IEEERemainder(radians, MathF.Tau);
            return float.IsNaN(wrapped) ? 0.0f : wrapped;
        }
    }
}
