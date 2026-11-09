using System.ComponentModel;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Content;
using Mana.Code.Nodes;
using Silk.NET.Input;

namespace ManaEditor.Code.Gameplay.Course
{
    /// <summary>
    /// A bow for the side-on player: hold F to draw, let go to loose an arrow the way the character faces
    /// (hold W or Up for a high shot). A longer draw shoots faster and flatter; arrows fall under gravity,
    /// defeat the first enemy they cross and stick into the first solid they meet. Put it on the Player Start
    /// beside Platformer 2D, which supplies the facing; everything it makes is removed when play stops.
    /// </summary>
    public sealed class BowComponent : ManaComponent
    {
        public const string ScriptType = "Bow";

        public static readonly ManaAssetRef WoodenBow = new("fff95836f678434a977f23eb25cc3622", "Sprites/Items/Wooden Bow.manasheet");
        public static readonly ManaAssetRef WoodenArrow = new("76ff27a95efb43ecb89195432a651706", "Sprites/Items/Wooden Arrow.manasheet");

        private const float HighShotRadians = 0.7f;

        [AssetKind(ManaSpriteSheet.Extension), Category("Look")]
        public ManaAssetRef BowSheet { get; set; } = WoodenBow;

        /// <summary>The arrow in flight; pick the one that matches the bow's colours.</summary>
        [AssetKind(ManaSpriteSheet.Extension), Category("Look")]
        public ManaAssetRef ArrowSheet { get; set; } = WoodenArrow;

        /// <summary>Where the bow is held, in metres from the character's feet; X is forward.</summary>
        [Category("Look")]
        public Vector2 HandOffset { get; set; } = new(0.75f, 0.9f);

        /// <summary>Size of the bow and its arrows, as a multiple of the pictures' own size.</summary>
        [Category("Look")]
        public float Scale { get; set; } = 1.5f;

        /// <summary>Seconds of holding the key for a full draw.</summary>
        [Category("Shooting")]
        public float DrawSeconds { get; set; } = 0.6f;

        /// <summary>Metres a second off a quick tap, and off a full draw.</summary>
        [Category("Shooting")]
        public float MinSpeed { get; set; } = 10.0f;

        [Category("Shooting")]
        public float MaxSpeed { get; set; } = 26.0f;

        [Category("Shooting")]
        public float Gravity { get; set; } = 9.0f;

        /// <summary>Wait after a shot before the next draw can begin.</summary>
        [Category("Shooting")]
        public float CooldownSeconds { get; set; } = 0.15f;

        /// <summary>Arrows that may be flying at once; stuck ones do not count.</summary>
        [Category("Shooting")]
        public int MaxInFlight { get; set; } = 5;

        /// <summary>How long an arrow stays in a wall before it is gone.</summary>
        [Category("Shooting")]
        public float StickSeconds { get; set; } = 3.0f;

        [Category("Shooting")]
        public float MaxFlightSeconds { get; set; } = 4.0f;

        private static readonly List<BowComponent> Active = new();

        /// <summary>Holds the fire key down as far as the bow can tell, for command scripts and tests.</summary>
        [JsonIgnore, Browsable(false)]
        public bool HoldFire { get; set; }

        /// <summary>Holds (or lets go of) the fire key of every bow in <paramref name="world"/>.</summary>
        public static void SetHoldFire(ManaWorld world, bool held, bool aimUp)
        {
            lock (Active)
            {
                foreach (var bow in Active.Where(bow => ReferenceEquals(bow.Owner.Context.World, world)))
                {
                    bow.HoldFire = held;
                    bow.HoldAimUp = aimUp;
                }
            }
        }

        [JsonIgnore, Browsable(false)]
        public bool HoldAimUp { get; set; }

        private sealed class Arrow
        {
            public ManaSceneNode Node = null!;
            public Vector3 Tip;
            public Vector3 Velocity;
            public float Age;
            public float StuckFor = -1.0f;
        }

        private readonly List<Arrow> _arrows = new();
        private ManaSceneNode? _bowNode;
        private ManaSpriteSheetComponent? _bowSprite;
        private bool _drawing;
        private bool _keyWasDown;
        private float _drawn;
        private float _cooldown;
        private bool _facingRight = true;
        private float _aimRadians;

        public BowComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Bow";
        }

        [JsonIgnore]
        public bool IsDrawing => _drawing;

