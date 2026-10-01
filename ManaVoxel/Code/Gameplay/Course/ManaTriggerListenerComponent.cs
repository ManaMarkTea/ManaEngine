using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// A component that reacts to the player entering, staying in, or leaving a <see cref="TriggerVolumeComponent"/>
    /// on the same node -- the wiring that lets a coin, a hazard or a checkpoint be "just the behaviour" while the
    /// volume lives in one place.
    /// <para>
    /// It finds the node's trigger when it is created, adding one with the piece's own defaults if the node has
    /// none, so a piece dropped on a bare node still has a volume to see and edit. It subscribes when play begins
    /// and lets go when it ends.
    /// </para>
    /// <para>
    /// <see cref="Size"/>, <see cref="Offset"/> and friends forward to that trigger. They are conveniences for code
    /// and tests, hidden from the inspector and from saved levels: the volume is edited on the Trigger Volume
    /// component, and saved as one.
    /// </para>
    /// </summary>
    public abstract class ManaTriggerListenerComponent : ManaComponent
    {
        private TriggerVolumeComponent? _trigger;
        private ManaComponentReference _triggerSource;

        protected ManaTriggerListenerComponent(ManaSceneNode owner) : base(owner)
        {
            _trigger = FindOrCreateTrigger();
        }

        /// <summary>
        /// Which trigger volume on the node to listen to. Left empty it uses the first one, which is what nearly
        /// every node wants; point it at another when a node has several volumes and this piece belongs to one.
        /// </summary>
        [ComponentReference(typeof(TriggerVolumeComponent))]
        public ManaComponentReference TriggerSource
        {
            get => _triggerSource;
            set
            {
                _triggerSource = value;

                // Found again on next use, and at the start of play.
                _trigger = null;
            }
        }

        /// <summary>The volume this listens to, found on the node -- or created, if it has none.</summary>
        [Browsable(false)]
        [JsonIgnore]
        public TriggerVolumeComponent Trigger => _trigger ??= FindOrCreateTrigger();

        /// <summary>Extents given to a trigger created for this piece.</summary>
        protected virtual Vector3 DefaultSize => new(2.0f, 2.0f, 2.0f);

        /// <summary>Centre offset given to a trigger created for this piece.</summary>
        protected virtual Vector3 DefaultOffset => Vector3.Zero;

        /// <summary>Wireframe colour given to a trigger created for this piece.</summary>
        protected virtual Vector4 DefaultColor => new(1.0f, 1.0f, 1.0f, 1.0f);

        [Browsable(false)]
        [JsonIgnore]
        public Vector3 Size
        {
            get => Trigger.Size;
            set => Trigger.Size = value;
        }

        [Browsable(false)]
        [JsonIgnore]
        public Vector3 Offset
        {
            get => Trigger.Offset;
            set => Trigger.Offset = value;
        }

        [Browsable(false)]
        [JsonIgnore]
        public bool IsPlayerInside => Trigger.IsPlayerInside;

        [Browsable(false)]
        [JsonIgnore]
        public ManaSceneNode? VolumeMarker => Trigger.VolumeMarker;

        public override void BeginPlay()
        {
            base.BeginPlay();

            // Looked up again: the trigger may have been removed or replaced in the editor since this was created.
            _trigger = FindOrCreateTrigger();
            Unsubscribe();
            _trigger.PlayerEntered += HandleEntered;
            _trigger.PlayerStayed += HandleStayed;
            _trigger.PlayerExited += HandleExited;
        }

        public override void EndPlay()
        {
            Unsubscribe();
            base.EndPlay();
        }

        protected virtual void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
        }

        protected virtual void OnPlayerStay(ManaPawnNode player, ManaGameState state)
        {
        }

        protected virtual void OnPlayerExit(ManaPawnNode player, ManaGameState state)
        {
        }

        /// <summary>
        /// Volumes used to be saved inside each piece's own script as <c>size</c> and <c>offset</c>. Levels written
        /// that way still load: the level loader hands those two values to the piece, which passes them on to the
        /// node's trigger. Newer levels carry a Trigger script of their own instead.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Vector3 is read by the options' own converter, not by reflection.")]
        internal void ApplyLegacyVolume(IReadOnlyDictionary<string, JsonElement> properties, JsonSerializerOptions options)
        {
            if (properties.TryGetValue("size", out var size))
            {
                Trigger.Size = size.Deserialize<Vector3>(options);
            }

            if (properties.TryGetValue("offset", out var offset))
            {
                Trigger.Offset = offset.Deserialize<Vector3>(options);
            }
        }

        private void HandleEntered(ManaPawnNode player, ManaGameState state) => OnPlayerEnter(player, state);

        private void HandleStayed(ManaPawnNode player, ManaGameState state) => OnPlayerStay(player, state);

        private void HandleExited(ManaPawnNode player, ManaGameState state) => OnPlayerExit(player, state);

        private void Unsubscribe()
        {
            if (_trigger == null)
            {
                return;
            }

            _trigger.PlayerEntered -= HandleEntered;
            _trigger.PlayerStayed -= HandleStayed;
            _trigger.PlayerExited -= HandleExited;
        }

        private TriggerVolumeComponent FindOrCreateTrigger()
        {
            // Under the scene lock: this can run while the render thread is walking the node's components (a level
            // loading in the background attaches nodes before it has finished with them).
            lock (Owner.Context.World.SceneLock)
            {
                if (Owner.GetComponent<TriggerVolumeComponent>(_triggerSource) is { } chosen)
                {
                    return chosen;
                }

                if (Owner.TryGetComponent<TriggerVolumeComponent>(out var existing))
                {
                    // A trigger that came from the level has no idea what it is for; borrow this piece's colour.
                    if (existing.EditorColor == new Vector4(1.0f, 1.0f, 1.0f, 1.0f))
                    {
                        existing.EditorColor = DefaultColor;
                    }

                    return existing;
                }

                var created = new TriggerVolumeComponent(Owner)
                {
                    Size = DefaultSize,
                    Offset = DefaultOffset,
                    EditorColor = DefaultColor
                };
                Owner.Components.Add(created);
                return created;
            }
        }
    }
}
