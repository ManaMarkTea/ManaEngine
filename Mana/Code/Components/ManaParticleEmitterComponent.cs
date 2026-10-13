using System.ComponentModel;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.ManaShaders;
using ManaCore.Graphics;

namespace Mana.Code.Components
{
    public enum ManaParticleShape
    {
        /// <summary>Camera-facing discs with a soft edge -- sparks, smoke, dust, fireflies.</summary>
        Points,

        /// <summary>Discs pulled out along their travel, longer the faster they go -- rain, embers, shooting stars.</summary>
        Streaks,

        /// <summary>Teardrops that stand along their travel with a pointed tip: the tongues of a fire.</summary>
        Flames
    }

    public enum ManaParticleBlend
    {
        /// <summary>Covers what is behind in proportion to alpha: smoke, dust, rain.</summary>
        Alpha,

        /// <summary>Adds light to what is behind, so overlaps brighten: fire, sparks, glows.</summary>
        Additive
    }

    /// <summary>
    /// A stream of small, short-lived shapes thrown from its node: sparks, smoke, dust, rain. They are simulated on
    /// the CPU in world space and drawn as one blended mesh sorted back to front, so a burst costs one draw. Lives
    /// in the editor too, so it can be tuned in the viewport; it is rebuilt from its properties and never saved.
    /// </summary>
    public sealed class ManaParticleEmitterComponent : ManaComponent
    {
        /// <summary>The most alive at once; more than this and the oldest slots are reused.</summary>
        [Category("Emission")]
        public int MaxParticles { get; set; } = 200;

        /// <summary>New particles a second. 0 emits only the burst.</summary>
        [Category("Emission")]
        public float Rate { get; set; } = 40.0f;

        /// <summary>Particles thrown all at once when it starts.</summary>
        [Category("Emission")]
        public int Burst { get; set; }

        /// <summary>How far the stream fans out from the node's up axis, in degrees (180 is every way).</summary>
        [Category("Emission")]
        public float SpreadDegrees { get; set; } = 20.0f;

        /// <summary>Spawn within this radius of the node, in metres.</summary>
        [Category("Emission")]
        public float Radius { get; set; }

        /// <summary>Seconds each lives.</summary>
        [Category("Motion")]
        public float Lifetime { get; set; } = 1.5f;

        /// <summary>Air resistance: the share of speed lost each second. Smoke and embers slow down; sparks keep going.</summary>
        [Category("Motion")]
        public float Drag { get; set; }

        /// <summary>How hard a swirling wind pushes each particle sideways, metres a second squared: the lick of a flame.</summary>
        [Category("Motion")]
        public float Turbulence { get; set; }

        /// <summary>How tight the swirls are, in cycles per metre.</summary>
        [Category("Motion")]
        public float TurbulenceScale { get; set; } = 1.2f;

        /// <summary>Starting speed, metres a second.</summary>
        [Category("Motion")]
        public float Speed { get; set; } = 2.0f;

        /// <summary>How much each lifetime and speed varies, as a share (0.3 is 30% either way).</summary>
        [Category("Motion")]
        public float Variation { get; set; } = 0.3f;

        /// <summary>Acceleration, metres a second squared -- negative Y falls, positive Y rises like smoke.</summary>
        [Category("Motion")]
        public Vector3 Gravity { get; set; } = new(0.0f, -1.0f, 0.0f);

        [Category("Look")]
        public ManaParticleShape Shape { get; set; } = ManaParticleShape.Points;

        /// <summary>Width at birth and at death, metres.</summary>
        [Category("Look")]
        public float StartSize { get; set; } = 0.15f;

        [Category("Look")]
        public float EndSize { get; set; }

        /// <summary>How the particles combine with the picture behind them.</summary>
        [Category("Look")]
        public ManaParticleBlend BlendMode { get; set; } = ManaParticleBlend.Alpha;

        /// <summary>A middle colour the gradient passes through at MidPosition: a flame goes white-hot, orange, red.</summary>
        [Category("Look")]
        public bool UseMidColor { get; set; }

        [Category("Look")]
        public Vector4 MidColor { get; set; } = new(1.0f, 0.5f, 0.1f, 1.0f);

        /// <summary>Where in each particle's life the middle colour (and the size bulge) falls, 0..1.</summary>
        [Category("Look")]
        public float MidPosition { get; set; } = 0.4f;

