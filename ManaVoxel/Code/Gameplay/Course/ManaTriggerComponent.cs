using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// A box that notices the player walking into it. The base of every course piece -- coins, checkpoints,
    /// hazards, the goal -- so each of those is only "what happens on enter".
    /// <para>
    /// A plain overlap test each tick rather than a physics sensor: there is one player, so one box test
    /// per trigger is cheaper than teaching the narrow phase about non-solid bodies, and it stays testable
    /// without a physics world. Only ticks while the world is playing, so triggers are inert in the editor.
    /// </para>
    /// </summary>
    public abstract class ManaTriggerComponent : ManaComponent
    {
        // What the player's capsule is treated as: a box this wide and ReferenceHeight tall, feet at the
        // node origin. Slightly generous is right for a trigger -- grazing a coin should collect it.
        public const float PlayerRadius = 0.4f;

        private bool _playerInside;
        private readonly EditorVolumeMarker _marker;

        protected ManaTriggerComponent(ManaSceneNode owner) : base(owner)
        {
            _marker = new EditorVolumeMarker(owner);
        }

        /// <summary>Full extents of the volume, in the node's own axes. Node scale does not change it.</summary>
        public Vector3 Size { get; set; } = new(2.0f, 2.0f, 2.0f);

        /// <summary>Centre of the volume relative to the node, so a coin's volume can sit around the model.</summary>
        public Vector3 Offset { get; set; }

        [JsonIgnore]
        public bool IsPlayerInside => _playerInside;

        /// <summary>Colour of the editor wireframe, so the pieces can be told apart at a glance.</summary>
        protected virtual Vector4 VolumeColor => new(1.0f, 1.0f, 1.0f, 1.0f);

        /// <summary>The editor wireframe, or null before the first tick. Exposed for tests.</summary>
        public ManaSceneNode? VolumeMarker => _marker.Node;

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var world = Owner.Context.World;
            _marker.Update(!world.IsPlaying, Size, Offset, VolumeColor);

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
                OnPlayerEnter(player, state);
            }
            else if (!inside && _playerInside)
            {
                _playerInside = false;
                OnPlayerExit(player, state);
            }

            if (_playerInside)
            {
                OnPlayerStay(player, state);
            }
        }

        public override void BeginPlay()
        {
            base.BeginPlay();
            _marker.Update(false, Size, Offset, VolumeColor);
        }

        public override void EndPlay()
        {
            _playerInside = false;
            base.EndPlay();
        }

        protected virtual void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
        }

        protected virtual void OnPlayerExit(ManaPawnNode player, ManaGameState state)
        {
        }

        /// <summary>Every tick the player is inside, after enter -- how a damage volume keeps hurting.</summary>
        protected virtual void OnPlayerStay(ManaPawnNode player, ManaGameState state)
        {
        }

        /// <summary>
        /// Whether the player's box overlaps the trigger's. The player is moved into the trigger's own
        /// frame (position and rotation, not scale) and the trigger grown by the player's half-extents, so
        /// it is one point-in-box test however the trigger is turned.
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
