using Mana.Code.Models;

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
        public const float Speed = 60.0f;
        public const float Lifetime = 6.0f;
        public const int MaximumPeas = 40;

        /// <summary>The downward pull on a pea, m/s squared, whatever the world's: an arc, but a flat one, so a shot goes near where it is aimed.</summary>
        public const float EffectiveGravity = 7.5f;

        private sealed class Pea
        {
            public required ManaSceneNode Node { get; init; }
            public required BodyHandle Body { get; init; }
            public required Vector3 Last { get; set; }
            public float Age { get; set; }
        }

        private readonly ManaContext _context;
        private readonly ManaScene _scene;
        private readonly ManaModel? _look;
        private ManaSphere? _sphere;
        private readonly List<Pea> _peas = new();

        /// <param name="look">What a pea looks like (the Pea prefab, read once); a plain green ball without one.</param>
        public PeaShooter(ManaContext context, ManaModel? look = null)
        {
            _context = context;
            _look = look;
            _scene = new ManaScene(context) { Name = "Peas" };
            _context.World.Scenes.Add(_scene);
            Effects = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Effects", IsEditorTransient = true };
            _scene.Children.Add(Effects);
        }

        /// <summary>The scene the peas fly in, removed with them: the game's other passing things go here too.</summary>
        public ManaScene Scene => _scene;

        /// <summary>Raised when a pea knocks a target down: the target, and where the pea was.</summary>
        public event Action<ManaSceneNode, Vector3>? Hit;

        /// <summary>A node at the world's origin, in the peas' scene, for what a shot sets off (sparks, fireworks): gone with them.</summary>
        public ManaSceneNode Effects { get; }

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
            var node = MakePea(origin);
            _scene.Children.Add(node);
            node.UpdateTransform();
            // Lifted for the first step too, which the world's full gravity takes before Tick first sees the pea.
            var velocity = Vector3.Normalize(direction) * Speed + Vector3.UnitY * Lift(1.0f / 60.0f);
            var body = _context.Physics.AddDynamicSphereBody(node, Radius, 0.02f, velocity);
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
                var target = range.Sweep(pea.Last, position, Radius);
                if (target != null)
                {
                    Remove(pea);
                    Hit?.Invoke(target, position);
                    continue;
                }
                pea.Last = position;
                var velocity = body.Velocity;
                velocity.Linear.Y += Lift(step);
                body.Velocity = velocity;
                if (pea.Age > Lifetime || position.Y < -50.0f)
                {
                    Remove(pea);
                }
            }
        }

        // A copy of the prefab's look sharing its mesh, or the fallback ball.
        private ManaSceneNode MakePea(Vector3 origin)
        {
            if (_look != null)
            {
                return _look.Instantiate(_context, "Pea", Matrix4x4.CreateTranslation(origin));
            }
            _sphere ??= new ManaSphere(_context, new Vector4(0.45f, 0.85f, 0.25f, 1.0f), Radius) { LatitudeSegments = 10, LongitudeSegments = 12 };
            var node = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(origin)) { Name = "Pea", IsEditorTransient = true };
            node.Components.Add(new MeshRendererComponent(node, _sphere));
            return node;
        }

        // The upward speed a step takes back of the world's gravity, leaving the pea its own.
        private float Lift(float step) => (-_context.Physics.Gravity.Y - EffectiveGravity) * step;

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
