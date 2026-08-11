using Mana.Code.Animation;

namespace Mana.Code.Nodes
{
    // A rigged model that can be told where to go. Locomotion is kinematic (position integration, no
    // rigid body): the pawn is meant to walk over scenery the physics world knows nothing about --
    // grid planes, voxel layers, imported meshes -- so ground height comes from GroundSampler rather
    // than from a simulated collider.
    //
    // Extends ManaSkeletalActorNode rather than wrapping one so the pose/propagate/skin ordering is
    // inherited as-is; movement is applied before that whole chain, which is why UpdateTransform is
    // overridden here instead of this being a component.
    public class ManaPawnNode : ManaSkeletalActorNode
    {
        private const float DirectionEpsilon = 1e-6f;

        private Vector3? destination;
        private string? activeLocomotionClip;
        private bool ownsPlaybackSpeed;

        // Ground speed each locomotion clip was authored for, in world units per second at this
        // pawn's current scale. Populated by CalibrateLocomotionClips.
        private readonly Dictionary<string, float> clipReferenceSpeeds = new(StringComparer.Ordinal);

        public ManaPawnNode(ManaContext context, Matrix4x4 localTransform) : base(context, localTransform)
        {
            Name = "Pawn";
        }

        // Clip names are looked up on the animation component, so a rig using different names just
        // needs these three set. Defaults match the Kenney mini-character clip set.
        public string IdleClipName { get; set; } = "idle";
        public string WalkClipName { get; set; } = "walk";
        public string RunClipName { get; set; } = "sprint";
        public string RootBoneName { get; set; } = "root";

        public float WalkSpeed { get; set; } = 2.0f;
        public float RunSpeed { get; set; } = 5.0f;
        public float TurnDegreesPerSecond { get; set; } = 540.0f;

        // How close counts as arrived. Too small and the pawn jitters around the destination for a
        // frame or two at high speed; this is comfortably larger than one frame of travel.
        public float ArrivalRadius { get; set; } = 0.05f;

        // Added to the yaw derived from the movement direction. 0 suits a model whose mesh faces +Z
        // (the Kenney characters do -- their arm-left bone sits at +X, which in a right-handed Y-up
        // basis only works out for a +Z facing). A -Z-facing model wants 180.
        public float FacingOffsetDegrees { get; set; }

        // Returns the ground height at a world position, or null to leave the pawn where it is.
        // Null sampler means the pawn stays pinned to GroundHeight -- see the class comment.
        public Func<Vector3, float?>? GroundSampler { get; set; }
        public float GroundHeight { get; set; }

        // Set every frame by whatever is driving the pawn (see ManaPawnController's arrow keys). A
        // non-zero drive always wins over a click destination, and cancels it -- taking manual control
        // should abandon the walk order, not fight it.
        public Vector3 DriveDirection { get; set; }
        public bool WantsToRun { get; set; }

        public Vector3? Destination => destination;
        public float CurrentSpeed { get; private set; }
        public bool IsMoving => CurrentSpeed > DirectionEpsilon;

        // Purely presentational -- ManaPawnController owns which pawn is actually being driven.
        public bool IsPossessed { get; set; }

        // Retimes the locomotion clip so its stride matches how fast the pawn is actually moving --
        // the fix for feet sliding on the floor. Needs CalibrateLocomotionClips to have run; without a
        // reference speed for the current clip the playback rate is left alone.
        public bool MatchAnimationToSpeed { get; set; } = true;

        // Guards against a near-stationary pawn stretching a clip out to a crawl, and against a very
        // fast one flickering. Generous on both ends -- the point is to catch degenerate values, not
        // to second-guess the match.
        public float MinPlaybackSpeed { get; set; } = 0.25f;
        public float MaxPlaybackSpeed { get; set; } = 4.0f;

        // Measures what ground speed each locomotion clip was authored for and remembers it, so
        // MatchAnimationToSpeed has something to scale against. Call after the pawn's scale is final:
        // the stored speeds are in world units, so rescaling the model invalidates them.
        //
        // Returns the measurements taken, for inspection -- see ManaStrideAnalyzer for what the
        // numbers do and do not mean on a stylised rig.
        public IReadOnlyList<ManaStrideMeasurement> CalibrateLocomotionClips(IReadOnlyList<string>? footBoneNames = null)
        {
            clipReferenceSpeeds.Clear();
            var measurements = new List<ManaStrideMeasurement>();

            // Uniform scale is assumed throughout the pawn (TryNormalizeHeight only ever sets that),
            // so one axis is enough to convert the rig's local units into world units.
            var worldScale = MathF.Abs(Scale.X) > 1e-6f ? MathF.Abs(Scale.X) : 1.0f;

            foreach (var clipName in new[] { WalkClipName, RunClipName })
            {
                var clip = Animation.Clips.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, clipName, StringComparison.OrdinalIgnoreCase));
                if (clip == null)
                {
                    continue;
                }

