using Mana;
using ManaCore.Graphics;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Fits a camera to a planet: flying slows towards the surface, the far plane reaches just past the horizon and the
    /// near plane is a small share of the height, so a true-size planet draws from orbit and from 10 km alike. The
    /// editor and --play both hold one while a level has a planet; <see cref="Release"/> gives the camera back as it was.
    /// </summary>
    public sealed class PlanetCameraFit
    {
        // The planet scales the user's fly speed with height, with the base speed available at ground level.
        private const float SpeedHeightReference = 5.0f;
        private const float MinimumMovementScale = 1.0f;

        // Near plane as a share of the height above the surface, so the depth buffer's range follows the camera down
        // instead of spreading 24 bits over a whole planet. The ground is never nearer than the height, so a few percent
        // clips nothing of it; at half a percent, shores tens of kilometres off fought the water and lines over them.
        private const float NearClipHeightShare = 0.03f;

        // Up follows the ground below this height (a share of the radius, ~640 km on Earth) and is world +Y above the
        // outer one (~3,200 km), turning between: from orbit the globe stays upright, near it the horizon is level.
        private const float SurfaceUpInnerShare = 0.1f;
        private const float SurfaceUpOuterShare = 0.5f;

        // Above this height (a share of the radius) flying gets no faster.
        private const float MaxSpeedHeightShare = 2.0f;

        // The sky's air fades out between these heights (metres): gone by the Karman line, where space is reckoned to start.
        private const float SpaceFadeStartMeters = 30_000.0f;
        private const float KarmanLineMeters = 100_000.0f;

        // The free camera is kept this far above the drawn ground (metres): below it there is only the patches' undersides.
        private const float GroundClearanceMeters = 5.0f;

        private readonly CameraComponent _camera;
        private readonly PlanetNode _planet;
        private readonly float _baseFar;
        private readonly float _baseNear;
        // The drawn ground's distance from the centre below the camera, from the last survey (sea level until one).
        private double _groundRadius;

        public PlanetCameraFit(CameraComponent camera, PlanetNode planet, bool surfaceUp = false)
        {
            _camera = camera;
            _planet = planet;
            _baseFar = camera.FarClipPlane;
            _baseNear = camera.NearClipPlane;
            _groundRadius = planet.Radius;
            // Capped: uncapped, each frame's climb raised the next frame's speed, and a fast climb overflowed in half a second.
            camera.MovementSpeedScale = position => MovementScale(Height(position), _planet.Radius);
            if (surfaceUp)
            {
                camera.UpDirection = Up;
            }
            Update();
        }

        /// <summary>The camera held; a mode that swaps cameras (a debug camera made after loading) fits the new one instead.</summary>
        public CameraComponent Camera => _camera;

        public static float MovementScale(float height, float radius)
        {
            return MathF.Max(MinimumMovementScale, MathF.Min(height, radius * MaxSpeedHeightShare) / SpeedHeightReference);
        }

        public void Release()
        {
            _camera.MovementSpeedScale = null;
            _camera.UpDirection = null;
            _camera.FarClipPlane = _baseFar;
            _camera.NearClipPlane = _baseNear;
            _camera.UpdateProjection();
            _camera.Context.World.IrradianceSettings.Space = 0.0f;
            _camera.Context.World.IrradianceSettings.SkyPlanet = Vector4.Zero;
        }

        /// <summary>Height above the ground below the camera (sea level until surveyed), in world units; never quite zero.</summary>
        public float Height(Vector3 worldPosition)
        {
            var position = new ManaPosition(worldPosition);
            if (worldPosition == _camera.Owner.Position && _camera.PrecisePosition is { } precise)
            {
                position = precise;
            }
            var distance = (position - new ManaPosition(_planet.WorldTransform.Translation)).Length();
            return (float)Math.Max(distance - _groundRadius, _planet.Radius * 1e-6);
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
            KeepAboveGround();
            var position = _camera.Owner.Position;
            var distance = Vector3.Distance(position, _planet.WorldTransform.Translation);
            var horizon = MathF.Sqrt(MathF.Max(0.0f, distance * distance - _planet.Radius * _planet.Radius));
            // A tenth of a radius past the horizon keeps mountains and the far limb from being cut.
            var far = MathF.Max(_baseFar, horizon + _planet.Radius * 0.1f);
            var near = MathF.Max(_baseNear, Height(position) * NearClipHeightShare);
            // Following an aircraft, it is what is near: the plane stops short of it.
            if (_camera.AttachedRig is Flights.FlightFollowRig follow)
            {
                near = MathF.Max(_baseNear, MathF.Min(near, follow.DistanceMetres * 0.3f * _planet.Radius / PlanetLoader.EarthRadiusMeters));
            }
            var heightMeters = Height(position) * PlanetLoader.EarthRadiusMeters / _planet.Radius;
            var space = Math.Clamp((heightMeters - SpaceFadeStartMeters) / (KarmanLineMeters - SpaceFadeStartMeters), 0.0f, 1.0f);
            _camera.Context.World.IrradianceSettings.Space = space * space * (3.0f - 2.0f * space);
            _camera.Context.World.IrradianceSettings.SkyPlanet = new Vector4(_planet.WorldTransform.Translation, _planet.Radius);
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

        // Surveys the ground below and lifts a free camera that has sunk into it straight up out of it.
        private void KeepAboveGround()
        {
            var center = _planet.WorldTransform.Translation;
            var position = _camera.Owner.Position;
            // Only ground drawn finely for the height it is seen from: just after a jump the patch below is still a coarse
            // one, kilometres between corners and hundreds of metres off over hills, and lifting the camera above that
            // left it there for good once the fine ground came in below.
            var survey = _planet.SurveyBelow(position);
            if (survey?.GroundRadius is not { } ground)
            {
                return;
            }
            var heightMetres = (float)Math.Max((position - center).Length() - ground, 0.0) * PlanetLoader.EarthRadiusMeters / _planet.Radius;
            if (survey.Value.VertexMetres > MathF.Max(200.0f, heightMetres * 2.0f))
            {
                return;
            }
            _groundRadius = ground;
            var precise = _camera.PrecisePosition ?? new ManaPosition(position);
            var outward = precise - new ManaPosition(center);
            var distance = outward.Length();
            var floor = _groundRadius + GroundClearanceMeters * (double)_planet.Radius / PlanetLoader.EarthRadiusMeters;
            if (_camera.AttachedRig != null || distance >= floor || distance < 1e-3f)
            {
                return;
            }
            _camera.SetFreePosition(new ManaPosition(center) + outward * (floor / distance));
        }
    }
}
