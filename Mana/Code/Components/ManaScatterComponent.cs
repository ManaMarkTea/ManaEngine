using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Mana.Code.Models;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Grass, trees or rocks strewn over a terrain where one of its paint layers is (on a <see cref="ManaTerrainNode"/>):
    /// a jittered grid at <see cref="Density"/> a square metre, kept off steep ground, each copy turned and sized at
    /// random from <see cref="Seed"/>, so the same settings always grow the same wood. Copies share their model's meshes
    /// and draw instanced; they are rebuilt from the settings and the ground, never saved, and rebuild a moment after
    /// the terrain is sculpted or painted. Several on one terrain make the layers: grass, then bushes, then trees.
    /// </summary>
    public sealed class ManaScatterComponent : ManaComponent
    {
        /// <summary>What to strew, picked at random for each spot.</summary>
        [ModelPath, Category("Scatter")]
        public string[] Models { get; set; } = [];

        /// <summary>The paint layer it grows on: 0 grass, 1 dirt, 2 rock, 3 sand (the Terrain window's order).</summary>
        [Category("Scatter"), Range(0, 3)]
        public int Layer { get; set; }

        /// <summary>Copies a square metre where the layer is fully painted.</summary>
        [Category("Scatter"), Range(0, 4)]
        public float Density { get; set; } = 0.1f;

        /// <summary>How much of the layer a spot needs: copies thin out around it, so patches have soft edges.</summary>
        [Category("Scatter"), Range(0, 1)]
        public float MinimumPaint { get; set; } = 0.5f;

        /// <summary>Steeper ground than this, in degrees, is left bare.</summary>
        [Category("Scatter"), Range(0, 90)]
        public float MaximumSlope { get; set; } = 35.0f;

        /// <summary>Another number grows another arrangement of the same things.</summary>
        [Category("Scatter")]
        public int Seed { get; set; } = 1;

        /// <summary>Smallest and largest size, as a multiple of the model's.</summary>
        [Category("Look")]
        public Vector2 Scale { get; set; } = new(0.8f, 1.2f);

        /// <summary>How far each copy leans with the slope: 0 stands upright (trees), 1 lies along it (rocks, grass).</summary>
        [Category("Look"), Range(0, 1)]
        public float FollowSlope { get; set; } = 0.5f;

        /// <summary>Turn each copy at random about its upright.</summary>
        [Category("Look")]
        public bool RandomTurn { get; set; } = true;

        /// <summary>A cap, so a high density over a big terrain cannot bury the scene.</summary>
        [Category("Look"), Range(0, 50000)]
        public int MaximumCount { get; set; } = 5000;

        // After a brush stroke, wait this long for it to finish before regrowing.
        private const long SettleMilliseconds = 300;

        private ManaSceneNode? _group;
        private ManaModelLibrary? _library;
        private string? _builtKey;
        private string? _pendingKey;
        private long _pendingSince;

        public ManaScatterComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Scatter";
        }

        /// <summary>How many copies stand now.</summary>
        public int Count => _group?.Children.Count ?? 0;

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            var key = Key();
            if (key == _builtKey)
            {
                return;
            }
            if (key != _pendingKey)
            {
                _pendingKey = key;
                _pendingSince = Environment.TickCount64;
            }
            // The first growth is at once; a change waits for the stroke or the typing to settle.
            if (_builtKey == null || Environment.TickCount64 - _pendingSince >= SettleMilliseconds)
            {
                Rebuild();
            }
        }

        private string Key()
        {
            var revision = (Owner as ManaTerrainNode)?.Revision ?? -1;
            return $"{string.Join(';', Models)}|{Layer}|{Density}|{MinimumPaint}|{MaximumSlope}|{Seed}|{Scale}|{FollowSlope}|{RandomTurn}|{MaximumCount}|{revision}";
        }

        /// <summary>Clears the copies and grows them again from the settings and the ground as it is now.</summary>
        public void Rebuild()
        {
            _builtKey = Key();
            var group = new ManaSceneNode(Owner.Context, Matrix4x4.Identity) { Name = "Scatter", IsEditorTransient = true };
            if (Owner is ManaTerrainNode terrain && Models.Length > 0)
            {
                _library ??= new ManaModelLibrary(Owner.Context);
                foreach (var placement in Plan())
                {
                    var asset = Models[placement.Model];
                    if (string.IsNullOrWhiteSpace(asset) || _library.Place(asset, placement.Transform) is not { } copy)
                    {
                        continue;
                    }
                    MarkTransient(copy);
                    group.AddChild(copy);
                }
            }

            lock (Owner.Context.World.SceneLock)
            {
                if (_group != null)
                {
                    Owner.RemoveChild(_group);
                }
                _group = group;
                Owner.AddChild(group);
            }
        }

        /// <summary>One copy to place: which of <see cref="Models"/>, and where on the terrain (in its own frame).</summary>
        public readonly record struct Placement(int Model, Matrix4x4 Transform);

        // A jittered grid over the terrain, one candidate a cell, each with its own random numbers from the seed and the
        // cell -- so a stroke on one hill leaves the rest of the wood exactly where it stood.
        public IEnumerable<Placement> Plan()
        {
            if (Owner is not ManaTerrainNode terrain || Models.Length == 0)
            {
                yield break;
            }
            var density = MathF.Max(Density, 0.0f);
            if (density <= 0.0f || MaximumCount <= 0)
            {
                yield break;
            }
            var cell = 1.0f / MathF.Sqrt(density);
            var cellsX = Math.Max(1, (int)MathF.Ceiling(terrain.Width / cell));
            var cellsZ = Math.Max(1, (int)MathF.Ceiling(terrain.Depth / cell));
            var minimumUp = MathF.Cos(Math.Clamp(MaximumSlope, 0.0f, 90.0f) * MathF.PI / 180.0f);
            var edge = MathF.Max(0.1f, 1.0f - MinimumPaint) * 0.5f;
            var low = MathF.Min(Scale.X, Scale.Y);
            var high = MathF.Max(Scale.X, Scale.Y);
            var placed = 0;
            for (var cz = 0; cz < cellsZ; cz++)
            {
                for (var cx = 0; cx < cellsX; cx++)
                {
                    var random = new Random(HashCode.Combine(Seed, cx, cz));
                    var x = -terrain.Width * 0.5f + (cx + random.NextSingle()) * cell;
                    var z = -terrain.Depth * 0.5f + (cz + random.NextSingle()) * cell;
                    var chance = random.NextSingle();
                    var model = random.Next(Models.Length);
                    var turn = random.NextSingle() * MathF.Tau;
                    var size = low + (high - low) * random.NextSingle();
                    if (!terrain.Covers(x, z))
                    {
                        continue;
                    }

                    // Sure inside the layer, fewer toward where its paint runs out.
                    var paint = terrain.PaintAt(x, z, Layer);
                    var keep = Math.Clamp((paint - MinimumPaint) / edge + 0.5f, 0.0f, 1.0f);
                    if (chance >= keep)
                    {
                        continue;
                    }

                    var step = terrain.Spacing * 0.5f;
                    var normal = Vector3.Normalize(new Vector3(
                        terrain.HeightAt(x - step, z) - terrain.HeightAt(x + step, z),
                        2.0f * step,
                        terrain.HeightAt(x, z - step) - terrain.HeightAt(x, z + step)));
                    if (normal.Y < minimumUp)
                    {
                        continue;
                    }

                    var up = Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, normal, Math.Clamp(FollowSlope, 0.0f, 1.0f)));
                    var lean = Lean(up);
                    var spin = Matrix4x4.Identity;
                    if (RandomTurn)
                    {
                        spin = Matrix4x4.CreateRotationY(turn);
                    }
                    var transform = Matrix4x4.CreateScale(size) * spin * lean
                        * Matrix4x4.CreateTranslation(x, terrain.HeightAt(x, z), z);
                    yield return new Placement(model, transform);
                    placed++;
                    if (placed >= MaximumCount)
                    {
                        yield break;
                    }
                }
            }
        }

        // The rotation taking straight up to the given direction.
        private static Matrix4x4 Lean(Vector3 up)
        {
            var axis = Vector3.Cross(Vector3.UnitY, up);
            var length = axis.Length();
            if (length < 1e-5f)
            {
                return Matrix4x4.Identity;
            }
            return Matrix4x4.CreateFromAxisAngle(axis / length, MathF.Asin(Math.Clamp(length, 0.0f, 1.0f)));
        }

        private static void MarkTransient(ManaSceneNode node)
        {
            node.IsEditorTransient = true;
            foreach (var child in node.Children)
            {
                MarkTransient(child);
            }
        }
    }
}
