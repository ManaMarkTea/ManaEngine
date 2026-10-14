namespace Mana
{
    /// <summary>
    /// A camera rig whose pose its owner works out each frame -- a board game's view, an arena's framing -- for a game
    /// mode that frames the shot itself but should not have to borrow and restore the camera to do it.
    /// </summary>
    public sealed class ManaComputedCameraRig : IManaCameraRig
    {
        private readonly Func<(Vector3 Position, Vector3 Rotation)> _pose;

        /// <param name="pose">Where the camera is and its pitch, yaw and roll, asked once a frame after the scene has moved.</param>
        /// <param name="fieldOfViewDegrees">The field of view to draw with; null keeps the camera's own.</param>
        public ManaComputedCameraRig(Func<(Vector3 Position, Vector3 Rotation)> pose, float? fieldOfViewDegrees = null)
        {
            _pose = pose;
            FieldOfViewDegrees = fieldOfViewDegrees;
        }

        public float? FieldOfViewDegrees { get; set; }

        public Vector3 CameraPosition { get; private set; }

        public Vector3 CameraRotation { get; private set; }

        public float FieldOfView(float cameraDegrees) => FieldOfViewDegrees ?? cameraDegrees;

        public void Evaluate(float deltaSeconds) => (CameraPosition, CameraRotation) = _pose();

        public void Snap() => Evaluate(0.0f);
    }
}
