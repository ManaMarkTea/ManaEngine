using Mana;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Fits a camera to a planet: flying slows towards the surface, the far plane reaches just past the horizon and the
    /// near plane is a small share of the height, so a true-size planet draws from orbit and from 10 km alike. The
    /// editor and --play both hold one while a level has a planet; <see cref="Release"/> gives the camera back as it was.
    /// </summary>
    public sealed class PlanetCameraFit
    {
        // Fly speed is divided by this per unit of height: at the default speed of 50 the camera covers about half its
        // own height a second, from orbit down to rooftops; the speed slider and right-drag scroll scale it.
        private const float SpeedHeightReference = 5.0f;

        // Near plane as a share of the height above the surface, so the depth buffer's range follows the camera down
        // instead of spreading 24 bits over a whole planet.
        private const float NearClipHeightShare = 0.005f;

        // Up follows the ground below this height (a share of the radius, ~640 km on Earth) and is world +Y above the
        // outer one (~3,200 km), turning between: from orbit the globe stays upright, near it the horizon is level.
        private const float SurfaceUpInnerShare = 0.1f;
        private const float SurfaceUpOuterShare = 0.5f;

        private readonly CameraComponent _camera;
        private readonly PlanetNode _planet;
        private readonly float _baseFar;
        private readonly float _baseNear;

        public PlanetCameraFit(CameraComponent camera, PlanetNode planet, bool surfaceUp = false)
        {
            _camera = camera;
            _planet = planet;
            _baseFar = camera.FarClipPlane;
            _baseNear = camera.NearClipPlane;
            camera.MovementSpeedScale = position => Height(position) / SpeedHeightReference;
            if (surfaceUp)
            {
                camera.UpDirection = Up;
            }
            Update();
        }

        /// <summary>The camera held; a mode that swaps cameras (a debug camera made after loading) fits the new one instead.</summary>
        public CameraComponent Camera => _camera;

        public void Release()
        {
            _camera.MovementSpeedScale = null;
            _camera.UpDirection = null;
            _camera.FarClipPlane = _baseFar;
            _camera.NearClipPlane = _baseNear;
            _camera.UpdateProjection();
        }

        /// <summary>Height above the planet's sea-level sphere, in world units; a sliver above zero inside it.</summary>
        public float Height(Vector3 worldPosition)
        {
            var distance = Vector3.Distance(worldPosition, _planet.WorldTransform.Translation);
            return MathF.Max(distance - _planet.Radius, _planet.Radius * 1e-6f);
        }

        /// <summary>The camera's up at a world position: the way out from the planet's centre near it, world +Y far off.</summary>
        public Vector3 Up(Vector3 worldPosition)
        {
            var outward = worldPosition - _planet.WorldTransform.Translation;
            if (outward.LengthSquared() < 1e-6f)
            {
                return Vector3.UnitY;
            }
            outward = Vector3.Normalize(outward);
            var share = Height(worldPosition) / _planet.Radius;
            var t = Math.Clamp((SurfaceUpOuterShare - share) / (SurfaceUpOuterShare - SurfaceUpInnerShare), 0.0f, 1.0f);
            t = t * t * (3.0f - 2.0f * t);
            // Turned part way from +Y toward the outward direction (a lerp of the two vanishes over the south pole).
            var turn = Quaternion.Slerp(Quaternion.Identity, CameraComponent.RotationBetween(Vector3.UnitY, outward), t);
            return Vector3.Transform(Vector3.UnitY, turn);
        }

        /// <summary>Once a frame: far plane just past the horizon (the level's own far a floor), near plane a share of the height.</summary>
        public void Update()
        {
            var position = _camera.Owner.Position;
            var distance = Vector3.Distance(position, _planet.WorldTransform.Translation);
            var horizon = MathF.Sqrt(MathF.Max(0.0f, distance * distance - _planet.Radius * _planet.Radius));
            // A tenth of a radius past the horizon keeps mountains and the far limb from being cut.
            var far = MathF.Max(_baseFar, horizon + _planet.Radius * 0.1f);
            var near = MathF.Max(_baseNear, Height(position) * NearClipHeightShare);
            if (MathF.Abs(far - _camera.FarClipPlane) > far * 0.01f || MathF.Abs(near - _camera.NearClipPlane) > near * 0.01f)
            {
                _camera.FarClipPlane = far;
                _camera.NearClipPlane = near;
                _camera.UpdateProjection();
            }

            // --play's free camera is only re-aimed when it flies; until then it would draw the planet from its old pose.
            if (_camera.AttachedRig == null)
            {
                _camera.UpdateViewMatrix();
            }
        }
    }
}
