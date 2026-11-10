namespace TargetPractice
{
    /// <summary>
    /// Peas fired for real: each is a ball in the physics world, launched at <see cref="Speed"/> along the aim, arcing
    /// under gravity, bouncing off the floor, crates and walls, and knocking down any target it touches on the way.
    /// Nodes live in a scene of their own that <see cref="Clear"/> removes, so nothing is ever saved into the level.
    /// </summary>
    internal sealed class PeaShooter
    {
        public const float Radius = 0.12f;
        public const float Speed = 45.0f;
        public const float Lifetime = 6.0f;
        public const int MaximumPeas = 40;

        // The world's gravity (-64) is a platformer's; a pea feels this share of it, which gives a gentle arc.
        private const float GravityShare = 0.2f;

        /// <summary>The downward pull on a pea, m/s squared.</summary>
        public static float EffectiveGravity => 64.0f * GravityShare;

        private sealed class Pea
        {
            public required ManaSceneNode Node { get; init; }
            public required BodyHandle Body { get; init; }
            public required Vector3 Last { get; set; }
            public float Age { get; set; }
        }

        private readonly ManaContext _context;
        private readonly ManaScene _scene;
        private readonly ManaSphere _mesh;
        private readonly List<Pea> _peas = new();

        public PeaShooter(ManaContext context)
        {
            _context = context;
            _scene = new ManaScene(context) { Name = "Peas" };
            _context.World.Scenes.Add(_scene);
            _mesh = new ManaSphere(context, new Vector4(0.45f, 0.85f, 0.25f, 1.0f), Radius) { LatitudeSegments = 10, LongitudeSegments = 12 };
        }

        public int InFlight => _peas.Count;

        /// <summary>The peas' nodes, for a ray that should look past them.</summary>
        public IEnumerable<ManaSceneNode> Nodes => _peas.Select(p => p.Node);

        public IEnumerable<Vector3> Positions => _peas.Select(p => _context.Physics.Simulation.Bodies[p.Body].Pose.Position);

        /// <summary>Spits a pea from <paramref name="origin"/> along <paramref name="direction"/>.</summary>
        public void Fire(Vector3 origin, Vector3 direction)
        {
            if (_peas.Count >= MaximumPeas)
            {
                Remove(_peas[0]);
            }
            var node = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(origin)) { Name = "Pea" };
            node.Components.Add(new MeshRendererComponent(node, _mesh));
            _scene.Children.Add(node);
            node.UpdateTransform();
            var body = _context.Physics.AddDynamicSphereBody(node, Radius, 0.02f, Vector3.Normalize(direction) * Speed);
            _peas.Add(new Pea { Node = node, Body = body, Last = origin });
        }

        /// <summary>
        /// Lightens each pea's gravity, ages it, and asks <paramref name="range"/> whether it touched a target since the
        /// last frame (which uses it up). Call after the physics has stepped.
        /// </summary>
        public void Tick(float deltaSeconds, TargetRange range)
        {
            var simulation = _context.Physics.Simulation;
            // The step the physics really took (frame time, clamped), so the lifted gravity matches it on a slow frame.
            var step = _context.Physics.LastTimestep;
            if (step <= 0.0f)
            {
                step = deltaSeconds;
            }
            foreach (var pea in _peas.ToList())
            {
                pea.Age += deltaSeconds;
                if (!simulation.Bodies.BodyExists(pea.Body))
                {
                    Remove(pea);
                    continue;
                }
                var body = simulation.Bodies[pea.Body];
                body.Awake = true;
                var position = body.Pose.Position;
                if (range.Sweep(pea.Last, position, Radius))
                {
                    Remove(pea);
                    continue;
                }
                pea.Last = position;
                var velocity = body.Velocity;
                velocity.Linear.Y += -_context.Physics.Gravity.Y * (1.0f - GravityShare) * step;
                body.Velocity = velocity;
                if (pea.Age > Lifetime || position.Y < -50.0f)
                {
                    Remove(pea);
                }
            }
        }

        public void Clear()
        {
            foreach (var pea in _peas.ToList())
            {
                Remove(pea);
            }
        }

        /// <summary>Removes every pea and the scene they were in.</summary>
        public void Dispose()
        {
            Clear();
            _context.World.Scenes.Remove(_scene);
        }

        private void Remove(Pea pea)
        {
            _peas.Remove(pea);
            if (_context.Physics.Simulation.Bodies.BodyExists(pea.Body))
            {
                _context.Physics.RemoveBody(pea.Body);
            }
            _scene.Children.Remove(pea.Node);
        }
    }
}