        /// <summary>Size at MidPosition relative to the straight line from start to end: above 1 swells then shrinks.</summary>
        [Category("Look")]
        public float SizeBulge { get; set; } = 1.0f;

        /// <summary>Steps the colour gradient into this many flat bands for a cel-shaded look; 0 keeps it smooth.</summary>
        [Category("Look")]
        public int ColorSteps { get; set; }

        /// <summary>Flames: how much taller than wide each tongue is.</summary>
        [Category("Look")]
        public float FlameStretch { get; set; } = 2.0f;

        [Category("Look")]
        public Vector4 StartColor { get; set; } = new(1.0f, 0.65f, 0.2f, 1.0f);

        /// <summary>Usually the same colour with no alpha, so they fade out.</summary>
        [Category("Look")]
        public Vector4 EndColor { get; set; } = new(1.0f, 0.2f, 0.05f, 0.0f);

        /// <summary>Scales the colour; above 1 blooms.</summary>
        [Category("Look")]
        public float Brightness { get; set; } = 1.0f;

        /// <summary>Streaks: extra length per metre a second of speed.</summary>
        [Category("Look")]
        public float StreakLength { get; set; } = 0.15f;

        /// <summary>Solid shows each disc to its rim; off fades it to nothing there.</summary>
        [Category("Look")]
        public bool HardEdge { get; set; }

        public bool Enabled { get; set; } = true;

        public int AliveCount { get; private set; }

        private const int RimPoints = 8;
        private const int VerticesPerParticle = RimPoints + 1;

        private readonly Random _random = new();
        private ManaParticleMesh? _mesh;
        private MeshRendererComponent? _renderer;
        private int _builtCapacity;
        private bool _started;
        private float _emitCarry;

        // Alive particles, packed at the front of each array.
        private Vector3[] _position = [];
        private Vector3[] _velocity = [];
        private float[] _age = [];
        private float[] _life = [];
        private float[] _seed = [];
        private float _clock;
        private float[] _sortKey = [];
        private int[] _order = [];

        public ManaParticleEmitterComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Particle Emitter";
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            var capacity = Math.Clamp(MaxParticles, 1, 5000);
            if (_mesh == null || _renderer == null || capacity != _builtCapacity)
            {
                Rebuild(capacity);
            }

            var seconds = ManaTime.FixedFrameSeconds is { } fixedStep
                ? (float)fixedStep
                : Math.Min((float)ManaTime.GlobalTime.DeltaTime.TotalSeconds, 0.1f);
            Step(seconds);
            Publish();
        }

        /// <summary>Advances every particle by <paramref name="seconds"/> and throws new ones. Public so a test can run it without a clock.</summary>
        public void Step(float seconds)
        {
            var capacity = Math.Clamp(MaxParticles, 1, 5000);
            if (_position.Length != capacity)
            {
                _position = new Vector3[capacity];
                _velocity = new Vector3[capacity];
                _age = new float[capacity];
                _life = new float[capacity];
                _seed = new float[capacity];
                _sortKey = new float[capacity];
                _order = new int[capacity];
                AliveCount = 0;
            }

            _clock += seconds;
            var alive = 0;
            var damping = MathF.Max(0.0f, 1.0f - Drag * seconds);
            for (var i = 0; i < AliveCount; i++)
            {
                var age = _age[i] + seconds;
                if (age >= _life[i])
                {
                    continue;
                }

                var velocity = _velocity[i] + Gravity * seconds;
                if (Turbulence > 0.0f)
                {
                    velocity += Swirl(_position[i], _seed[i]) * (Turbulence * seconds);
                }
                velocity *= damping;
                _position[alive] = _position[i] + velocity * seconds;
                _velocity[alive] = velocity;
                _age[alive] = age;
                _life[alive] = _life[i];
                _seed[alive] = _seed[i];
                alive++;
            }
            AliveCount = alive;

            if (!Enabled)
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                for (var i = 0; i < Burst; i++)
                {
                    Spawn();
                }
            }

