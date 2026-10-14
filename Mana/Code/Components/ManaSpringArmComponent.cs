using Mana.Code;
using Mana.Code.Nodes;

namespace Mana
{
    // A camera boom: holds a camera at a fixed distance from a socket on its owner, orbiting on a
    // yaw/pitch the user drives, and pulling in when geometry gets between the two so the view never
    // ends up inside a wall.
    //
    // The component itself never touches the camera. It publishes a desired pose each frame and
    // CameraComponent reads it (it is the camera's IManaCameraRig while attached), which keeps the boom usable
    // for anything else that wants a follow position and keeps the camera the only thing that writes
    // camera state. It never touches its own target either -- the yaw here is the authority on where
    // the view points, and a target that wants to face along it turns itself.
    public class ManaSpringArmComponent : ManaComponent, IManaCameraLookRig
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

        // Temporary gameplay lens effects leave the camera's configured FOV untouched.
        public float FieldOfViewOffsetDegrees { get; set; }

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

        // Side-on games can soften jumps without making horizontal travel sluggish; null uses the usual speed.
        public float? VerticalSocketLagSpeed { get; set; }

        // Copies any yaw the target turns by itself -- its turn keys, a walk order -- onto the boom, so
        // the camera stays behind a character that turns under its own steam.
        //
        // A delta copy, deliberately, not a "steer toward behind the target" chase. Whoever slaves the
        // target's facing to this boom (see ManaPawnController) must switch this off while doing so:
        // two-way coupling makes the pair chase its own tail, and the target's facing target then
        // recedes by exactly what each frame's turn just closed -- an endless spin rather than a turn
        // that arrives.
        public bool FollowTargetYaw { get; set; }

        // Scales a yaw look-input. Separate from the camera's own mouse scaling because a boom whose
        // target's facing is locked to it turns a whole character, which reads as a much larger motion
        // than panning a free-flying camera by the same angle.
        public float LookYawSensitivity { get; set; } = 0.6f;

        // Optional. Excluded from the collision probe so the arm never collides with the character it
        // is following.
        public ManaCharacterBody? IgnoreBody { get; set; }

        // The two look drags, published by whoever feeds AddLookInput: true on every frame of an active
        // drag, including the frames it does not happen to move. Published here rather than re-read from
        // the mouse because the camera's own gesture latch is the authority on whether a drag is still
        // live -- one that started on the scene and wandered over a panel still is.
        //
        // Both turn the boom. They differ in what the target does about it: steering (right-drag) drags
        // the character round with the view, orbiting (left-drag) leaves it alone so you can look at it
        // from any side without it turning to follow.
        public bool IsSteerGestureActive { get; set; }
        public bool IsOrbitGestureActive { get; set; }

        /// <summary>
        /// Whether this boom uses the play-mode mouse scheme: a left-drag orbits as well as a right-drag,
        /// and the pointer is hidden and locked for the duration of either.
        /// <para>
        /// One flag rather than two because it is one decision -- "this is a game being played, not a
        /// scene being edited" -- and both halves are wrong in the editor. A left-press there is already a
        /// selection or a walk order, decided on the press before a drag has moved far enough to be one,
        /// so an always-on left orbit would fire both; and grabbing the pointer out from under someone
        /// nudging a gizmo is worse than leaving it visible.
        /// </para>
        /// </summary>
        public bool MouseLookControls { get; set; }

        public Vector3 CameraPosition { get; private set; }
        public Vector3 CameraTarget { get; private set; }
        public Vector3 CameraRotation => new(PitchRadians, YawRadians, 0.0f);
        public float CurrentArmLength { get; private set; }
        public bool IsProbeShortened { get; private set; }

        private Vector3 smoothedSocket;
        private bool hasSmoothedSocket;
        private float? lastTargetYaw;

        // Camera-side only. The boom's yaw is the authority on where the view points; anything that
        // wants the target to face along it reads GetForward and turns itself (ManaPawnController
        // does), which keeps the coupling one-way. Writing the target's rotation from in here is what
        // made a strafe spin: the strafe heading is derived from this yaw, so a yaw derived back from
        // the target closed the loop.
        public void AddLookInput(float yawRadians, float pitchRadians)
        {
            YawRadians = WrapRadians(YawRadians + (yawRadians * LookYawSensitivity));
            PitchRadians = Math.Clamp(
                PitchRadians + pitchRadians,
                MinPitchDegrees.ToRadians(),
                MaxPitchDegrees.ToRadians());
        }

        /// <summary>
        /// Turns the boom by an exact amount, bypassing <see cref="LookYawSensitivity"/> -- that scales a
        /// raw mouse delta into an angle, and a caller who already has the angle it wants (a turn key at a
        /// rate per second) would otherwise have it silently scaled.
        /// </summary>
        public void AddYawTurn(float yawRadians)
        {
            YawRadians = WrapRadians(YawRadians + yawRadians);
        }

        // Multiplicative so a notch feels the same whether you are close in or far out.
        public void AddZoomInput(float notches)
        {
            const float zoomPerNotch = 1.15f;
            ArmLength = Math.Clamp(ArmLength * MathF.Pow(zoomPerNotch, -notches), MinArmLength, MaxArmLength);
        }

        // Drops the smoothing for one frame. Call when the arm is first attached or the target
        // teleports, so the camera arrives immediately instead of flying across the level.
        public float FieldOfView(float cameraDegrees) => cameraDegrees + FieldOfViewOffsetDegrees;

        void IManaCameraRig.Snap() => SnapToTarget();

        public void SnapToTarget()
        {
            hasSmoothedSocket = false;
            CurrentArmLength = ArmLength;

            // Forgotten rather than kept: a remembered yaw from whatever this boom followed before
            // would land on the new target as one spurious frame of "it turned by this much".
            lastTargetYaw = null;
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

            if (hasSmoothedSocket && deltaSeconds > 0.0f)
            {
                // Exponential smoothing, framerate independent: the same responsiveness at 30fps and
                // 240fps, unlike a raw per-frame lerp factor.
                var previousHeight = smoothedSocket.Y;
                var blend = SocketLagSpeed > 0.0f ? 1.0f - MathF.Exp(-SocketLagSpeed * deltaSeconds) : 1.0f;
                smoothedSocket = Vector3.Lerp(smoothedSocket, socket, blend);
                var verticalSpeed = VerticalSocketLagSpeed ?? SocketLagSpeed;
                var verticalBlend = verticalSpeed > 0.0f ? 1.0f - MathF.Exp(-verticalSpeed * deltaSeconds) : 1.0f;
                smoothedSocket.Y = previousHeight + (socket.Y - previousHeight) * verticalBlend;
            }
            else
            {
                smoothedSocket = socket;
                hasSmoothedSocket = true;
            }

            // Tracked unconditionally, so switching FollowTargetYaw back on mid-play picks up from the
            // target's current heading instead of copying everything it turned while switched off.
            var targetYaw = Owner.Rotation.Y;
            if (FollowTargetYaw && lastTargetYaw.HasValue)
            {
                YawRadians = WrapRadians(YawRadians + WrapRadians(targetYaw - lastTargetYaw.Value));
            }

            lastTargetYaw = targetYaw;

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
