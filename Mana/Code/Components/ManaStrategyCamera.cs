namespace Mana
{
    /// <summary>
    /// A strategy view over a map -- a builder's, a board game's: looking down at a point on the ground from a pitch and a
    /// distance, zooming toward what is under the pointer, dragging the ground, panning with keys, turning in quarter
    /// steps, eased (or not) and kept over the map. It drives the main camera through <see cref="Rig"/>, which the game
    /// attaches while it plays (and detaches on Suspend), and reads the ground under the pointer through that view.
    /// </summary>
    public sealed class ManaStrategyCamera
    {
        private readonly ManaContext _context;
        private float _targetYaw;
        private Vector3? _grabbed;
        private (Vector3 Ground, Vector2 Pointer)? _zoomAnchor;

        /// <param name="pitch">How far it looks down, in radians below the horizon (negative).</param>
        public ManaStrategyCamera(ManaContext context, float fieldOfViewDegrees, float pitch)
        {
            _context = context;
            Pitch = pitch;
            Rig = new ManaComputedCameraRig(Pose, fieldOfViewDegrees);
        }

        /// <summary>The rig to attach to the main camera while playing.</summary>
        public ManaComputedCameraRig Rig { get; }

        public float Pitch { get; set; }

        /// <summary>The height of the ground the pointer is read against.</summary>
        public float GroundHeight { get; set; }

        /// <summary>The point looked at; its height is the game's to set, only X and Z are kept over the map.</summary>
        public Vector3 Focus { get; set; }

        /// <summary>How far the view is from the focus now; it eases toward <see cref="TargetDistance"/>.</summary>
        public float Distance { get; private set; } = 30.0f;

        public float TargetDistance { get; private set; } = 30.0f;

        public float MinDistance { get; set; } = 1.0f;

        public float MaxDistance { get; set; } = 1000.0f;

        /// <summary>The turn about the vertical, eased toward the target <see cref="Turn"/> sets.</summary>
        public float Yaw { get; private set; }

        /// <summary>Quarter turns from the start (0..3).</summary>
        public int Quarter { get; private set; }

        /// <summary>The distance kept per wheel notch toward the ground (0.87 = 13% closer).</summary>
        public float ZoomStep { get; set; } = 0.87f;

        /// <summary>How quickly distance and turns settle, per second; 0 moves at once.</summary>
        public float Easing { get; set; } = 10.0f;

        /// <summary>Key panning speed, in distances a second (so it covers as much screen whatever the zoom).</summary>
        public float PanSpeed { get; set; } = 0.9f;

        /// <summary>Where the focus may go (X and Z), or null for anywhere.</summary>
        public (Vector2 Min, Vector2 Max)? Bounds { get; set; }

        /// <summary>Wheel notches to apply at the next <see cref="Update"/>, positive toward the ground.</summary>
        public float PendingScroll { get; set; }

        /// <summary>The camera's place and its pitch, yaw and roll -- what <see cref="Rig"/> asks for each frame.</summary>
        public (Vector3 Position, Vector3 Rotation) Pose() => (Focus - Forward() * Distance, new Vector3(Pitch, Yaw, 0.0f));

        /// <summary>The way it looks.</summary>
        public Vector3 Forward() => Vector3.TransformNormal(-Vector3.UnitZ, Matrix4x4.CreateFromYawPitchRoll(Yaw, Pitch, 0.0f));

        /// <summary>Turns a number of quarter turns (eased).</summary>
        public void Turn(int quarters)
        {
            Quarter = ((Quarter + quarters) % 4 + 4) % 4;
            _targetYaw += quarters * MathF.PI / 2.0f;
        }

        /// <summary>Settles the distance (clamped) at once, or eases to it.</summary>
        public void SetDistance(float distance, bool snap = true)
        {
            TargetDistance = Math.Clamp(distance, MinDistance, MaxDistance);
            if (snap)
            {
                Distance = TargetDistance;
            }
        }

        /// <summary>Ends any easing: the view is where it is heading.</summary>
        public void Snap()
        {
            Distance = TargetDistance;
            Yaw = _targetYaw;
            _zoomAnchor = null;
        }

        /// <summary>
        /// A frame: applies the wheel (toward the ground under <paramref name="pointer"/>, which stays under it), eases,
        /// pans by <paramref name="pan"/> (screen right and up, -1..1), and keeps the focus over the map.
        /// </summary>
        public void Update(float deltaSeconds, Vector2 pan, Vector2? pointer)
        {
            if (PendingScroll != 0.0f)
            {
                TargetDistance = Math.Clamp(TargetDistance * MathF.Pow(ZoomStep, PendingScroll), MinDistance, MaxDistance);
                PendingScroll = 0.0f;
                _zoomAnchor = null;
                if (pointer is { } at && GroundUnder(at) is { } ground)
                {
                    _zoomAnchor = (ground, at);
                }
            }

            var follow = 1.0f;
            if (Easing > 0.0f)
            {
                follow = 1.0f - MathF.Exp(-deltaSeconds * Easing);
            }
            Distance += (Math.Clamp(TargetDistance, MinDistance, MaxDistance) - Distance) * follow;
            Yaw += (_targetYaw - Yaw) * follow;

            // Zooming toward the pointer: the ground it was over stays under it while the distance settles.
            if (_zoomAnchor is { } anchor)
            {
                if (pointer != anchor.Pointer || MathF.Abs(Distance - TargetDistance) < TargetDistance * 1e-3f)
                {
                    _zoomAnchor = null;
                }
                if (pointer == anchor.Pointer && GroundUnder(anchor.Pointer) is { } now)
                {
                    Focus += (anchor.Ground - now) with { Y = 0.0f };
                }
            }

            var right = Vector3.TransformNormal(Vector3.UnitX, Matrix4x4.CreateRotationY(Yaw));
            var ahead = Vector3.TransformNormal(-Vector3.UnitZ, Matrix4x4.CreateRotationY(Yaw));
            Focus += (right * pan.X + ahead * pan.Y) * (deltaSeconds * Distance * PanSpeed);
            KeepOverMap();
        }

        /// <summary>
        /// Drags the ground while <paramref name="held"/>: the point first grabbed stays under the pointer. Call each frame
        /// with the button's state.
        /// </summary>
        public void Drag(bool held, Vector2 pointer)
        {
            if (!held)
            {
                _grabbed = null;
                return;
            }
            if (_grabbed is { } grabbed)
            {
                DragTo(grabbed, pointer);
            }
            else
            {
                _grabbed = GroundUnder(pointer);
            }
        }

        /// <summary>Moves the view so <paramref name="grabbed"/> (a point on the ground) is under the pointer.</summary>
        public void DragTo(Vector3 grabbed, Vector2 pointer)
        {
            if (GroundUnder(pointer) is { } now)
            {
                Focus += (grabbed - now) with { Y = 0.0f };
                KeepOverMap();
            }
        }

        /// <summary>The point on the ground under a screen position, through the view as it is posed now.</summary>
        public Vector3? GroundUnder(Vector2 pointer)
        {
            var camera = _context.World.MainCamera;
            // Posed now rather than at the frame's end, so the ground is read through this view.
            camera.ApplyAttachedPose(0.0f);
            var ray = camera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            if (ray.Direction.Y > -1e-4f)
            {
                return null;
            }
            return ray.Origin + ray.Direction * ((GroundHeight - ray.Origin.Y) / ray.Direction.Y);
        }

        private void KeepOverMap()
        {
            if (Bounds is { } bounds)
            {
                Focus = new Vector3(Math.Clamp(Focus.X, bounds.Min.X, bounds.Max.X), Focus.Y, Math.Clamp(Focus.Z, bounds.Min.Y, bounds.Max.Y));
            }
        }
    }
}
