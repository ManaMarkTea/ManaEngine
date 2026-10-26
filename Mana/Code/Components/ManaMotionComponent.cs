using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Moves its node -- and everything beneath, lights included -- without touching where it was placed: a spin about
    /// an axis, a swing back and forth, a glide to and fro. The motion is laid over the node's saved position and
    /// rotation each frame (its local transform), never written into them, so a level stays unchanged in the editor
    /// and dragging the node moves where it swings from. Driven by game time, so it plays in the editor as in play.
    /// </summary>
    public sealed class ManaMotionComponent : ManaComponent
    {
        /// <summary>The axis it spins about, in its own frame.</summary>
        public Vector3 SpinAxis { get; set; } = Vector3.UnitY;

        /// <summary>Degrees a second; 0 does not spin.</summary>
        public float SpinSpeed { get; set; }

        /// <summary>The axis it swings about, in its own frame.</summary>
        public Vector3 SwingAxis { get; set; } = Vector3.UnitX;

        /// <summary>How far it swings each way, in degrees; 0 does not swing.</summary>
        public float SwingAngle { get; set; }

        /// <summary>Swings a second (there and back).</summary>
        public float SwingSpeed { get; set; } = 0.5f;

        /// <summary>The line it glides along, in its own frame.</summary>
        public Vector3 MoveAxis { get; set; } = Vector3.UnitX;

        /// <summary>How far it glides each way, in metres; 0 does not glide.</summary>
        public float MoveDistance { get; set; }

        /// <summary>Glides a second (there and back).</summary>
        public float MoveSpeed { get; set; } = 0.25f;

        /// <summary>Puts this one ahead of others, in seconds: a row out of step, a wave along it.</summary>
        public float Phase { get; set; }

        public ManaMotionComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Motion";
        }

        // Before the node works out its world transform, so the motion is in this frame's.
        public override bool UpdatesBeforeNodeTransform => true;

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            Owner.LocalTransform = LocalAt(ManaTime.GlobalTime.GameTime.TotalSeconds + Phase);
        }

        /// <summary>The node's local transform with the motion laid over it, at a moment.</summary>
        public Matrix4x4 LocalAt(double seconds)
        {
            var owner = Owner;
            var turn = Quaternion.Identity;
            if (SpinSpeed != 0.0f && SpinAxis.LengthSquared() > 1e-8f)
            {
                var angle = (float)(seconds * SpinSpeed % 360.0) * MathF.PI / 180.0f;
                turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(SpinAxis), angle);
            }
            if (SwingAngle != 0.0f && SwingAxis.LengthSquared() > 1e-8f)
            {
                var angle = SwingAngle * MathF.PI / 180.0f * (float)Math.Sin(seconds * SwingSpeed * Math.Tau);
                turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(SwingAxis), angle) * turn;
            }

            var placed = owner.Rotation.ToQuaternionFromEuler();
            var position = owner.Position;
            if (MoveDistance != 0.0f && MoveAxis.LengthSquared() > 1e-8f)
            {
                var along = MoveDistance * (float)Math.Sin(seconds * MoveSpeed * Math.Tau);
                position += Vector3.Transform(Vector3.Normalize(MoveAxis) * along, placed);
            }

            // Scale, then the motion in the node's own frame, then the node as placed.
            return Matrix4x4.CreateScale(owner.Scale)
                * Matrix4x4.CreateFromQuaternion(turn)
                * Matrix4x4.CreateFromQuaternion(placed)
                * Matrix4x4.CreateTranslation(position);
        }
    }
}
