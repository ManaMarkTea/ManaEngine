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

        // Tuned against WoW, which is close to a direct read: a character is normalised to 2 units tall
        // (CharacterSpawnService), so a unit is about a WoW yard. WoW runs at 7 yd/s and walks at 2.5,
        // so RunSpeed is its run outright and the unmodified speed sits between the two as a jog.
        //
        // Chosen rather than adopted from the animation: ApplyCalibratedSpeeds would set these to
        // whatever speed each clip was authored for, which makes how fast the game plays a property of
        // the art. MatchAnimationToSpeed retimes the clip to whatever is set here instead, which is the
        // same anti-slide guarantee without the game's feel riding on it.
        public const float DefaultWalkSpeed = 4.5f;
        public const float DefaultRunSpeed = 7.0f;
        public const float DefaultJumpHeight = 3.0f;

        /// <summary>The character height the default speeds and jump were tuned for.</summary>
        public const float ReferenceHeight = 2.0f;

        public float WalkSpeed { get; set; } = DefaultWalkSpeed;
        public float RunSpeed { get; set; } = DefaultRunSpeed;
        public float TurnDegreesPerSecond { get; set; } = 540.0f;

        // How close counts as arrived. Too small and the pawn jitters around the destination for a
        // frame or two at high speed; this is comfortably larger than one frame of travel.
        public float ArrivalRadius { get; set; } = 0.05f;

        // Added to the yaw derived from the movement direction. 0 suits a model whose mesh faces +Z
        // (the Kenney characters do -- their arm-left bone sits at +X, which in a right-handed Y-up
        // basis only works out for a +Z facing). A -Z-facing model wants 180.
        public float FacingOffsetDegrees { get; set; }

        /// <summary>
        /// Whether this pawn walks itself. Turned off by anything that takes the transform over wholesale
        /// -- a flying mount, a cutscene -- so the locomotion step stops writing a position and a facing
        /// that something else is about to overwrite anyway.
        /// <para>
        /// Not merely tidiness: the kinematic step pins Y to the ground every frame it runs, so a pawn
        /// being flown would be dragged back to the floor and lifted again, twice per frame, forever. Also
        /// read by ManaPawnController, which stands its input down while a pawn is not its to drive.
        /// </para>
        /// </summary>
        public bool LocomotionEnabled { get; set; } = true;

        // Returns the ground height at a world position, or null to leave the pawn where it is.
        // Null sampler means the pawn stays pinned to GroundHeight -- see the class comment.
        public Func<Vector3, float?>? GroundSampler { get; set; }
        public float GroundHeight { get; set; }

        // Set every frame by whatever is driving the pawn (see ManaPawnController's arrow keys). A
        // non-zero drive always wins over a click destination, and cancels it -- taking manual control
        // should abandon the walk order, not fight it. A drive direction never turns the pawn by
        // itself; facing is a separate decision, see DesiredFacingDirection.
        public Vector3 DriveDirection { get; set; }
        public bool WantsToRun { get; set; }

        // Where the pawn should be looking, independent of where it is walking -- so it can strafe
        // sideways, backpedal, or turn on the spot without moving. Null leaves facing alone, which is
        // what plain drive input wants: a heading built from the pawn's own facing can never be turned
        // toward, because it moves with every degree of the turn.
        public Vector3? DesiredFacingDirection { get; set; }

        // Whether to arrive at DesiredFacingDirection this frame rather than turn toward it at
        // TurnDegreesPerSecond. Instant is for facing that is slaved to a camera the player is
        // dragging: the view moves with the mouse, and a rate-limited character visibly lags behind a
        // fast flick instead of reading as locked to it.
        public bool DesiredFacingIsInstant { get; set; }

        // Whether this frame's DriveDirection should turn the pawn to face it, the same as a
        // click-to-move destination does. Left off for plain forward/back -- walking backward is
        // always exactly opposite of the pawn's own facing, so a heading built from the pawn's own
        // rotation can never converge there (the target recedes by exactly what the turn just
        // closed, every frame) -- which is what the old backward-jitter actually was. Strafing is
        // safe because ManaPawnController builds its heading from the camera instead, an external
        // reference that doesn't move just because the pawn turns.
        public bool DriveFacesHeading { get; set; }

        // Turn-in-place input, -1 (turn left) .. 1 (turn right), applied at TurnDegreesPerSecond.
        // Separate from DriveDirection because turning is the only thing it does -- it implies no
        // movement -- so the "turn" keys (A/D when the look button isn't held) drive this instead of
        // DriveDirection, matching a WoW-style controller where strafing and turning are distinct.
        public float DriveTurnInput { get; set; }

        public Vector3? Destination => destination;
        public float CurrentSpeed { get; private set; }

        // Well below any real walk, but far above the residual the solver leaves on a character
        // pressed against a wall -- comparing against a float epsilon there left the walk cycle
        // creeping along at 0.003 u/s instead of settling into idle.
        public bool IsMoving => CurrentSpeed > MovingSpeedThreshold;

        private const float MovingSpeedThreshold = 0.05f;

        // Purely presentational -- ManaPawnController owns which pawn is actually being driven.
        public bool IsPossessed { get; set; }

        // Which asset pack this model came from. Together with Name it identifies the character in the
        // spawn catalogue, which is how "swap to the next character" knows where it currently is.
        public string? SourcePackName { get; set; }

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

        // The direction the model is actually facing in world space, in the same heading convention
        // TurnToward turns toward (atan2(x, z), i.e. the rig's own +Z-faces-front axis) -- the opposite
        // sense from ManaSceneNode.Forward, which assumes a camera's -Z-is-forward instead. This is
        // what "forward" means to the drive keys: always where the character is looking, never the
        // camera, so pressing back walks backward instead of spinning the pawn around to face the camera.
        public Vector3 FacingDirection
        {
            get
            {
                var yaw = Rotation.Y - FacingOffsetDegrees.ToRadians();
                return new Vector3(MathF.Sin(yaw), 0.0f, MathF.Cos(yaw));
            }
        }

        public void WalkTo(Vector3 worldPosition)
        {
            destination = worldPosition;
        }

        public void StopMoving()
        {
            destination = null;
            DriveDirection = Vector3.Zero;
            DriveTurnInput = 0.0f;
            DesiredFacingDirection = null;

            // Vertical velocity is left alone: cancelling a walk order should not freeze a pawn in
            // mid-air.
            if (Body != null && Body.Exists)
            {
                var velocity = Body.Velocity;
                Body.Velocity = new Vector3(0.0f, velocity.Y, 0.0f);
            }
        }

        // Moves the pawn and, when physics-driven, the capsule with it -- setting Position alone would
        // be overwritten by the body's pose on the very next frame.
        public void TeleportTo(Vector3 worldPosition)
        {
            Position = worldPosition;
            destination = null;
            Body?.Teleport(worldPosition);
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
        /// <summary>
        /// Scales walk speed, run speed and jump height to this pawn's actual height, from the values tuned for
        /// a <see cref="ReferenceHeight"/> character. A model kept at its own size can be a third of that or
        /// bigger than it, and the same speeds in world units would make a small one sprint at many body
        /// lengths a second. Call once, after the pawn's scale is final.
        /// </summary>
        public void ScaleLocomotionToHeight()
        {
            var ratio = ResolveWorldHeight() / ReferenceHeight;
            WalkSpeed = DefaultWalkSpeed * ratio;
            RunSpeed = DefaultRunSpeed * ratio;
            JumpHeight = DefaultJumpHeight * ratio;
        }

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

        // Height of the model in this node's local space. Delegates to TryGetLocalBounds, which knows
        // how to measure both a skinned rig (from its bind pose) and a rigid one (from the meshes
        // parented into the node tree) -- the blocky characters are the latter and would otherwise
        // measure as zero and spawn at native scale.
        public bool TryGetBindPoseHeight(out float height)
        {
            height = 0.0f;
            if (!TryGetLocalBounds(out var min, out var max))
            {
                return false;
            }

            height = max.Y - min.Y;
            return height > 0.0f;
        }

        public override void UpdateTransform()
        {
            var deltaSeconds = (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;

            if (!LocomotionEnabled)
            {
                // Reported as stationary so the clip below settles into idle rather than running on the
                // spot at whatever speed the pawn was last moving under its own power.
                CurrentSpeed = 0.0f;
            }
            else if (Body != null)
            {
                StepPhysicsLocomotion(deltaSeconds);
            }
            else
            {
                StepLocomotion(deltaSeconds);
            }

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

            ApplyDriveTurn(deltaSeconds);

            var position = Position;
            var heading = ResolveHeading(position, out var distanceToDestination, out var isDriveHeading);

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
            }
            else
            {
                CurrentSpeed = 0.0f;
            }

            // Outside the move, so a pawn standing still can still be turned on the spot.
            ApplyFacing(heading, isDriveHeading, deltaSeconds);

            var groundY = GroundSampler?.Invoke(position) ?? GroundHeight;
            Position = new Vector3(position.X, groundY, position.Z);
        }

        // ---- Physics-driven locomotion ------------------------------------------------------------

        // Attaches a Bepu capsule and switches this pawn from kinematic position integration to
        // velocity-driven physics: it now collides with walls and platforms, falls under gravity, and
        // can jump. Sized from the pawn's rendered height by default.
        public ManaCharacterBody EnablePhysics(float? radius = null, float? height = null, float mass = 1.0f)
        {
            DisablePhysics();

            var capsuleHeight = height ?? ResolveWorldHeight();
            var capsuleRadius = radius ?? MathF.Max(capsuleHeight * 0.18f, 0.05f);

            Body = ManaCharacterBody.Create(Context, Position, capsuleRadius, capsuleHeight, mass);
            return Body;
        }

        public void DisablePhysics()
        {
            Body?.Dispose();
            Body = null;
        }

        public ManaCharacterBody? Body { get; private set; }

        // Jump tuning. The defaults are shaped for the editor's -64 u/s^2 gravity and a 2-unit
        // character; JumpHeight is the apex a full-hold jump reaches from flat ground.
        public float JumpHeight { get; set; } = DefaultJumpHeight;

        // Mario's defining trick: releasing the button early cuts the rise short, so tapping gives a
        // hop and holding gives the full arc. Applied as a one-shot multiplier on upward velocity at
        // the moment of release.
        public float JumpReleaseCutoff { get; set; } = 0.45f;

        // Falling faster than you rose is what stops a jump feeling floaty. Applied as extra gravity
        // on top of the simulation's own, only while descending.
        public float FallGravityMultiplier { get; set; } = 1.9f;

        // Fraction of ground control retained mid-air. 1 is full air control, 0 commits you to the
        // arc you took off with.
        public float AirControl { get; set; } = 0.75f;

        // Grace period after walking off a ledge during which a jump still counts, and how long a jump
        // pressed just before landing is remembered. Both are the standard forgiveness windows -- input
        // that "should" have worked does.
        public float CoyoteTimeSeconds { get; set; } = 0.12f;
        public float JumpBufferSeconds { get; set; } = 0.12f;

        // 1 is a plain jump; 2 gives a double jump, and so on.
        public int MaxJumpCount { get; set; } = 1;

        public string JumpClipName { get; set; } = "jump";
        public string FallClipName { get; set; } = "fall";

        // Falling off the edge of the world is a real outcome once gravity is involved -- the editor's
        // collision floor only covers the 256-unit grid. Dropping below this teleports the pawn back
        // to RespawnPoint instead of accelerating into the void forever.
        public float FallResetHeight { get; set; } = -50.0f;
        public Vector3 RespawnPoint { get; set; }

        // Ground contact is suppressed for this long right after take-off. Without it the capsule is
        // still inside the ground probe on the frame after a jump, which reads as an instant landing
        // and refills the jump budget -- infinite jumps from a held button.
        public float JumpLockoutSeconds { get; set; } = 0.1f;

        public bool IsGrounded => Body == null || isGrounded;
        public bool IsJumpHeld { get; private set; }
        public int JumpsUsed { get; private set; }

        private float coyoteTimer;
        private float jumpBufferTimer;
        private float jumpLockoutTimer;
        private bool isGrounded = true;
        private bool wasGrounded = true;

        // Call on the frame the jump button goes down. Buffered rather than acted on immediately, so a
        // press a few frames before landing still fires (see JumpBufferSeconds).
        public void PressJump()
        {
            IsJumpHeld = true;
            jumpBufferTimer = JumpBufferSeconds;
        }

        public void ReleaseJump()
        {
            IsJumpHeld = false;

            if (Body == null)
            {
                return;
            }

            // Cut the rise short. Only while actually rising -- applying this on the way down would
            // slow the fall instead, which reads as floatiness.
            var velocity = Body.Velocity;
            if (velocity.Y > 0.0f)
            {
                velocity.Y *= Math.Clamp(JumpReleaseCutoff, 0.0f, 1.0f);
                Body.Velocity = velocity;
            }
        }

        // Take-off speed for the configured apex: v = sqrt(2 * g * h).
        public float GetJumpVelocity()
        {
            var gravity = MathF.Abs(Context.Physics.Gravity.Y);
            return MathF.Sqrt(2.0f * gravity * MathF.Max(JumpHeight, 0.0f));
        }

        private void StepPhysicsLocomotion(float deltaSeconds)
        {
            var body = Body!;
            if (!body.Exists || deltaSeconds <= 0.0f)
            {
                CurrentSpeed = 0.0f;
                return;
            }

            if (body.FootPosition.Y < FallResetHeight)
            {
                TeleportTo(RespawnPoint);
                CurrentSpeed = 0.0f;
                JumpsUsed = 0;
                return;
            }

            body.RefreshGroundState();
            ApplyDriveTurn(deltaSeconds);

            jumpLockoutTimer = MathF.Max(jumpLockoutTimer - deltaSeconds, 0.0f);

            var grounded = body.IsGrounded && jumpLockoutTimer <= 0.0f;
            isGrounded = grounded;

            if (grounded && !wasGrounded)
            {
                JumpsUsed = 0;
            }

            coyoteTimer = grounded ? CoyoteTimeSeconds : MathF.Max(coyoteTimer - deltaSeconds, 0.0f);

            // Walking off a ledge and letting the coyote window lapse forfeits the ground jump.
            // Without this, JumpsUsed stays 0 the whole way down and MaxJumpCount == 1 still grants a
            // free mid-air jump -- which is a double jump wearing a disguise.
            if (!grounded && coyoteTimer <= 0.0f && JumpsUsed == 0)
            {
                JumpsUsed = 1;
            }

            jumpBufferTimer = MathF.Max(jumpBufferTimer - deltaSeconds, 0.0f);
            wasGrounded = grounded;

            var velocity = body.Velocity;
            var velocityBeforeInput = velocity;

            // Horizontal steering. The same heading logic as the kinematic path, but resolved against
            // the body's position, which is now the authority on where the pawn is.
            var footPosition = body.FootPosition;
            var heading = ResolveHeading(footPosition, out var distanceToDestination, out var isDriveHeading);

            var targetSpeed = WantsToRun ? RunSpeed : WalkSpeed;
            var horizontal = new Vector3(velocity.X, 0.0f, velocity.Z);

            if (heading.HasValue)
            {
                // Arrival damping only matters on the ground; in the air the pawn is committed.
                if (distanceToDestination.HasValue && grounded)
                {
                    targetSpeed = MathF.Min(targetSpeed, distanceToDestination.Value / deltaSeconds);
                }

                var desired = heading.Value * targetSpeed;
                horizontal = grounded
                    ? desired
                    : Vector3.Lerp(horizontal, desired, Math.Clamp(AirControl, 0.0f, 1.0f));
            }
            else if (grounded)
            {
                horizontal = Vector3.Zero;
            }
            else
            {
                horizontal = Vector3.Lerp(horizontal, Vector3.Zero, Math.Clamp(AirControl, 0.0f, 1.0f) * deltaSeconds);
            }

            // Outside the steering, same as the kinematic path: facing is its own decision, and a pawn
            // held against a wall still turns.
            ApplyFacing(heading, isDriveHeading, deltaSeconds);

            velocity.X = horizontal.X;
            velocity.Z = horizontal.Z;

            // Jump: a buffered press fires if we are grounded, inside the coyote window, or still have
            // an air jump left.
            var canGroundJump = grounded || coyoteTimer > 0.0f;
            if (jumpBufferTimer > 0.0f && (canGroundJump || JumpsUsed < MaxJumpCount))
            {
                velocity.Y = GetJumpVelocity();
                JumpsUsed = canGroundJump ? 1 : JumpsUsed + 1;
                jumpBufferTimer = 0.0f;
                coyoteTimer = 0.0f;
                jumpLockoutTimer = JumpLockoutSeconds;
                isGrounded = false;
                wasGrounded = false;
            }
            else if (grounded)
            {
                if (!heading.HasValue)
                {
                    // Counter the portion of next frame's gravity that lies along the ground plane.
                    // The solver still receives the normal component, so the pawn remains planted
                    // without accumulating the tiny downhill step that causes idle slope creep.
                    velocity = ComputeIdleGroundSupportVelocity(
                        Context.Physics.Gravity,
                        body.GroundNormal,
                        deltaSeconds);
                }
                else if (velocity.Y < 0.0f)
                {
                    velocity.Y = 0.0f;
                }
            }
            else if (velocity.Y < 0.0f)
            {
                // Extra fall gravity, on top of what the pose integrator already applies.
                var extraGravity = MathF.Abs(Context.Physics.Gravity.Y) * MathF.Max(FallGravityMultiplier - 1.0f, 0.0f);
                velocity.Y -= extraGravity * deltaSeconds;
            }

            body.Velocity = velocity;

            // Reported from the velocity the solver actually produced last step, not the velocity we
            // are about to request. Walking into a wall then reads as stopped -- the walk cycle stops
            // and the stride retiming stops stretching -- instead of running on the spot.
            var solvedHorizontal = new Vector3(velocityBeforeInput.X, 0.0f, velocityBeforeInput.Z);
            CurrentSpeed = solvedHorizontal.Length();
            Position = body.FootPosition;
        }

        private static Vector3 ComputeIdleGroundSupportVelocity(
            Vector3 gravity,
            Vector3 groundNormal,
            float deltaSeconds)
        {
            var tangentGravity = gravity - Vector3.Dot(gravity, groundNormal) * groundNormal;
            return -tangentGravity * deltaSeconds;
        }

        private float ResolveWorldHeight()
        {
            var worldScale = MathF.Abs(Scale.Y) > 1e-6f ? MathF.Abs(Scale.Y) : 1.0f;
            return TryGetBindPoseHeight(out var bindHeight) && bindHeight > 0.0f
                ? bindHeight * worldScale
                : 2.0f;
        }

        // Returns the horizontal unit direction to travel this frame, plus how far is left when the
        // move is destination-driven (null when free-driving, which has no end point), plus whether
        // the heading came from DriveDirection rather than a click-to-move destination -- ApplyFacing
        // uses that to decide whether the heading may turn the pawn.
        private Vector3? ResolveHeading(Vector3 position, out float? distanceToDestination, out bool isDriveHeading)
        {
            distanceToDestination = null;

            var drive = new Vector3(DriveDirection.X, 0.0f, DriveDirection.Z);
            if (drive.LengthSquared() > DirectionEpsilon)
            {
                destination = null;
                isDriveHeading = true;
                return Vector3.Normalize(drive);
            }

            isDriveHeading = false;
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

        // Resolves where the pawn looks, after the move so it can face where it actually went. An
        // explicit DesiredFacingDirection wins; failing that, only a click-to-move heading turns the
        // pawn -- a drive heading leaves facing alone (see DesiredFacingDirection).
        private void ApplyFacing(Vector3? moveHeading, bool headingIsDrive, float deltaSeconds)
        {
            if (DesiredFacingDirection is { } desired)
            {
                var flattened = new Vector3(desired.X, 0.0f, desired.Z);
                if (flattened.LengthSquared() > DirectionEpsilon)
                {
                    if (DesiredFacingIsInstant)
                    {
                        Rotation = new Vector3(Rotation.X, GetFacingYawFor(flattened), Rotation.Z);
                    }
                    else
                    {
                        TurnToward(flattened, deltaSeconds);
                    }

                    return;
                }
            }

            if (moveHeading.HasValue && !headingIsDrive)
            {
                TurnToward(moveHeading.Value, deltaSeconds);
            }
        }

        // A Y-rotation of theta maps the model's local +Z onto (sin theta, 0, cos theta), so this is
        // the yaw that points the model's own forward axis along the heading.
        private float GetFacingYawFor(Vector3 heading)
        {
            return WrapRadians(MathF.Atan2(heading.X, heading.Z) + FacingOffsetDegrees.ToRadians());
        }

        private void TurnToward(Vector3 heading, float deltaSeconds)
        {
            var currentYaw = Rotation.Y;
            var difference = WrapRadians(GetFacingYawFor(heading) - currentYaw);
            var maxTurn = TurnDegreesPerSecond.ToRadians() * deltaSeconds;
            var appliedTurn = Math.Clamp(difference, -maxTurn, maxTurn);

            Rotation = new Vector3(Rotation.X, WrapRadians(currentYaw + appliedTurn), Rotation.Z);
        }

        // Turning in place, from DriveTurnInput. Negated because increasing Rotation.Y sweeps
        // FacingDirection from +Z toward +X, which is this rig's left, not its right -- so a
        // positive (turn right) input has to subtract.
        private void ApplyDriveTurn(float deltaSeconds)
        {
            if (MathF.Abs(DriveTurnInput) <= DirectionEpsilon)
            {
                return;
            }

            var turn = -Math.Clamp(DriveTurnInput, -1.0f, 1.0f) * TurnDegreesPerSecond.ToRadians() * deltaSeconds;
            Rotation = new Vector3(Rotation.X, WrapRadians(Rotation.Y + turn), Rotation.Z);
        }

        private static float WrapRadians(float radians)
        {
            var wrapped = MathF.IEEERemainder(radians, MathF.Tau);
            return float.IsNaN(wrapped) ? 0.0f : wrapped;
        }

        // Airborne beats everything else: a character mid-jump should not be running on the spot. The
        // rise/fall split is what makes a jump read as an arc rather than one held pose.
        private string ResolveLocomotionClipName()
        {
            // IsGrounded rather than Body.IsGrounded: the jump lockout has to count as airborne here
            // too, or the take-off frame flashes the idle pose.
            if (Body != null && !IsGrounded)
            {
                var airborneClip = Body.Velocity.Y > 0.0f ? JumpClipName : FallClipName;

                // Not every rig has airborne clips -- the Kenney blocky characters ship walk and
                // sprint but no jump or fall. Idle is the least wrong stand-in; without this the
                // previous clip stays resident and the character runs on the spot in mid-air.
                if (Animation.HasClip(airborneClip))
                {
                    return airborneClip;
                }

                return IdleClipName;
            }

            if (!IsMoving)
            {
                return IdleClipName;
            }

            return WantsToRun ? RunClipName : WalkClipName;
        }

        private void SyncLocomotionClip()
        {
            var desiredClip = ResolveLocomotionClipName();

            if (string.Equals(desiredClip, activeLocomotionClip, StringComparison.Ordinal))
            {
                return;
            }

            // Jump and fall are one-shots -- looping them restarts the take-off pose mid-arc, which
            // reads as a stutter. Everything else is a cycle.
            var isAirborneClip =
                string.Equals(desiredClip, JumpClipName, StringComparison.Ordinal) ||
                string.Equals(desiredClip, FallClipName, StringComparison.Ordinal);

            // Only remember the clip if the rig actually has it, so a rig missing "sprint" keeps
            // retrying against "walk" rather than latching onto a name that never resolved.
            if (Animation.Play(desiredClip))
            {
                Animation.Looping = !isAirborneClip;
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
