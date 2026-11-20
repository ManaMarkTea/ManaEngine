using Mana;
using Mana.Code;
using Mana.Code.Components;
using ManaCore.Graphics;
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
        private readonly ManaPosition _startPosition;
        private readonly Quaternion _startTurn;
        private float _elapsed;
        private float _yaw = 0.35f;
        private float _pitch = 0.22f;
        private float _distanceMetres;
        private readonly float _closestMetres;

        public FlightFollowRig(PlanetNode planet, FlightTrafficComponent traffic, string icao24, float lengthMetres,
            ManaPosition startPosition, Vector3 startRotation)
        {
            _planet = planet;
            _traffic = traffic;
            Icao24 = icao24;
            _startPosition = startPosition;
            _startTurn = startRotation.ToQuaternionFromEuler();
            _closestMetres = lengthMetres * 1.2f;
            _distanceMetres = lengthMetres * 2.5f;
            PreciseCameraPosition = startPosition;
            CameraRotation = startRotation;
        }

        public string Icao24 { get; }

        /// <summary>How far behind the aircraft the camera rides, in metres.</summary>
        public float DistanceMetres => _distanceMetres;

        /// <summary>The aircraft has left the feed (or another was picked): nothing to follow.</summary>
        public bool Lost { get; private set; }

        public ManaPosition? PreciseCameraPosition { get; private set; }

        public Vector3 CameraPosition => PreciseCameraPosition.GetValueOrDefault().ToVector3();

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
            var centre = _planet.PreciseWorldPosition ?? new ManaPosition(world.Translation);
            var (local, localHeading, localUp) = _traffic.SelectedPose(_planet, state);
            var target = local.TransformOffset(world) + centre;
            var up = Vector3.Normalize(Vector3.TransformNormal(localUp, world));
            var heading = Vector3.TransformNormal(localHeading, world);
            heading = Vector3.Normalize(heading - up * Vector3.Dot(heading, up));
            var right = Vector3.Normalize(Vector3.Cross(heading, up));

            // Behind it, turned by the orbit and raised by its pitch.
            var unitsPerMetre = _planet.Radius / PlanetLoader.EarthRadiusMeters;
            var away = -heading * (MathF.Cos(_pitch) * MathF.Cos(_yaw)) + right * (MathF.Cos(_pitch) * MathF.Sin(_yaw)) + up * MathF.Sin(_pitch);
            var offset = new ManaPosition(away) * (_distanceMetres * (double)unitsPerMetre);
            var follow = target + offset;

            var t = Math.Clamp(_elapsed / GlideSeconds, 0.0f, 1.0f);
            var position = follow;
            var lookDirection = offset * -1.0;
            if (t < 1.0f)
            {
                position = Glide(centre, Ease(t), follow);
                lookDirection = target - position;
            }
            // Never under the drawn ground: an aircraft landing can be lower than the patches put the runway.
            if (_planet.SurveyBelow(position.ToVector3())?.GroundRadius is { } ground)
            {
                var outwardNow = position - centre;
                var floor = ground + 5.0 * unitsPerMetre;
                if (outwardNow.Length() < floor)
                {
                    position = centre + outwardNow.Normalized() * floor;
                    lookDirection = target - position;
                }
            }
            PreciseCameraPosition = position;

            // Looking at the aircraft, with the planet's up; turned to it over the first part of the glide.
            var outward = (position - centre).Normalized().ToVector3();
            var look = Look(lookDirection.ToVector3(), outward);
            var turn = Math.Clamp(t / 0.45f, 0.0f, 1.0f);
            if (turn < 1.0f)
            {
                look = Quaternion.Slerp(_startTurn, look, Ease(turn));
            }
            CameraRotation = look.ToEuler();
        }

        private static float Ease(float t) => 0.5f - 0.5f * MathF.Cos(t * MathF.PI);

        // Round the planet from the start to where it follows: the direction turned along the great circle, the height
        // eased on a log scale (a dive from orbit slows as it nears), lifted on a long trip so it arcs over.
        private ManaPosition Glide(ManaPosition centre, double e, ManaPosition follow)
        {
            var radius = _planet.Radius;
            var from = _startPosition - centre;
            var to = follow - centre;
            var fromLength = from.Length();
            var toLength = to.Length();
            if (fromLength == 0.0)
            {
                return centre + to * e;
            }
            var fromDirection = from * (1.0 / fromLength);
            var toDirection = to * (1.0 / toLength);
            var dot = Math.Clamp(ManaPosition.Dot(fromDirection, toDirection), -1.0, 1.0);
            var angle = Math.Acos(dot);
            ManaPosition direction;
            if (angle < 1e-5)
            {
                direction = (fromDirection * (1.0 - e) + toDirection * e).Normalized();
            }
            else
            {
                var tangent = toDirection - fromDirection * dot;
                if (tangent.Length() < 1e-12)
                {
                    var axis = Vector3.UnitY;
                    if (Math.Abs(fromDirection.Y) > 0.9)
                    {
                        axis = Vector3.UnitX;
                    }
                    tangent = new ManaPosition(Vector3.Cross(fromDirection.ToVector3(), axis));
                }
                direction = fromDirection * Math.Cos(e * angle) + tangent.Normalized() * Math.Sin(e * angle);
            }
            var floor = radius * 1e-6;
            var fromHeight = Math.Max(fromLength - radius, floor);
            var toHeight = Math.Max(toLength - radius, floor);
            var height = Math.Exp(double.Lerp(Math.Log(fromHeight), Math.Log(toHeight), e));
            height += Math.Sin(e * Math.PI) * angle * radius * 0.25;
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
