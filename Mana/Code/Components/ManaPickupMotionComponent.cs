using Mana.Code.Nodes;
using Mana.Code;

namespace Mana
{
    /// <summary>
    /// Spins a node and bobs it up and down, the way a collectable pickup sits in the world.
    /// <para>
    /// Deliberately has no inspector widget of its own. It is the working example of the reflection
    /// fallback: select the node and every property below shows up as an editor, live in both directions,
    /// without anyone having written a line of UI for it.
    /// </para>
    /// </summary>
    public class ManaPickupMotionComponent : ManaComponent
    {
        // Where the node sits when it is not being bobbed. Captured from the node rather than passed in,
        // so dropping this on anything already placed just works.
        private Vector3 _restPosition;
        private bool _hasRest;

        // What this component last wrote. Anything else moving the node -- a drag in the inspector, a
        // gizmo -- leaves the node somewhere other than this, which is the signal to take the new spot as
        // the rest position instead of yanking it back.
        private Vector3 _lastApplied;

        private double _elapsedSeconds;

        public ManaPickupMotionComponent(ManaSceneNode owner) : base(owner)
        {
            _restPosition = owner.Position;
            _lastApplied = owner.Position;
            _hasRest = true;
        }

        public bool Spins { get; set; } = true;

        public float DegreesPerSecond { get; set; } = 90.0f;

        public bool Bobs { get; set; } = true;

        /// <summary>How far above and below the rest position it travels.</summary>
        public float BobHeight { get; set; } = 0.35f;

        public float BobsPerSecond { get; set; } = 0.5f;

        /// <summary>Offsets the cycle, so a row of pickups is not in lockstep.</summary>
        public float PhaseSeconds { get; set; }

        /// <summary>Stops the motion without removing the component, leaving it where it stands.</summary>
        public bool Paused { get; set; }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var owner = Owner;
            if (owner == null)
            {
                return;
            }

            if (!_hasRest)
            {
                _restPosition = owner.Position;
                _lastApplied = owner.Position;
                _hasRest = true;
            }

            // Moved by something else since the last tick, so that is where it lives now.
            if (owner.Position != _lastApplied)
            {
                _restPosition = owner.Position;
            }

            if (Paused)
            {
                _lastApplied = owner.Position;
                return;
            }

            var delta = ManaTime.GlobalTime.DeltaTime.TotalSeconds;
            _elapsedSeconds += delta;

            if (Spins)
            {
                var turned = owner.Rotation;
                turned.Y += (float)(DegreesPerSecond * delta * Math.PI / 180.0);

                // No wrapping here: the node's Rotation setter folds it into (-pi, pi], so nothing that
                // turns a node every frame has to remember to.
                owner.Rotation = turned;
            }

            if (Bobs && BobHeight != 0.0f)
            {
                var cycle = (_elapsedSeconds + PhaseSeconds) * BobsPerSecond * Math.PI * 2.0;
                var height = MathF.Sin((float)cycle) * BobHeight;

                owner.Position = _restPosition + new Vector3(0.0f, height, 0.0f);
            }

            // Read back rather than assuming: the setter ignores a write that matches what is already
            // there, and the comparison above has to agree with what the node actually holds.
            _lastApplied = owner.Position;

            // Not calling UpdateTransform here on purpose -- it walks children and components, so calling
            // it from inside a component's own tick would recurse straight back into this. The setters have
            // already flagged the node, so the next frame's pass rebuilds the matrices.
        }
    }
}