                // Authored root motion beats any estimate, so prefer it when a clip actually has it.
                if (ManaStrideAnalyzer.TryGetRootMotionSpeed(clip, RootBoneName, out var rootMotionSpeed))
                {
                    var worldRootSpeed = rootMotionSpeed * worldScale;
                    clipReferenceSpeeds[clipName] = worldRootSpeed;
                    measurements.Add(new ManaStrideMeasurement(
                        clip.Name,
                        worldRootSpeed * (float)clip.DurationSeconds * 0.5f,
                        (float)clip.DurationSeconds,
                        worldRootSpeed,
                        0));
                    continue;
                }

                var measurement = ManaStrideAnalyzer.Measure(this, clip, footBoneNames);
                if (measurement == null || measurement.ReferenceSpeed <= 0.0f)
                {
                    continue;
                }

                // Every distance in the returned measurement is converted, not just the speed --
                // mixing rig-local lengths with world-space speeds in one record is a trap.
                clipReferenceSpeeds[clipName] = measurement.ReferenceSpeed * worldScale;
                measurements.Add(measurement with
                {
                    StepLength = measurement.StepLength * worldScale,
                    ReferenceSpeed = measurement.ReferenceSpeed * worldScale
                });
            }

            return measurements;
        }

        // Sets WalkSpeed/RunSpeed to whatever the clips were authored for, so the feet are planted at
        // the pawn's default speeds instead of the animation being stretched to fit a chosen speed.
        public void ApplyCalibratedSpeeds()
        {
            if (clipReferenceSpeeds.TryGetValue(WalkClipName, out var walkReference) && walkReference > 0.0f)
            {
                WalkSpeed = walkReference;
            }

            if (clipReferenceSpeeds.TryGetValue(RunClipName, out var runReference) && runReference > 0.0f)
            {
                RunSpeed = runReference;
            }
        }

        public bool TryGetClipReferenceSpeed(string clipName, out float referenceSpeed)
        {
            return clipReferenceSpeeds.TryGetValue(clipName, out referenceSpeed);
        }

        public void WalkTo(Vector3 worldPosition)
        {
            destination = worldPosition;
        }

        public void StopMoving()
        {
            destination = null;
            DriveDirection = Vector3.Zero;
        }

        // Walks up from any descendant (the pick lands on a child mesh node, never on the pawn root).
        public static ManaPawnNode? FindOwningPawn(ManaSceneNode? node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current is ManaPawnNode pawn)
                {
                    return pawn;
                }
            }

            return null;
        }

        // Uniformly scales the pawn so its rendered height matches targetHeight. Rigged source models
        // are authored at wildly different scales (a Kenney mini-character is 0.67 units tall, against
        // a 1-unit voxel), so spawning one at native scale usually means an invisible speck.
        public bool TryNormalizeHeight(float targetHeight)
        {
            if (targetHeight <= 0.0f || !TryGetBindPoseHeight(out var sourceHeight) || sourceHeight <= 0.0f)
            {
                return false;
            }

            var factor = targetHeight / sourceHeight;
            Scale = new Vector3(factor, factor, factor);
            return true;
        }

        // Height of the untouched bind pose, in this node's local space. Read from the skin bindings
        // rather than the live mesh so an in-progress animation can't skew the result.
        public bool TryGetBindPoseHeight(out float height)
        {
            height = 0.0f;
            var minY = float.MaxValue;
            var maxY = float.MinValue;

            foreach (var skinnedMesh in SkinnedMeshes)
            {
                foreach (var bindPosition in skinnedMesh.Binding.BindPositions)
                {
                    var local = Vector3.Transform(bindPosition, skinnedMesh.Owner.LocalTransform);
                    minY = MathF.Min(minY, local.Y);
                    maxY = MathF.Max(maxY, local.Y);
                }
            }

            if (minY > maxY)
            {
                return false;
            }

            height = maxY - minY;
            return height > 0.0f;
        }

        public override void UpdateTransform()
        {
            StepLocomotion((float)ManaTime.GlobalTime.DeltaTime.TotalSeconds);
            SyncLocomotionClip();
            SyncAnimationPlaybackRate();

            // Base handles the skeletal chain: pose bones, propagate world transforms, skin.
            base.UpdateTransform();
        }

        private void StepLocomotion(float deltaSeconds)
        {
            if (deltaSeconds <= 0.0f)
            {
                CurrentSpeed = 0.0f;
                return;
            }

            var position = Position;
            var heading = ResolveHeading(position, out var distanceToDestination);

            if (heading.HasValue)
            {
                var speed = WantsToRun ? RunSpeed : WalkSpeed;
                var step = speed * deltaSeconds;

                // Clamping to the remaining distance is what stops a fast pawn from oscillating past
                // its destination and immediately turning around.
                if (distanceToDestination.HasValue)
                {
                    step = MathF.Min(step, distanceToDestination.Value);
                }

                position += heading.Value * step;
                CurrentSpeed = step / deltaSeconds;
                TurnToward(heading.Value, deltaSeconds);
            }
            else
            {
                CurrentSpeed = 0.0f;
            }

            var groundY = GroundSampler?.Invoke(position) ?? GroundHeight;
            Position = new Vector3(position.X, groundY, position.Z);
        }

        // Returns the horizontal unit direction to travel this frame, plus how far is left when the
        // move is destination-driven (null when free-driving, which has no end point).
        private Vector3? ResolveHeading(Vector3 position, out float? distanceToDestination)
        {
            distanceToDestination = null;

            var drive = new Vector3(DriveDirection.X, 0.0f, DriveDirection.Z);
            if (drive.LengthSquared() > DirectionEpsilon)
            {
                destination = null;
                return Vector3.Normalize(drive);
            }

            if (!destination.HasValue)
            {
                return null;
            }

            var toDestination = destination.Value - position;
            toDestination.Y = 0.0f;

            var distance = toDestination.Length();
            if (distance <= ArrivalRadius)
            {
                destination = null;
                return null;
            }

            distanceToDestination = distance;
            return toDestination / distance;
        }

        private void TurnToward(Vector3 heading, float deltaSeconds)
        {
            // A Y-rotation of theta maps the model's local +Z onto (sin theta, 0, cos theta), so this
            // is the yaw that points the model's own forward axis along the heading.
            var targetYaw = MathF.Atan2(heading.X, heading.Z) + FacingOffsetDegrees.ToRadians();
            var currentYaw = Rotation.Y;

            var difference = WrapRadians(targetYaw - currentYaw);
            var maxTurn = TurnDegreesPerSecond.ToRadians() * deltaSeconds;
            var appliedTurn = Math.Clamp(difference, -maxTurn, maxTurn);

            Rotation = new Vector3(Rotation.X, WrapRadians(currentYaw + appliedTurn), Rotation.Z);
        }

        private static float WrapRadians(float radians)
        {
            var wrapped = MathF.IEEERemainder(radians, MathF.Tau);
            return float.IsNaN(wrapped) ? 0.0f : wrapped;
        }

        private void SyncLocomotionClip()
        {
            var desiredClip = !IsMoving
                ? IdleClipName
                : WantsToRun ? RunClipName : WalkClipName;

            if (string.Equals(desiredClip, activeLocomotionClip, StringComparison.Ordinal))
            {
                return;
            }

            // Only remember the clip if the rig actually has it, so a rig missing "sprint" keeps
            // retrying against "walk" rather than latching onto a name that never resolved.
            if (Animation.Play(desiredClip))
            {
                activeLocomotionClip = desiredClip;
            }
        }

        // The anti-slide step: play the clip at the rate its stride implies for the speed we are
        // actually travelling. A clip authored for 2.7 u/s played at 1.0x while the pawn moves at
        // 5 u/s is exactly what foot sliding looks like.
        private void SyncAnimationPlaybackRate()
        {
            if (!MatchAnimationToSpeed)
            {
                // Only reset a rate this pawn set itself. Turning the match off has to undo it --
                // otherwise the clip stays stuck at whatever multiple was in force at the time -- but
                // a PlaybackSpeed the caller set by hand is not ours to clobber.
                if (ownsPlaybackSpeed)
                {
                    Animation.PlaybackSpeed = 1.0f;
                    ownsPlaybackSpeed = false;
                }

                return;
            }

            if (!IsMoving || activeLocomotionClip == null ||
                !clipReferenceSpeeds.TryGetValue(activeLocomotionClip, out var referenceSpeed) ||
                referenceSpeed <= 0.0f)
            {
                if (ownsPlaybackSpeed)
                {
                    Animation.PlaybackSpeed = 1.0f;
                    ownsPlaybackSpeed = false;
                }

                return;
            }

            Animation.PlaybackSpeed = Math.Clamp(CurrentSpeed / referenceSpeed, MinPlaybackSpeed, MaxPlaybackSpeed);
            ownsPlaybackSpeed = true;
        }
    }
}
