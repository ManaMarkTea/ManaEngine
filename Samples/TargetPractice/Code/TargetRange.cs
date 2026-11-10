namespace TargetPractice
{
    /// <summary>
    /// The game's rules, with no window or input: the targets a level holds, which of them are down, and the clock. A
    /// target is any node whose name starts with the prefix ("Target 1", "Target Roof") -- an instance of one of the
    /// Target prefabs, or anything else so named; a shot that hits it knocks it down. Kept apart from <see cref="TargetPracticeMode"/> so the tests can
    /// play it with a ray and no keyboard.
    /// </summary>
    internal sealed class TargetRange
    {
        private sealed class Target
        {
            public required ManaSceneNode Node { get; init; }
            public required Vector3 HomeScale { get; init; }
            public required List<MeshRendererComponent> Renderers { get; init; }

            /// <summary>Where it was at the last tick, and how fast it is going (a Moving Target glides).</summary>
            public Vector3? Last { get; set; }

            public Vector3 Velocity { get; set; }

            /// <summary>Seconds since it was hit; negative while it still stands.</summary>
            public float Age { get; set; } = -1.0f;

            public bool Down => Age >= 0.0f;
        }

        private readonly List<Target> _targets = new();
        private readonly float _popSeconds;

        private TargetRange(float popSeconds)
        {
            _popSeconds = Math.Max(popSeconds, 0.01f);
        }

        public int Total => _targets.Count;

        public int Down => _targets.Count(t => t.Down);

        /// <summary>Seconds since the first shot, stopped when the last target falls.</summary>
        public float Time { get; private set; }

        private bool _started;

        public bool Won => Total > 0 && Down == Total;

        /// <summary>The names of the targets still standing, for a script that wants to shoot one.</summary>
        public IEnumerable<string> Standing => _targets.Where(t => !t.Down).Select(t => t.Node.Name);

        /// <summary>Every target, standing or down.</summary>
        public IEnumerable<ManaSceneNode> Nodes => _targets.Select(t => t.Node);

        /// <summary>How fast a target is moving, metres a second, as of the last tick: zero for one that stands still.</summary>
        public Vector3 VelocityOf(ManaSceneNode node) => _targets.FirstOrDefault(t => t.Node == node)?.Velocity ?? Vector3.Zero;

        /// <summary>The standing target called <paramref name="name"/>, or null.</summary>
        public ManaSceneNode? NodeOf(string name) =>
            _targets.FirstOrDefault(t => !t.Down && t.Node.Name == name)?.Node;

        /// <summary>Collects the level's targets: the outermost nodes in any scene whose name starts with <paramref name="prefix"/>.</summary>
        public static TargetRange Find(ManaWorld world, string prefix, float popSeconds)
        {
            var range = new TargetRange(popSeconds);
            void Walk(ManaSceneNode node)
            {
                if (node.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    range._targets.Add(new Target
                    {
                        Node = node,
                        HomeScale = node.Scale,
                        Renderers = Subtree(node).SelectMany(n => n.Components.OfType<MeshRendererComponent>()).ToList()
                    });
                    return;
                }
                foreach (var child in node.Children)
                {
                    Walk(child);
                }
            }
            foreach (var scene in world.Scenes)
            {
                foreach (var node in scene.Children)
                {
                    Walk(node);
                }
            }
            return range;
        }

        /// <summary>
        /// A thrown ball's turn: the standing target nearest the segment it travelled this frame, if the ball (of
        /// <paramref name="radius"/>) touched its face, goes down and is returned. A target is a disc about 1.2 m across.
        /// </summary>
        public ManaSceneNode? Sweep(Vector3 from, Vector3 to, float radius)
        {
            if (Won)
            {
                return null;
            }
            Target? best = null;
            var bestDistance = float.MaxValue;
            foreach (var target in _targets.Where(t => !t.Down))
            {
                var centre = target.Node.WorldTransform.Translation;
                var along = to - from;
                var length = along.LengthSquared();
                var t = 0.0f;
                if (length > 1e-8f)
                {
                    t = Math.Clamp(Vector3.Dot(centre - from, along) / length, 0.0f, 1.0f);
                }
                var distance = Vector3.Distance(centre, from + along * t);
                if (distance <= DiscRadius + radius && distance < bestDistance)
                {
                    best = target;
                    bestDistance = distance;
                }
            }
            if (best == null)
            {
                return null;
            }
            _started = true;
            best.Age = 0.0f;
            return best.Node;
        }

        /// <summary>
        /// How far along a ray the nearest standing target lies, if the ray passes within <paramref name="corridor"/>
        /// metres of its centre: the range to aim a shot for when the crosshair is near a target but not on it.
        /// </summary>
        public float? RangeAlong(Vector3 eye, Vector3 look, float corridor)
        {
            float? nearest = null;
            foreach (var target in _targets.Where(t => !t.Down))
            {
                var centre = target.Node.WorldTransform.Translation;
                var along = Vector3.Dot(centre - eye, look);
                if (along <= 0.0f || Vector3.Distance(centre, eye + look * along) > corridor)
                {
                    continue;
                }
                if (nearest == null || along < nearest)
                {
                    nearest = along;
                }
            }
            return nearest;
        }

        /// <summary>Half the width of a target's face, metres.</summary>
        public const float DiscRadius = 0.6f;

        /// <summary>Runs the clock and shrinks the targets that were hit; one that has finished goes out of sight.</summary>
        public void Tick(float deltaSeconds)
        {
            if (_started && !Won)
            {
                Time += deltaSeconds;
            }
            foreach (var target in _targets)
            {
                var now = target.Node.WorldTransform.Translation;
                if (target.Last is { } last && deltaSeconds > 0.0f)
                {
                    target.Velocity = (now - last) / deltaSeconds;
                }
                target.Last = now;
            }
            foreach (var target in _targets.Where(t => t.Down))
            {
                if (target.Age >= _popSeconds)
                {
                    continue;
                }
                target.Age += deltaSeconds;
                var left = 1.0f - Math.Clamp(target.Age / _popSeconds, 0.0f, 1.0f);
                target.Node.Scale = target.HomeScale * left * left;
                if (target.Age >= _popSeconds)
                {
                    foreach (var renderer in target.Renderers)
                    {
                        renderer.IsVisible = false;
                    }
                }
            }
        }

        /// <summary>Stands every target up again, as the level has it, and sets the clock back to wait for the first shot. Also how play puts the level back.</summary>
        public void Reset()
        {
            foreach (var target in _targets)
            {
                target.Age = -1.0f;
                target.Node.Scale = target.HomeScale;
                foreach (var renderer in target.Renderers)
                {
                    renderer.IsVisible = true;
                }
            }
            Time = 0.0f;
            _started = false;
        }

        public static IEnumerable<ManaSceneNode> Subtree(ManaSceneNode root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var node in Subtree(child))
                {
                    yield return node;
                }
            }
        }
    }
}
