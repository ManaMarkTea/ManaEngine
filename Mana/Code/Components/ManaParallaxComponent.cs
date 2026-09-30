using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A background layer that lags the camera while playing, so far scenery slides by slower than the level:
    /// <see cref="Factor"/> 0 moves with the level, 1 stays pinned to the camera like the sky. The depth a
    /// perspective view would give for free, which an orthographic one has none of. Back where it was on stop.
    /// </summary>
    public sealed class ManaParallaxComponent : ManaComponent
    {
        /// <summary>How much of the camera's movement the layer follows, 0..1, per axis (x across, y up).</summary>
        public Vector2 Factor { get; set; } = new(0.5f, 0.5f);

        private Vector3? _startPosition;
        private Vector3 _startCamera;

        public ManaParallaxComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Parallax";
        }

        public override void BeginPlay()
        {
            base.BeginPlay();
            _startPosition = Owner.Position;
            _startCamera = CameraPosition();
        }

        public override void EndPlay()
        {
            base.EndPlay();
            if (_startPosition is { } start)
            {
                Owner.Position = start;
                _startPosition = null;
            }
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (_startPosition is not { } start)
            {
                return;
            }

            Owner.Position = OffsetFor(start, CameraPosition() - _startCamera);
        }

        /// <summary>Where the layer sits once the camera has moved by <paramref name="cameraMoved"/> since play began.</summary>
        public Vector3 OffsetFor(Vector3 start, Vector3 cameraMoved) =>
            start + new Vector3(cameraMoved.X * Factor.X, cameraMoved.Y * Factor.Y, 0.0f);

        private Vector3 CameraPosition() => Owner.Context.World.CameraNode?.WorldTransform.Translation ?? Vector3.Zero;
    }
}
