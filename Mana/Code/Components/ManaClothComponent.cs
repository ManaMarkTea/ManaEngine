using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana.Code.Components
{
    /// <summary>Which edge of a cloth holds still: a flag's pole, a banner's rail, a sheet hung by two corners.</summary>
    public enum ManaClothPin
    {
        Left,
        Top,
        TopCorners
    }

    /// <summary>
    /// A flag, banner or awning that hangs and blows in the world's wind (<see cref="ManaWind"/>). A sheet of
    /// <see cref="Size"/> metres lies across +X and down -Y from its node, held along <see cref="Pinned"/>; the rest is
    /// a grid of points kept apart by springs (Verlet, solved in world space so moving the node drags the cloth). The
    /// mesh is rebuilt from the points every frame, never saved. BEPU has no cloth, and a sheet this size wants none.
    /// </summary>
    public sealed class ManaClothComponent : ManaComponent
    {
        /// <summary>Metres across (+X) and down (-Y) from the node.</summary>
        [Category("Shape")]
        public Vector2 Size { get; set; } = new(2.0f, 1.25f);

        /// <summary>Points across: more folds smaller, at the cost of time.</summary>
        [Category("Shape"), Range(2, 48)]
        public int Columns { get; set; } = 20;

        /// <summary>Points down.</summary>
        [Category("Shape"), Range(2, 48)]
        public int Rows { get; set; } = 13;

        [Category("Shape")]
        public ManaClothPin Pinned { get; set; } = ManaClothPin.Left;

        /// <summary>Degrees the sheet curls round toward +Z across its width: 0 is flat, a cape wraps the shoulders.</summary>
        [Category("Shape"), Range(0, 180)]
        public float Wrap { get; set; }

        /// <summary>A picture across the cloth (a flag's design); transparent parts are cut out.</summary>
        [PicturePath, Category("Look")]
        public string Texture { get; set; } = string.Empty;

        [Category("Look")]
        public Vector4 Color { get; set; } = new(0.78f, 0.12f, 0.12f, 1.0f);

        /// <summary>How hard the wind pushes it: 0 hangs still, 1 is a light flag.</summary>
        [Category("Motion"), Range(0, 4)]
        public float WindResponse { get; set; } = 1.0f;

        /// <summary>How far it resists folding: low drapes like silk, high holds like canvas.</summary>
        [Category("Motion"), Range(0, 1)]
        public float Stiffness { get; set; } = 0.4f;

        /// <summary>Share of gravity it feels.</summary>
        [Category("Motion"), Range(0, 2)]
        public float Gravity { get; set; } = 1.0f;

        /// <summary>Kilograms a square metre: a light flag lifts in a breeze, a heavy cape only trails.</summary>
        [Category("Motion"), Range(0.05, 2)]
        public float Weight { get; set; } = 0.25f;

        /// <summary>
        /// Share of its holder's movement it feels: 1 is a real sheet; less is carried along with the rest, so a
        /// character who sets off at full speed in an instant does not whip its cape over its head.
        /// </summary>
        [Category("Motion"), Range(0, 1)]
        public float Inertia { get; set; } = 1.0f;

        /// <summary>Share of its swing lost each step: higher settles sooner.</summary>
        [Category("Motion"), Range(0, 0.2)]
        public float Damping { get; set; } = 0.01f;

        private const float Step = 1.0f / 90.0f;
        private const int Iterations = 16;
        // kg per cubic metre of air: what turns the wind's push into acceleration, with the cloth's weight.
        private const float AirDensity = 1.2f;

        private readonly record struct Spring(int A, int B, float Rest, float Strength);

        // A body the cloth slides round rather than through: a capsule in a node's frame, its radius in metres.
        private readonly record struct Capsule(ManaSceneNode Node, Vector3 A, Vector3 B, float Radius);

        private readonly List<Capsule> _capsules = [];
        private ManaSceneNode? _anchor;
        private Matrix4x4 _anchorOffset = Matrix4x4.Identity;

        private MeshRendererComponent? _renderer;
        private ManaMesh? _mesh;
        private string? _builtKey;
        private int _columns;
        private int _rows;
        private Vector3[] _rest = [];
        private Vector3[] _points = [];
        private Vector3[] _previous = [];
        private Vector3[] _push = [];
        private bool[] _held = [];
        private Spring[] _springs = [];
        private float _pointMass = 0.01f;
        private float _carry;
        private Vector3? _lastAnchor;
        private Matrix4x4? _lastPin;
        private Vector3 _anchorVelocity;
        private double _time;

        public ManaClothComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Cloth";
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            Advance(Math.Clamp((float)ManaTime.GlobalTime.DeltaTime.TotalSeconds, 0.0f, 0.1f));
        }

        /// <summary>
        /// Holds the pinned edge to another node instead of this one (a cape on a character), at an offset in that
        /// node's frame; null goes back to this node. Set from code, never saved.
        /// </summary>
        public void HangFrom(ManaSceneNode? anchor, Matrix4x4 offset)
        {
            _anchor = anchor;
            _anchorOffset = offset;
        }

        /// <summary>Keeps the cloth out of a capsule from <paramref name="a"/> to <paramref name="b"/> in a node's frame.</summary>
        public void CollideWith(ManaSceneNode node, Vector3 a, Vector3 b, float radius)
        {
            _capsules.Add(new Capsule(node, a, b, radius));
        }

        public void ClearColliders()
        {
            _capsules.Clear();
        }

        /// <summary>Moves the cloth on by some seconds and hands the mesh its new shape.</summary>
        public void Advance(float elapsed)
        {
            var key = $"{Size}|{Columns}|{Rows}|{Pinned}|{Wrap}|{Weight}|{Texture}|{Color}";
            if (key != _builtKey || _renderer == null)
            {
                _builtKey = key;
                Rebuild();
            }

            Simulate(PinFrame(), elapsed);
            Publish(Owner.WorldTransform);
        }

        // Where the held edge hangs from in the world.
        private Matrix4x4 PinFrame()
        {
            if (_anchor != null)
            {
                return _anchorOffset * _anchor.WorldTransform;
            }
            return Owner.WorldTransform;
        }

        /// <summary>Lays the points out flat where the cloth hangs from, and makes its springs and mesh.</summary>
        private void Rebuild()
        {
            _columns = Math.Clamp(Columns, 2, 48);
            _rows = Math.Clamp(Rows, 2, 48);
            var size = new Vector2(MathF.Max(Size.X, 0.05f), MathF.Max(Size.Y, 0.05f));
            var wrap = Math.Clamp(Wrap, 0.0f, 180.0f) * MathF.PI / 180.0f;
            var count = _columns * _rows;
            _rest = new Vector3[count];
            _held = new bool[count];
            var uvs = new Vector2[count];
            for (var row = 0; row < _rows; row++)
            {
                for (var column = 0; column < _columns; column++)
                {
                    var u = column / (float)(_columns - 1);
                    var v = row / (float)(_rows - 1);
                    var index = row * _columns + column;
                    _rest[index] = new Vector3(u * size.X, -v * size.Y, 0.0f);
                    if (wrap > 1e-3f)
                    {
                        // Round an arc as long as the width, its edges curling toward +Z.
                        var radius = size.X / wrap;
                        var angle = (u - 0.5f) * wrap;
                        _rest[index] = new Vector3(size.X * 0.5f + radius * MathF.Sin(angle), -v * size.Y, radius * (1.0f - MathF.Cos(angle)));
                    }
                    uvs[index] = new Vector2(u, 1.0f - v);
                    _held[index] = Pinned switch
                    {
                        ManaClothPin.Left => column == 0,
                        ManaClothPin.Top => row == 0,
                        _ => row == 0 && (column == 0 || column == _columns - 1)
                    };
                }
            }
            _pointMass = Math.Clamp(Weight, 0.05f, 2.0f) * size.X * size.Y / count;

            // Along the weave, across its diagonals, and two apart for folding.
            var springs = new List<Spring>();
            var fold = 0.05f + 0.6f * Math.Clamp(Stiffness, 0.0f, 1.0f);
            void Join(int columnA, int rowA, int columnB, int rowB, float strength)
            {
                if (columnB >= _columns || rowB >= _rows || columnB < 0)
                {
                    return;
                }
                var a = rowA * _columns + columnA;
                var b = rowB * _columns + columnB;
                springs.Add(new Spring(a, b, Vector3.Distance(_rest[a], _rest[b]), strength));
            }
            for (var row = 0; row < _rows; row++)
            {
                for (var column = 0; column < _columns; column++)
                {
                    Join(column, row, column + 1, row, 1.0f);
                    Join(column, row, column, row + 1, 1.0f);
                    Join(column, row, column + 1, row + 1, 0.5f);
                    Join(column, row, column - 1, row + 1, 0.5f);
                    Join(column, row, column + 2, row, fold);
                    Join(column, row, column, row + 2, fold);
                }
            }
            _springs = springs.ToArray();

            var indexes = new List<uint>();
            for (var row = 0; row + 1 < _rows; row++)
            {
                for (var column = 0; column + 1 < _columns; column++)
                {
                    var a = (uint)(row * _columns + column);
                    var b = a + 1;
                    var c = a + (uint)_columns;
                    var d = c + 1;
                    indexes.AddRange([a, c, b, b, c, d]);
                }
            }

            var world = PinFrame();
            _lastAnchor = null;
            _lastPin = null;
            _points = new Vector3[count];
            _previous = new Vector3[count];
            _push = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                // A sheet laid perfectly flat is a rigid frame in its own plane: a faint ripple lets it fold and hang.
                var ripple = new Vector3(0.0f, 0.0f, 0.01f * MathF.Sin(i * 1.7f));
                _points[i] = Vector3.Transform(_rest[i] + (_held[i] ? Vector3.Zero : ripple), world);
                _previous[i] = _points[i];
            }
            _carry = 0.0f;

            var colors = Enumerable.Repeat(Vector4.One, count).ToArray();
            var picture = ManaSpritePictures.Resolve(Texture);
            _mesh = new ManaMesh(Owner.Context) { Name = "Cloth" };
            _mesh.SetCpuData(new ManaMeshCpuData
            {
                Positions = (Vector3[])_rest.Clone(),
                Normals = Enumerable.Repeat(Vector3.UnitZ, count).ToArray(),
                Colors = colors,
                TextureCoordinates0 = uvs,
                TextureCoordinates1 = uvs,
                Indexes = indexes.ToArray(),
                AlbedoTexturePath = picture
            });
            var material = new ManaMaterial(Owner.Context)
            {
                Name = "Cloth Material",
                Shader = new ManaPbrShader(Owner.Context),
                OpacityMode = picture != null ? ManaOpacityMode.Cutout : ManaOpacityMode.Opaque,
                AlphaCutoff = 0.5
            };
            material.Albedo.Value = Color;
            material.Roughness.Value = 0.9f;
            material.Metallic.Value = 0.0f;
            _mesh.Materials = [material];

            lock (Owner.Context.World.SceneLock)
            {
                if (_renderer != null)
                {
                    Owner.Components.Remove(_renderer);
                }
                // Seen from both sides: each side is lit as the one facing the camera.
                _renderer = new MeshRendererComponent(Owner, _mesh) { TwoSidedLighting = true };
                Owner.Components.Add(_renderer);
            }
        }

        private void Simulate(Matrix4x4 world, float elapsed)
        {
            var count = _points.Length;
            if (count == 0)
            {
                return;
            }

            // A jump (a level loading, the node typed somewhere new) starts the cloth over rather than whipping it there.
            var anchor = Array.IndexOf(_held, true);
            if (anchor >= 0 && Vector3.DistanceSquared(Vector3.Transform(_rest[anchor], world), _points[anchor]) > 25.0f)
            {
                for (var i = 0; i < count; i++)
                {
                    _points[i] = Vector3.Transform(_rest[i], world);
                    _previous[i] = _points[i];
                }
                _lastAnchor = null;
                _lastPin = null;
            }

            // The share of its holder's move and turn it does not feel, it is carried through, previous positions too,
            // so that share starts no swing.
            var inertia = Math.Clamp(Inertia, 0.0f, 1.0f);
            if (inertia < 1.0f && _lastPin is { } lastPin && Matrix4x4.Invert(lastPin, out var undo))
            {
                var moved = undo * world;
                for (var i = 0; i < count; i++)
                {
                    if (_held[i])
                    {
                        continue;
                    }
                    var shift = (Vector3.Transform(_points[i], moved) - _points[i]) * (1.0f - inertia);
                    _points[i] += shift;
                    _previous[i] += shift;
                }
            }
            _lastPin = world;

            // How fast what it hangs from is moving: damping settles the cloth toward that, not toward standing still,
            // which would drag it back like a gale whenever its holder walked.
            if (anchor >= 0)
            {
                var anchorNow = Vector3.Transform(_rest[anchor], world);
                if (_lastAnchor is { } before && elapsed > 1e-4f)
                {
                    _anchorVelocity = (anchorNow - before) / elapsed;
                }
                else
                {
                    _anchorVelocity = Vector3.Zero;
                }
                _lastAnchor = anchorNow;
            }

            var settings = Owner.Context.World.IrradianceSettings;
            var gustiness = Math.Clamp(settings.Gustiness, 0.0f, 1.0f);
            var direction = ManaWind.DirectionFor(settings.WindDirection);
            var speed = ManaWind.SpeedAt(Math.Clamp(settings.WindSpeed, 0.0f, ManaWind.MaximumSpeed), gustiness,
                ManaWind.GustAt(ManaSky.Seconds));
            var wind = new Vector3(direction.X, 0.0f, direction.Y) * speed * MathF.Max(WindResponse, 0.0f);
            var gravity = new Vector3(0.0f, -9.81f * Gravity, 0.0f);
            var keep = 1.0f - Math.Clamp(Damping, 0.0f, 0.5f);
            var bodies = Bodies();

            _carry = MathF.Min(_carry + elapsed, Step * 6.0f);
            while (_carry >= Step)
            {
                _carry -= Step;
                _time += Step;
                for (var i = 0; i < count; i++)
                {
                    if (_held[i])
                    {
                        _points[i] = Vector3.Transform(_rest[i], world);
                        _previous[i] = _points[i];
                    }
                }

                PushByWind(wind);
                for (var i = 0; i < count; i++)
                {
                    if (_held[i])
                    {
                        continue;
                    }
                    var point = _points[i];
                    var acceleration = gravity + _push[i] / _pointMass;
                    var carried = _anchorVelocity * (Step * inertia);
                    _points[i] = point + carried + (point - _previous[i] - carried) * keep + acceleration * (Step * Step);
                    _previous[i] = point;
                }

                for (var iteration = 0; iteration < Iterations; iteration++)
                {
                    // Sweeping back and forth in turn settles a long weave far sooner than one way round.
                    var forward = iteration % 2 == 0;
                    for (var s = 0; s < _springs.Length; s++)
                    {
                        var spring = _springs[forward ? s : _springs.Length - 1 - s];
                        var heldA = _held[spring.A];
                        var heldB = _held[spring.B];
                        if (heldA && heldB)
                        {
                            continue;
                        }
                        var delta = _points[spring.B] - _points[spring.A];
                        var length = delta.Length();
                        if (length < 1e-6f)
                        {
                            continue;
                        }
                        var correction = delta * ((length - spring.Rest) / length * spring.Strength);
                        if (heldA)
                        {
                            _points[spring.B] -= correction;
                        }
                        else if (heldB)
                        {
                            _points[spring.A] += correction;
                        }
                        else
                        {
                            _points[spring.A] += correction * 0.5f;
                            _points[spring.B] -= correction * 0.5f;
                        }
                    }
                    PushOutOfBodies(bodies);
                }
            }
        }

        // Each capsule's ends in the world, for this frame.
        private (Vector3 A, Vector3 B, float Radius)[] Bodies()
        {
            var bodies = new (Vector3 A, Vector3 B, float Radius)[_capsules.Count];
            for (var i = 0; i < bodies.Length; i++)
            {
                var capsule = _capsules[i];
                var frame = capsule.Node.WorldTransform;
                bodies[i] = (Vector3.Transform(capsule.A, frame), Vector3.Transform(capsule.B, frame), capsule.Radius);
            }
            return bodies;
        }

        private void PushOutOfBodies((Vector3 A, Vector3 B, float Radius)[] bodies)
        {
            foreach (var (a, b, radius) in bodies)
            {
                var axis = b - a;
                var lengthSquared = MathF.Max(axis.LengthSquared(), 1e-8f);
                for (var i = 0; i < _points.Length; i++)
                {
                    if (_held[i])
                    {
                        continue;
                    }
                    var along = Math.Clamp(Vector3.Dot(_points[i] - a, axis) / lengthSquared, 0.0f, 1.0f);
                    var nearest = a + axis * along;
                    var away = _points[i] - nearest;
                    var distanceSquared = away.LengthSquared();
                    if (distanceSquared >= radius * radius || distanceSquared < 1e-10f)
                    {
                        continue;
                    }
                    _points[i] = nearest + away * (radius / MathF.Sqrt(distanceSquared));
                }
            }
        }

        // Air drag on each triangle, against the wind as it meets the cloth: a sheet edge-on feels almost nothing,
        // one square on feels it all. A ripple running down the wind gives the flutter a flag has.
        private void PushByWind(Vector3 wind)
        {
            Array.Clear(_push);
            // Still air pushes too, against the cloth's own motion; only the streaming along the wind needs a wind.
            var along = Vector3.Zero;
            if (wind.LengthSquared() > 1e-6f)
            {
                along = Vector3.Normalize(wind);
            }
            for (var row = 0; row + 1 < _rows; row++)
            {
                for (var column = 0; column + 1 < _columns; column++)
                {
                    var a = row * _columns + column;
                    var b = a + 1;
                    var c = a + _columns;
                    var d = c + 1;
                    var flutter = 1.0f + 0.35f * MathF.Sin((float)_time * 9.0f - column * 0.9f + row * 0.4f);
                    PushTriangle(a, c, b, wind * flutter, along);
                    PushTriangle(b, c, d, wind * flutter, along);
                }
            }
        }

        private void PushTriangle(int a, int b, int c, Vector3 wind, Vector3 along)
        {
            var cross = Vector3.Cross(_points[b] - _points[a], _points[c] - _points[a]);
            var twiceArea = cross.Length();
            if (twiceArea < 1e-8f)
            {
                return;
            }
            var normal = cross / twiceArea;
            var velocity = (_points[a] - _previous[a] + _points[b] - _previous[b] + _points[c] - _previous[c]) / (3.0f * Step);
            var relative = wind - velocity;
            var facing = Vector3.Dot(normal, relative);
            // Pressure on the face, plus a little drag along it so a flag edge-on still streams out.
            var force = normal * (0.5f * AirDensity * twiceArea * 0.5f * facing * MathF.Abs(facing))
                + along * (0.02f * AirDensity * twiceArea * relative.Length());
            var share = force / 3.0f;
            _push[a] += share;
            _push[b] += share;
            _push[c] += share;
        }

        /// <summary>Hands the points to the mesh in the node's frame, with smooth normals, as a new geometry version.</summary>
        private void Publish(Matrix4x4 world)
        {
            if (_mesh?.CpuData is not { } previous || !Matrix4x4.Invert(world, out var toLocal))
            {
                return;
            }
            var positions = new Vector3[_points.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = Vector3.Transform(_points[i], toLocal);
            }
            var normals = new Vector3[_points.Length];
            var indexes = previous.Indexes;
            for (var i = 0; i + 2 < indexes.Length; i += 3)
            {
                var a = (int)indexes[i];
                var b = (int)indexes[i + 1];
                var c = (int)indexes[i + 2];
                var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }
            for (var i = 0; i < normals.Length; i++)
            {
                if (normals[i].LengthSquared() > 1e-12f)
                {
                    normals[i] = Vector3.Normalize(normals[i]);
                }
                else
                {
                    normals[i] = Vector3.UnitZ;
                }
            }
            // Fresh arrays each frame: the renderer may still be reading last frame's.
            _mesh.SetCpuData(new ManaMeshCpuData
            {
                Positions = positions,
                Normals = normals,
                Colors = previous.Colors,
                TextureCoordinates0 = previous.TextureCoordinates0,
                TextureCoordinates1 = previous.TextureCoordinates1,
                Indexes = indexes,
                AlbedoTexturePath = previous.AlbedoTexturePath
            });
        }

        /// <summary>The points in world space -- for tests.</summary>
        public IReadOnlyList<Vector3> Points => _points;
    }
}
