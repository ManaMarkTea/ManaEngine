using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A cape on a character: a <see cref="ManaClothComponent"/> held across the shoulders and kept out of a capsule
    /// round the body, so it trails when the character runs and turns and blows in the wind. The cloth sits on a node
    /// of its own beside the pawn rather than under it, so the pawn's measured height and bounds never include it.
    /// </summary>
    public static class ManaCape
    {
        // Shares of the character's height, fitted to Kenney's characters: big heads, so the neck is under halfway up.
        private const float NeckHeight = 0.45f;
        private const float Width = 0.42f;
        private const float HemClearance = 0.08f;

        /// <summary>Hangs a cape on the pawn and returns its node, already in the pawn's scene; null when it has no model to measure.</summary>
        public static ManaSceneNode? Attach(ManaPawnNode pawn, Vector4 color)
        {
            if (!pawn.TryGetLocalBounds(out var min, out var max) || max.Y <= min.Y)
            {
                return null;
            }
            var scale = MathF.Abs(pawn.Scale.Y);
            if (scale < 1e-6f)
            {
                scale = 1.0f;
            }

            // Measured in the pawn's frame turned so +Z is where the model faces: the back is the bounds' -Z side.
            var facing = Matrix4x4.CreateRotationY(-pawn.FacingOffsetDegrees * MathF.PI / 180.0f);
            Matrix4x4.Invert(facing, out var unfacing);
            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);
            foreach (var x in new[] { min.X, max.X })
            {
                foreach (var z in new[] { min.Z, max.Z })
                {
                    foreach (var y in new[] { min.Y, max.Y })
                    {
                        var point = Vector3.Transform(new Vector3(x, y, z), unfacing);
                        low = Vector3.Min(low, point);
                        high = Vector3.Max(high, point);
                    }
                }
            }
            var localHeight = high.Y - low.Y;
            var height = localHeight * scale;
            var middle = (low + high) * 0.5f;
            var neck = low.Y + localHeight * NeckHeight;
            var back = low.Z - localHeight * 0.04f;
            var width = height * Width;

            // Hung just behind the back of the head, curling forward round the body.
            var offset = Matrix4x4.CreateScale(1.0f / scale)
                * Matrix4x4.CreateTranslation(middle.X - width * 0.5f / scale, neck, back)
                * facing;

            var node = new ManaSceneNode(pawn.Context, Matrix4x4.Identity) { Name = "Cape", IsEditorTransient = true };
            var cloth = new ManaClothComponent(node) { Color = color };
            Configure(cloth, width, (neck - low.Y) * scale - height * HemClearance, (middle.Z - back) * scale);
            cloth.HangFrom(pawn, offset);
            // From the knees to the neck, as deep as the back: it keeps the cape from swinging in through the body.
            var radius = middle.Z - low.Z;
            var axisTop = Vector3.Transform(new Vector3(middle.X, neck, middle.Z), facing);
            var axisBottom = Vector3.Transform(new Vector3(middle.X, low.Y + localHeight * 0.15f, middle.Z), facing);
            cloth.CollideWith(pawn, axisBottom, axisTop, radius * scale);
            node.Components.Add(cloth);

            var root = (ManaSceneNode)pawn;
            while (root.Parent != null)
            {
                root = root.Parent;
            }
            lock (pawn.Context.World.SceneLock)
            {
                var scene = pawn.Context.World.Scenes.FirstOrDefault(candidate => candidate.Children.Contains(root))
                    ?? pawn.Context.World.DefaultScene;
                scene.Children.Add(node);
            }
            return node;
        }

        /// <summary>
        /// A cape's cloth: held by its top edge, curled round the shoulders, and half carried along by its wearer, so
        /// a character who sets off at 7 m/s in an instant trails it rather than flinging it over its head.
        /// </summary>
        public static void Configure(ManaClothComponent cloth, float width, float length, float bodyDistance)
        {
            cloth.Size = new Vector2(width, length);
            cloth.Columns = 12;
            cloth.Rows = 14;
            cloth.Pinned = ManaClothPin.Top;
            // Curled round the body's own axis (or wider), so its edges stay outside the body it slides round.
            cloth.Wrap = MathF.Min(120.0f, width / MathF.Max(bodyDistance, 0.01f) * 180.0f / MathF.PI);
            cloth.Stiffness = 0.55f;
            cloth.WindResponse = 0.6f;
            cloth.Weight = 0.6f;
            cloth.Inertia = 0.5f;
            cloth.Damping = 0.02f;
        }

        /// <summary>Takes a cape off again, from whichever scene holds it.</summary>
        public static void Remove(ManaSceneNode cape)
        {
            lock (cape.Context.World.SceneLock)
            {
                foreach (var scene in cape.Context.World.Scenes)
                {
                    scene.Children.Remove(cape);
                }
            }
        }
    }
}
