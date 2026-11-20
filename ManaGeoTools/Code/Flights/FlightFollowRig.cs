using Mana;
using Mana.Code;
using Mana.Code.Components;
using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>
    /// The camera on a picked aircraft: it glides there from wherever it was (round the planet, never through it,
    /// climbing on a long trip and turning to look at the aircraft early on), then rides behind it. A right-drag orbits,
    /// the wheel moves in and out. <see cref="FlightTrafficComponent"/> attaches and lets go of it.
    /// </summary>
    public sealed class FlightFollowRig : IManaCameraLookRig
    {
        private const float GlideSeconds = 2.5f;

        private readonly PlanetNode _planet;
        private readonly FlightTrafficComponent _traffic;
        private readonly Vector3 _startPosition;
        private readonly Quaternion _startTurn;
        private float _elapsed;
        private float _yaw = 0.35f;
        private float _pitch = 0.22f;
        private float _distanceMetres;
        private readonly float _closestMetres;

        public FlightFollowRig(PlanetNode planet, FlightTrafficComponent traffic, string icao24, float lengthMetres,
            Vector3 startPosition, Vector3 startRotation)
        {
            _planet = planet;
            _traffic = traffic;
            Icao24 = icao24;
            _startPosition = startPosition;
            _startTurn = startRotation.ToQuaternionFromEuler();
            _closestMetres = lengthMetres * 1.2f;
            _distanceMetres = lengthMetres * 2.5f;
            CameraPosition = startPosition;
            CameraRotation = startRotation;
        }

        public string Icao24 { get; }

        /// <summary>How far behind the aircraft the camera rides, in metres.</summary>
        public float DistanceMetres => _distanceMetres;

        /// <summary>The aircraft has left the feed (or another was picked): nothing to follow.</summary>
        public bool Lost { get; private set; }

        public Vector3 CameraPosition { get; private set; }

        public Vector3 CameraRotation { get; private set; }

        public bool MouseLookControls => false;

        public bool IsSteerGestureActive { get; set; }

        public bool IsOrbitGestureActive { get; set; }

        public float FieldOfView(float cameraDegrees) => cameraDegrees;

        public void Snap()
        {
        }

        public void AddLookInput(float yawRadians, float pitchRadians)
        {
            _yaw -= yawRadians;
            _pitch = Math.Clamp(_pitch + pitchRadians, -0.3f, 1.45f);
        }

        public void AddZoomInput(float notches)
        {
            _distanceMetres = Math.Clamp(_distanceMetres * MathF.Pow(0.85f, notches), _closestMetres, 50_000.0f);
        }

        public void Evaluate(float deltaSeconds)
        {
            if (_traffic.SelectedIcao24 != Icao24 || _traffic.Selected is not { } state)
            {
                Lost = true;
                return;
            }
            _elapsed += deltaSeconds;

            var world = _planet.WorldTransform;
            var centre = world.Translation;
            var (local, localHeading) = _traffic.Locate(_planet, state);
            var target = Vector3.Transform(local, world);
            var up = Vector3.Normalize(Vector3.TransformNormal(local, world));
            var heading = Vector3.TransformNormal(localHeading, world);
            heading = Vector3.Normalize(heading - up * Vector3.Dot(heading, up));
            var right = Vector3.Normalize(Vector3.Cross(heading, up));

            // Behind it, turned by the orbit and raised by its pitch.
            var unitsPerMetre = _planet.Radius / PlanetLoader.EarthRadiusMeters;
            var away = -heading * (MathF.Cos(_pitch) * MathF.Cos(_yaw)) + right * (MathF.Cos(_pitch) * MathF.Sin(_yaw)) + up * MathF.Sin(_pitch);
            var follow = target + away * (_distanceMetres * unitsPerMetre);

            var t = Math.Clamp(_elapsed / GlideSeconds, 0.0f, 1.0f);
            var position = follow;
            if (t < 1.0f)
            {
                position = Glide(centre, Ease(t), follow);
            }
            else if (_traffic.SelectedModel is { } model)
            {
                // Riding on the model as drawn, by an offset on the float grid where it is: the two then move by exactly the
                // same amount, and the shaders' model-less-camera comes out the same every frame. Each placed on the grid
                // by itself, they landed up to half a metre apart, a few pixels at this distance, and the aircraft shook.
                target = model.WorldTransform.Translation;
                position = target + Snap(follow - target, target);
            }
            // Never under the drawn ground: an aircraft landing can be lower than the patches put the runway.
            if (_planet.SurveyBelow(position)?.GroundRadius is { } ground)
            {
                var outwardNow = position - centre;
                var floor = ground + 5.0f * unitsPerMetre;
                if (outwardNow.Length() < floor)
                {
                    position = centre + Vector3.Normalize(outwardNow) * floor;
                }
            }
            CameraPosition = position;

            // Looking at the aircraft, with the planet's up; turned to it over the first part of the glide.
            var outward = Vector3.Normalize(position - centre);
            var look = Look(target - position, outward);
            var turn = Math.Clamp(t / 0.45f, 0.0f, 1.0f);
            if (turn < 1.0f)
            {
                look = Quaternion.Slerp(_startTurn, look, Ease(turn));
            }
            CameraRotation = look.ToEuler();
        }

        private static float Ease(float t) => 0.5f - 0.5f * MathF.Cos(t * MathF.PI);

        private static Vector3 Snap(Vector3 offset, Vector3 at)
        {
            return new Vector3(Snap(offset.X, at.X), Snap(offset.Y, at.Y), Snap(offset.Z, at.Z));
        }

        // To a whole number of the float spacing at `at` (one unit in the last place of its 24-bit significand).
        private static float Snap(float offset, float at)
        {
            var magnitude = MathF.Abs(at);
            if (magnitude < 1.0f)
            {
                return offset;
            }
            var spacing = MathF.Pow(2.0f, MathF.Floor(MathF.Log2(magnitude)) - 23.0f);
            return MathF.Round(offset / spacing) * spacing;
        }

        // Round the planet from the start to where it follows: the direction turned along the great circle, the height
        // eased on a log scale (a dive from orbit slows as it nears), lifted on a long trip so it arcs over.
        private Vector3 Glide(Vector3 centre, float e, Vector3 follow)
        {
            var radius = _planet.Radius;
            var from = _startPosition - centre;
            var to = follow - centre;
            var fromDirection = Vector3.Normalize(from);
            var toDirection = Vector3.Normalize(to);
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(fromDirection, toDirection), -1.0f, 1.0f));
            Vector3 direction;
            if (angle < 1e-5f)
            {
                direction = Vector3.Normalize(Vector3.Lerp(fromDirection, toDirection, e));
            }
            else
            {
                var sin = MathF.Sin(angle);
                direction = fromDirection * (MathF.Sin((1.0f - e) * angle) / sin) + toDirection * (MathF.Sin(e * angle) / sin);
            }
            var floor = radius * 1e-6f;
            var fromHeight = MathF.Max(from.Length() - radius, floor);
            var toHeight = MathF.Max(to.Length() - radius, floor);
            var height = MathF.Exp(float.Lerp(MathF.Log(fromHeight), MathF.Log(toHeight), e));
            height += MathF.Sin(e * MathF.PI) * angle * radius * 0.25f;
            return centre + direction * (radius + height);
        }

        // A camera turned to look along forward with up as near the planet's up as it can be (it looks along -Z).
        private static Quaternion Look(Vector3 forward, Vector3 up)
        {
            forward = Vector3.Normalize(forward);
            var right = Vector3.Cross(forward, up);
            if (right.LengthSquared() < 1e-8f)
            {
                right = Vector3.Cross(forward, Vector3.UnitX);
            }
            right = Vector3.Normalize(right);
            var cameraUp = Vector3.Cross(right, forward);
            var basis = new Matrix4x4(
                right.X, right.Y, right.Z, 0.0f,
                cameraUp.X, cameraUp.Y, cameraUp.Z, 0.0f,
                -forward.X, -forward.Y, -forward.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
        }
    }
}