        [JsonIgnore]
        public int ArrowsInFlight => _arrows.Count(arrow => arrow.StuckFor < 0.0f);

        /// <summary>0 to 1: how far the current draw has come.</summary>
        [JsonIgnore]
        public float DrawPower => _drawing ? Math.Clamp(_drawn / MathF.Max(0.05f, DrawSeconds), 0.0f, 1.0f) : 0.0f;

        public override void BeginPlay()
        {
            base.BeginPlay();
            Clear();
            lock (Active)
            {
                Active.Add(this);
            }

            _drawing = false;
            _keyWasDown = false;
            HoldFire = false;
            _cooldown = 0.0f;

            var player = Owner.Context.World.GameState.Player;
            if (player == null)
            {
                return;
            }

            var node = new ManaSceneNode(Owner.Context, Matrix4x4.Identity) { Name = "Bow", IsEditorTransient = true };
            _bowSprite = new ManaSpriteSheetComponent(node) { Sheet = BowSheet, Animation = string.Empty, Frame = 0, Unlit = true };
            node.Components.Add(_bowSprite);
            node.Components.Add(new BowDriver(node, this));
            player.AddChild(node);
            _bowNode = node;
        }

        public override void EndPlay()
        {
            lock (Active)
            {
                Active.Remove(this);
            }

            Clear();
            base.EndPlay();
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            var world = Owner.Context.World;
            if (!world.IsPlaying || world.GameState.Player is not { } player)
            {
                return;
            }

            var delta = (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
            var playing = world.GameState.Status == ManaGameStatus.Playing;
            var keyboardFree = Context.GameMode?.UiWantsKeyboard != true;
            var keyboard = keyboardFree ? Context.Input?.Keyboards.FirstOrDefault() : null;

            _facingRight = FacingOf(player);
            _aimRadians = (keyboard != null && (keyboard.IsKeyPressed(Key.W) || keyboard.IsKeyPressed(Key.Up))) || HoldAimUp ? HighShotRadians : 0.0f;

            var down = playing && (HoldFire || keyboard?.IsKeyPressed(Key.F) == true);
            _cooldown = MathF.Max(0.0f, _cooldown - delta);
            if (down && !_keyWasDown && _cooldown <= 0.0f && ArrowsInFlight < MaxInFlight)
            {
                _drawing = true;
                _drawn = 0.0f;
            }

            if (_drawing)
            {
                _drawn += delta;
                if (!down)
                {
                    Loose(player);
                }
            }

            _keyWasDown = down;

            if (_bowSprite != null)
            {
                var power = DrawPower;
                _bowSprite.Frame = !_drawing ? 0 : power < 0.5f ? 1 : 2;
            }

            Fly(world, player, delta);
        }

        private bool FacingOf(ManaPawnNode player)
        {
            if (Owner.GetComponent<ManaPlatformer2DComponent>() is { } platformer)
            {
                return platformer.FacingRight;
            }

            var velocityX = player.Body?.Velocity.X ?? 0.0f;
            return MathF.Abs(velocityX) > 0.3f ? velocityX > 0.0f : _facingRight;
        }

        /// <summary>Where the bow is held and how it is turned, for the node that draws it.</summary>
        private Matrix4x4 BowWorld(ManaPawnNode player)
        {
            var feet = player.WorldTransform.Translation;
            var sign = _facingRight ? 1.0f : -1.0f;
            var angle = _aimRadians * sign;
            var position = feet + new Vector3(HandOffset.X * sign, HandOffset.Y, 0.05f);
            return Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(position);
        }

        private void Loose(ManaPawnNode player)
        {
            _drawing = false;
            _cooldown = CooldownSeconds;

            var power = Math.Clamp(_drawn / MathF.Max(0.05f, DrawSeconds), 0.0f, 1.0f);
            var speed = MinSpeed + ((MaxSpeed - MinSpeed) * power);
            var sign = _facingRight ? 1.0f : -1.0f;
            var direction = new Vector3(MathF.Cos(_aimRadians) * sign, MathF.Sin(_aimRadians), 0.0f);

            var feet = player.WorldTransform.Translation;
            var hand = feet + new Vector3(HandOffset.X * sign, HandOffset.Y, 0.1f);

            var node = new ManaSceneNode(Owner.Context, Matrix4x4.Identity) { Name = "Arrow", IsEditorTransient = true };
            node.Components.Add(new ManaSpriteSheetComponent(node) { Sheet = ArrowSheet, Animation = string.Empty, Frame = 0, Unlit = true });
            Owner.AddChild(node);

            var arrow = new Arrow { Node = node, Tip = hand + (direction * 0.5f * Scale), Velocity = direction * speed };
            _arrows.Add(arrow);
            Place(arrow);
        }

        private void Fly(ManaWorld world, ManaPawnNode player, float delta)
        {
            for (var i = _arrows.Count - 1; i >= 0; i--)
            {
                var arrow = _arrows[i];
                arrow.Age += delta;

                if (arrow.StuckFor >= 0.0f)
                {
                    arrow.StuckFor += delta;
                    if (arrow.StuckFor >= StickSeconds)
                    {
                        Remove(i);
                    }
                    continue;
                }

                if (arrow.Age >= MaxFlightSeconds)
                {
                    Remove(i);
                    continue;
                }

                var velocity = arrow.Velocity;
                velocity.Y -= Gravity * delta;
                var step = velocity * delta;
                var from = arrow.Tip;
                var to = from + step;
                arrow.Velocity = velocity;

                if (EnemyComponent.TryShoot(world, from, to))
                {
                    Remove(i);
                    continue;
                }

                var length = step.Length();
                if (length > 1e-5f && HitSolid(player, from, step / length, length, out var distance))
                {
                    arrow.Tip = from + (step / length * distance);
                    arrow.StuckFor = 0.0f;
                    Place(arrow);
                    continue;
                }

                arrow.Tip = to;
                Place(arrow);
            }
        }

        private bool HitSolid(ManaPawnNode player, Vector3 origin, Vector3 direction, float length, out float distance)
        {
            distance = 0.0f;
            if (Context.Physics == null)
            {
                return false;
            }

            var body = player.Body;
            var handler = body != null && body.Exists
                ? new ExcludeBodyRayHandler(body.Handle)
                : new ExcludeBodyRayHandler(new BepuPhysics.BodyHandle(-1));

            Context.Physics.Simulation.RayCast(origin, direction, length, Context.Physics.BufferPool, ref handler, 0);
            distance = handler.Distance;
            return handler.Hit;
        }

        // The node hangs off this component's own node, so its transform is the wanted world pose taken back
        // through the owner's: a start marker that moved or turned would otherwise carry the arrows with it.
        private void Place(Arrow arrow)
        {
            var heading = MathF.Atan2(arrow.Velocity.Y, arrow.Velocity.X);
            var direction = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0.0f);
            var centre = arrow.Tip - (direction * 0.45f * Scale);
            var world = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationZ(heading) * Matrix4x4.CreateTranslation(centre);
            if (Matrix4x4.Invert(Owner.WorldTransform, out var inverse))
            {
                arrow.Node.LocalTransform = world * inverse;
            }
        }

