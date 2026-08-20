using Mana.Code.Levels;

namespace Mana.Code.Nodes
{
    /// <summary>
    /// Where Play drops the character. Placed in the editor, saved in the level, and read by both the
    /// in-editor play state and the standalone <c>--play</c> run.
    /// <para>
    /// The marker is a child node rather than a renderer on this one, for two reasons: the box has to sit
    /// on top of the node (a character's origin is at its feet, the marker's is at its centre), and hiding
    /// it during play must not hide the start itself -- a level with the start collapsed away would have
    /// nothing left to spawn against.
    /// </para>
    /// </summary>
    public sealed class ManaPlayerStartNode : ManaSceneNode
    {
        // Roughly the space a normalised Kenney character occupies (CharacterSpawnService normalises to
        // two units tall), so what the marker outlines is what will actually stand there.
        private const float MarkerHeight = 2.0f;
        private const float MarkerHalfWidth = 0.4f;

        private static readonly Vector4 MarkerColor = new(0.25f, 0.85f, 0.45f, 1.0f);
        private static readonly Vector4 FacingColor = new(0.95f, 0.80f, 0.20f, 1.0f);

        private readonly ManaSceneNode _marker;
        private readonly ManaSceneNode _facing;

        public ManaPlayerStartNode(ManaContext context, Vector3 position)
            : base(context, Matrix4x4.CreateTranslation(position))
        {
            Name = "Player Start";
            LevelSource = ManaLevelSource.PlayerStart;

            _marker = new ManaSceneNode(context, Matrix4x4.CreateTranslation(new Vector3(0.0f, MarkerHeight * 0.5f, 0.0f)))
            {
                Name = "Player Start Marker",
                IncludeInEnvironmentCapture = false
            };
            _marker.Components.Add(new MeshRendererComponent(
                _marker,
                new ManaCube(context, MarkerColor, new Vector3(MarkerHalfWidth, MarkerHeight * 0.5f, MarkerHalfWidth))));
            AddChild(_marker);

            // A wedge in front of the box, so the yaw the character will spawn facing is visible rather
            // than something you find out by pressing Play. Characters face +Z (see ManaPawnController's
            // spring arm), so the wedge goes there too.
            _facing = new ManaSceneNode(context, Matrix4x4.CreateTranslation(new Vector3(0.0f, 0.15f, MarkerHalfWidth + 0.35f)))
            {
                Name = "Player Start Facing",
                IncludeInEnvironmentCapture = false
            };
            _facing.Components.Add(new MeshRendererComponent(
                _facing,
                new ManaPyramid(context, FacingColor, new Vector3(0.25f, 0.15f, 0.35f))));
            AddChild(_facing);

            UpdateTransform();
        }

        /// <summary>World position the character's feet land on.</summary>
        public Vector3 SpawnPosition => WorldTransform.Translation;

        /// <summary>Yaw in radians the character spawns facing, taken straight off this node's rotation.</summary>
        public float SpawnYawRadians => Rotation.Y;

        /// <summary>
        /// Hidden while playing -- a green box around the character's ankles is editor furniture, not part
        /// of the game.
        /// </summary>
        public bool IsMarkerVisible
        {
            get => _markerVisible;
            set
            {
                if (_markerVisible == value)
                {
                    return;
                }

                _markerVisible = value;
                SetMarkerRenderersEnabled(value);
            }
        }

        private bool _markerVisible = true;

        private void SetMarkerRenderersEnabled(bool enabled)
        {
            // Detaching the children rather than flagging the renderers: CollectRenderMeshInstances walks
            // the tree, and a node that is not in it costs nothing to skip.
            if (enabled)
            {
                if (!Children.Contains(_marker))
                {
                    AddChild(_marker);
                    AddChild(_facing);
                    UpdateTransform();
                }

                return;
            }

            RemoveChild(_marker);
            RemoveChild(_facing);
        }

        /// <summary>
        /// The start a level should spawn against: the first one in the world, or null when the level has
        /// none and the caller should fall back to its own default.
        /// </summary>
        public static ManaPlayerStartNode? Find(ManaWorld world)
        {
            foreach (var scene in world.Scenes)
            {
                foreach (var child in scene.Children)
                {
                    var found = FindIn(child);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static ManaPlayerStartNode? FindIn(ManaSceneNode node)
        {
            if (node is ManaPlayerStartNode start)
            {
                return start;
            }

            foreach (var child in node.Children)
            {
                var found = FindIn(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
