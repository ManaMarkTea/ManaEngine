using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaEditor.Code.Gameplay.Course
{
    /// <summary>
    /// A box that notices the player walking into it, and says so. It knows nothing about coins or damage: it owns
    /// the volume (<see cref="Size"/> and <see cref="Offset"/>), the wireframe that shows it in the editor, and three
    /// events. Whatever should happen when the player is inside -- a coin collected, a hazard hurting, a door opening
    /// -- is a separate component on the same node that subscribes to those events (see
    /// <see cref="ManaTriggerListenerComponent"/>).
    /// <para>
    /// A plain overlap test each tick rather than a physics sensor: there is one player, so one box test per trigger
    /// is cheaper than teaching the narrow phase about non-solid bodies, and it stays testable without a physics
    /// world. Only ticks while the world is playing, so triggers are inert in the editor.
    /// </para>
    /// </summary>
    public sealed class TriggerVolumeComponent : ManaComponent
    {
        public const string ScriptType = "Trigger";

        // What the player's capsule is treated as: a box this wide and ReferenceHeight tall, feet at the node origin.
        // Slightly generous is right for a trigger -- grazing a coin should collect it.
        public const float PlayerRadius = 0.4f;

        private bool _playerInside;
        private readonly EditorVolumeMarker _marker;

        public TriggerVolumeComponent(ManaSceneNode owner) : base(owner)
        {
            _marker = new EditorVolumeMarker(owner);
        }

        /// <summary>Full extents of the volume, in the node's own axes. Node scale does not change it.</summary>
        public Vector3 Size { get; set; } = new(2.0f, 2.0f, 2.0f);

        /// <summary>Centre of the volume relative to the node, so a coin's volume can sit around the model.</summary>
        public Vector3 Offset { get; set; }

        /// <summary>
        /// The colour of the editor wireframe, so different pieces can be told apart at a glance. Set by whatever
        /// creates the trigger for a piece; not saved, since it is a hint for the editor and not part of the level.
        /// </summary>
        internal Vector4 EditorColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

        [JsonIgnore]
        public bool IsPlayerInside => _playerInside;

        /// <summary>The editor wireframe, or null before the first tick. Exposed for tests.</summary>
        public ManaSceneNode? VolumeMarker => _marker.Node;

        /// <summary>Raised on the tick the player steps into the volume.</summary>
        public event Action<ManaPawnNode, ManaGameState>? PlayerEntered;

        /// <summary>Raised on every tick the player is inside, after <see cref="PlayerEntered"/> -- how a damage volume keeps hurting.</summary>
        public event Action<ManaPawnNode, ManaGameState>? PlayerStayed;

        /// <summary>Raised on the tick the player leaves the volume.</summary>
        public event Action<ManaPawnNode, ManaGameState>? PlayerExited;

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var world = Owner.Context.World;
            _marker.Update(!world.IsPlaying, Size, Offset, EditorColor);

            var player = world.GameState.Player;
            if (!world.IsPlaying || player == null)
            {
                _playerInside = false;
                return;
            }

            var state = world.GameState;
            var inside = Contains(
                Owner.WorldTransform,
                Offset,
                Size,
                player.WorldTransform.Translation,
                PlayerRadius,
                ManaPawnNode.ReferenceHeight);

            if (inside && !_playerInside)
            {
                _playerInside = true;
                PlayerEntered?.Invoke(player, state);
            }
            else if (!inside && _playerInside)
            {
                _playerInside = false;
                PlayerExited?.Invoke(player, state);
            }

            if (_playerInside)
            {
                PlayerStayed?.Invoke(player, state);
            }
        }

        public override void BeginPlay()
        {
            base.BeginPlay();
            _marker.Update(false, Size, Offset, EditorColor);
        }

        public override void EndPlay()
        {
            _playerInside = false;
            base.EndPlay();
        }

        /// <summary>
        /// Whether the player's box overlaps the trigger's. The player is moved into the trigger's own frame
        /// (position and rotation, not scale) and the trigger grown by the player's half-extents, so it is one
        /// point-in-box test however the trigger is turned.
        /// </summary>
        public static bool Contains(
            Matrix4x4 triggerWorld,
            Vector3 offset,
            Vector3 size,
            Vector3 playerFeet,
            float playerRadius,
            float playerHeight)
        {
            if (!Matrix4x4.Decompose(triggerWorld, out _, out var rotation, out var position))
            {
                return false;
            }

            var playerCentre = playerFeet + new Vector3(0.0f, playerHeight * 0.5f, 0.0f);
            var local = Vector3.Transform(playerCentre - position, Quaternion.Inverse(rotation)) - offset;
            var half = (size * 0.5f) + new Vector3(playerRadius, playerHeight * 0.5f, playerRadius);

            return MathF.Abs(local.X) <= half.X
                && MathF.Abs(local.Y) <= half.Y
                && MathF.Abs(local.Z) <= half.Z;
        }
    }
}