        private void Remove(int index)
        {
            Owner.RemoveChild(_arrows[index].Node);
            _arrows.RemoveAt(index);
        }

        private void Clear()
        {
            foreach (var arrow in _arrows)
            {
                Owner.RemoveChild(arrow.Node);
            }
            _arrows.Clear();

            if (_bowNode != null)
            {
                _bowNode.Parent?.RemoveChild(_bowNode);
                _bowNode = null;
                _bowSprite = null;
            }
        }

        /// <summary>
        /// Rides on the player like the 2D sprite does: its own transform cancels the player's turn so the picture
        /// stays square to the camera. Ticks before its node's transform, when the player has already moved.
        /// </summary>
        private sealed class BowDriver : ManaComponent
        {
            private readonly BowComponent _bow;

            public BowDriver(ManaSceneNode owner, BowComponent bow) : base(owner)
            {
                _bow = bow;
                Name = "Bow Driver";
            }

            public override bool UpdatesBeforeNodeTransform => true;

            public override void UpdateComponent()
            {
                base.UpdateComponent();
                if (Owner.Parent is not ManaPawnNode player || !Matrix4x4.Invert(player.WorldTransform, out var inverse))
                {
                    return;
                }

                Owner.LocalTransform = _bow.BowWorld(player) * inverse;
                if (Owner.GetComponent<ManaSpriteSheetComponent>() is { } sprite)
                {
                    sprite.FlipX = !_bow._facingRight;
                }
            }
        }
    }
}
