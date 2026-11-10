namespace TargetPractice
{
    /// <summary>
    /// Turns the character to the way the camera looks and raises its gun arm along <see cref="Aim"/>. It runs after the
    /// walk animation has posed the arm and before the transforms are worked out, so it overrides that one arm while the
    /// legs and the other arm still walk.
    /// </summary>
    internal sealed class AimingArm : ManaComponent
    {
        private readonly ManaPawnNode _pawn;

        public AimingArm(ManaPawnNode pawn, ManaSceneNode arm) : base(pawn)
        {
            _pawn = pawn;
            Arm = arm;
        }

        /// <summary>The arm node: it pivots at the shoulder and hangs down its own -Y to the hand.</summary>
        public ManaSceneNode Arm { get; }

        /// <summary>Where the arm points, in the world.</summary>
        public Vector3 Aim { get; set; } = -Vector3.UnitZ;

        public override bool UpdatesBeforeNodeTransform => true;

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            var aim = Aim;
            if (aim.LengthSquared() < 1e-6f)
            {
                return;
            }
            aim = Vector3.Normalize(aim);

            var flat = new Vector3(aim.X, 0.0f, aim.Z);
            if (flat.LengthSquared() > 1e-6f)
            {
                var rotation = _pawn.Rotation;
                _pawn.Rotation = new Vector3(rotation.X, MathF.Atan2(flat.X, flat.Z) + _pawn.FacingOffsetDegrees.ToRadians(), rotation.Z);
            }
            PoseArm(aim);
        }

        private void PoseArm(Vector3 aim)
        {
            // The shoulder's rotation as it will be this frame: the pawn's new heading, under the local rotations down
            // to the shoulder that the animation has just written.
            var chain = Matrix4x4.Identity;
            for (var node = Arm.Parent; node != null && node != _pawn; node = node.Parent)
            {
                chain *= node.LocalTransform;
            }
            chain *= Matrix4x4.CreateFromQuaternion(_pawn.Rotation.ToQuaternionFromEuler());
            if (!Matrix4x4.Decompose(chain, out _, out var shoulder, out _))
            {
                return;
            }

            // The arm's world axes: -Y along the aim (the hand end), +Z (its front when it hangs) turned up, X across.
            var y = -aim;
            var z = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, y) * y;
            if (z.LengthSquared() < 1e-4f)
            {
                z = new Vector3(MathF.Sin(_pawn.Rotation.Y), 0.0f, MathF.Cos(_pawn.Rotation.Y));
            }
            z = Vector3.Normalize(z);
            var x = Vector3.Cross(y, z);
            var world = new Matrix4x4(
                x.X, x.Y, x.Z, 0.0f,
                y.X, y.Y, y.Z, 0.0f,
                z.X, z.Y, z.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            var local = world * Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(shoulder));

            Matrix4x4.Decompose(Arm.LocalTransform, out var scale, out _, out var position);
            Arm.LocalTransform = Matrix4x4.CreateScale(scale) * local * Matrix4x4.CreateTranslation(position);
        }
    }
}