            _emitCarry += Math.Max(Rate, 0.0f) * seconds;
            while (_emitCarry >= 1.0f)
            {
                _emitCarry -= 1.0f;
                Spawn();
            }
        }

        /// <summary>
        /// Throws <paramref name="count"/> particles now, from where the emitter is -- a pickup's burst, on demand. They are
        /// in the world from then on, so the emitter can move straight on to the next one.
        /// </summary>
        public void Emit(int count)
        {
            if (_position.Length != Math.Clamp(MaxParticles, 1, 5000))
            {
                Step(0.0f);
            }
            for (var i = 0; i < count; i++)
            {
                Spawn();
            }
        }

        // A cheap smooth wind: sines of position and time, offset per particle so neighbours do not move as one.
        private Vector3 Swirl(Vector3 at, float seed)
        {
            var f = TurbulenceScale * MathF.Tau;
            var t = _clock * 1.6f + seed;
            return new Vector3(
                MathF.Sin(at.Y * f + t) + 0.5f * MathF.Sin(at.Z * f * 1.7f - t * 1.3f),
                0.35f * MathF.Sin(at.X * f * 1.3f + t * 0.7f),
                MathF.Sin(at.X * f + t * 1.1f + 2.0f) + 0.5f * MathF.Sin(at.Y * f * 1.9f + t));
        }

        private void Spawn()
        {
            var capacity = _position.Length;
            // Full: reuse the oldest slot (the front is oldest as they are packed in birth order).
            var slot = AliveCount < capacity ? AliveCount++ : 0;
            if (slot == 0 && AliveCount == capacity)
            {
                Array.Copy(_position, 1, _position, 0, capacity - 1);
                Array.Copy(_velocity, 1, _velocity, 0, capacity - 1);
                Array.Copy(_age, 1, _age, 0, capacity - 1);
                Array.Copy(_life, 1, _life, 0, capacity - 1);
                Array.Copy(_seed, 1, _seed, 0, capacity - 1);
                slot = capacity - 1;
            }

            var world = Owner.WorldTransform;
            var axis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, world));
            if (float.IsNaN(axis.X))
            {
                axis = Vector3.UnitY;
            }

            var (side, forward) = Basis(axis);
            var spread = Math.Clamp(SpreadDegrees, 0.0f, 180.0f) * MathF.PI / 180.0f;
            // Uniform over the cap of the cone: pick cos(theta) evenly between 1 and cos(spread).
            var cosTheta = 1.0f - (float)_random.NextDouble() * (1.0f - MathF.Cos(spread));
            var sinTheta = MathF.Sqrt(MathF.Max(0.0f, 1.0f - cosTheta * cosTheta));
            var phi = (float)_random.NextDouble() * MathF.Tau;
            var direction = axis * cosTheta + (side * MathF.Cos(phi) + forward * MathF.Sin(phi)) * sinTheta;

            var origin = world.Translation;
            if (Radius > 0.0f)
            {
                var r = Radius * MathF.Sqrt((float)_random.NextDouble());
                var a = (float)_random.NextDouble() * MathF.Tau;
                origin += (side * MathF.Cos(a) + forward * MathF.Sin(a)) * r;
            }

            _position[slot] = origin;
            _velocity[slot] = direction * Speed * Jitter();
            _age[slot] = 0.0f;
            _life[slot] = MathF.Max(0.05f, Lifetime * Jitter());
            _seed[slot] = (float)_random.NextDouble() * MathF.Tau;
        }

        private float Jitter() => 1.0f + ((float)_random.NextDouble() * 2.0f - 1.0f) * Math.Clamp(Variation, 0.0f, 0.95f);

        private static (Vector3 Side, Vector3 Forward) Basis(Vector3 axis)
        {
            var helper = MathF.Abs(axis.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            var side = Vector3.Normalize(Vector3.Cross(helper, axis));
            return (side, Vector3.Cross(axis, side));
        }

        private void Rebuild(int capacity)
        {
            _builtCapacity = capacity;
            _mesh = new ManaParticleMesh(Owner.Context, capacity, VerticesPerParticle, RimPoints);
            var material = _mesh.Materials[0];
            material.Unlit = true;
            lock (Owner.Context.World.SceneLock)
            {
                if (_renderer != null)
                {
                    Owner.Components.Remove(_renderer);
                }
                _renderer = new MeshRendererComponent(Owner, _mesh);
                Owner.Components.Add(_renderer);
            }
        }

        /// <summary>Writes every particle's quad into the mesh, farthest from the camera first so blending layers right.</summary>
        private void Publish()
        {
            if (_mesh == null)
            {
                return;
            }

            Matrix4x4.Invert(Owner.WorldTransform, out var toLocal);
            var camera = Owner.Context.World.CameraNode;
            var cameraPosition = camera.WorldTransform.Translation;
            var cameraRight = camera.Right;
            var cameraUp = camera.Up;
            var positions = _mesh.Positions;
            var colors = _mesh.Colors;
            _mesh.Materials[0].OpacityMode = BlendMode == ManaParticleBlend.Additive ? ManaOpacityMode.Additive : ManaOpacityMode.Blend;

            for (var i = 0; i < AliveCount; i++)
            {
                _order[i] = i;
                _sortKey[i] = -Vector3.DistanceSquared(_position[i], cameraPosition);
            }
            Array.Sort(_sortKey, _order, 0, AliveCount);

            for (var n = 0; n < AliveCount; n++)
            {
                var i = _order[n];
                var t = _age[i] / _life[i];
                var gradientT = ColorSteps > 1 ? MathF.Min(MathF.Floor(t * ColorSteps), ColorSteps - 1) / (ColorSteps - 1) : t;
                var mid = Math.Clamp(MidPosition, 0.05f, 0.95f);
                var size = StartSize + (EndSize - StartSize) * t;
                // A bump centred on the middle of the life, so a bulge swells and settles rather than kinking.
                size *= 1.0f + (SizeBulge - 1.0f) * MathF.Max(0.0f, 1.0f - MathF.Abs(t - mid) / MathF.Max(mid, 1.0f - mid));
                size *= 0.5f;
                var color = UseMidColor
                    ? (gradientT < mid ? Vector4.Lerp(StartColor, MidColor, gradientT / mid) : Vector4.Lerp(MidColor, EndColor, (gradientT - mid) / (1.0f - mid)))
                    : Vector4.Lerp(StartColor, EndColor, gradientT);
                color = new Vector4(color.X * Brightness, color.Y * Brightness, color.Z * Brightness, color.W);
                var edge = HardEdge ? color : new Vector4(color.X, color.Y, color.Z, 0.0f);

                var right = cameraRight * size;
                var up = cameraUp * size;
                if (Shape == ManaParticleShape.Streaks)
                {
                    var speed = _velocity[i].Length();
                    if (speed > 1e-4f)
                    {
                        var along = _velocity[i] / speed;
                        var view = Vector3.Normalize(cameraPosition - _position[i]);
                        var across = Vector3.Cross(along, view);
                        if (across.LengthSquared() > 1e-8f)
                        {
                            right = Vector3.Normalize(across) * size;
                            up = along * (size + speed * StreakLength * 0.5f);
                        }
                    }
                }

                var tip = 0.0f;
                if (Shape == ManaParticleShape.Flames)
                {
                    // Stands along its travel (mostly up, for a flame), broad at the base and drawn out to a tip.
                    var along = _velocity[i].LengthSquared() > 1e-6f ? Vector3.Normalize(_velocity[i] + Vector3.UnitY * 0.5f) : Vector3.UnitY;
                    var view = Vector3.Normalize(cameraPosition - _position[i]);
                    var across = Vector3.Cross(along, view);
                    if (across.LengthSquared() > 1e-8f)
                    {
                        right = Vector3.Normalize(across) * size;
                        up = along * size;
                        tip = FlameStretch - 1.0f;
                    }
                }

                var first = n * VerticesPerParticle;
                positions[first] = Vector3.Transform(_position[i], toLocal);
                colors[first] = color;
                for (var k = 0; k < RimPoints; k++)
                {
                    var angle = k * MathF.Tau / RimPoints;
                    // A flame's rim is pulled out along its top (angles round 90 degrees) into the tip.
                    var pull = tip > 0.0f ? 1.0f + tip * MathF.Pow(MathF.Max(0.0f, MathF.Sin(angle)), 4.0f) : 1.0f;
                    var world = _position[i] + right * MathF.Cos(angle) + up * (MathF.Sin(angle) * pull);
                    positions[first + 1 + k] = Vector3.Transform(world, toLocal);
                    colors[first + 1 + k] = edge;
                }
            }

            // The rest of the slots collapse to a point, so nothing of a dead particle is left on screen.
            var hidden = Vector3.Zero;
            for (var v = AliveCount * VerticesPerParticle; v < positions.Length; v++)
            {
                positions[v] = hidden;
                colors[v] = Vector4.Zero;
            }

            _mesh.Publish();
        }
    }
}
