using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Makes a level play side-on. While playing, the player is held to the plane it spawns on
    /// (A/D run, Space jumps), drawn by a sprite sheet instead of its 3D model -- idle, run and jump frames,
    /// facing the way it last moved -- and watched by a camera from +Z. Put it on any node, typically the
    /// Player Start; everything it changes is put back when play stops.
    /// </summary>
    public sealed class ManaPlatformer2DComponent : ManaComponent
    {
        public string Texture { get; set; } = "Content/Sprites/hero.png";
        public string NormalMap { get; set; } = "Content/Sprites/hero_n.png";
        public int Columns { get; set; } = 4;
        public int Rows { get; set; } = 1;

        /// <summary>Pixels per metre for the player's sprite; 16 makes a 32-pixel frame the character's 2 m.</summary>
        public float PixelsPerUnit { get; set; } = 16.0f;

        public int IdleFrame { get; set; }
        public int RunFirstFrame { get; set; }
        public int RunFrameCount { get; set; } = 4;
        public int JumpFrame { get; set; } = 1;
        public float RunFramesPerSecond { get; set; } = 10.0f;

        /// <summary>Whether the sheet is drawn facing right; a left-facing one is mirrored the other way round.</summary>
        public bool SheetFacesRight { get; set; } = true;

        public float CameraDistance { get; set; } = 12.0f;
        public float CameraPitchDegrees { get; set; } = -5.0f;
        public float CameraHeight { get; set; } = 1.5f;

        private ManaPawnNode? _player;
        private ManaSceneNode? _spriteNode;
        private ManaSpringArmComponent? _arm;
        private readonly List<MeshRendererComponent> _hidden = new();
        private float _savedTurnRate;
        private (float Yaw, float Pitch, float Length, bool Probe, bool FollowYaw, Vector3 Socket) _savedArm;

        public ManaPlatformer2DComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Platformer 2D";
        }

        public ManaPawnNode? Player => _player;
        public ManaSceneNode? SpriteNode => _spriteNode;

        public override void BeginPlay()
        {
            base.BeginPlay();
            var player = Owner.Context.World.GameState.Player;
            if (player == null)
            {
                return;
            }

            _player = player;
            player.PlaneZ = player.Position.Z;
            // Turning is a sprite flip here, so the model's facing may as well arrive at once.
            _savedTurnRate = player.TurnDegreesPerSecond;
            player.TurnDegreesPerSecond = 100000.0f;

            _hidden.Clear();
            HideRenderers(player);

            var node = new ManaSceneNode(Owner.Context, Matrix4x4.Identity) { Name = "2D Player Sprite", IsEditorTransient = true };
            node.Components.Add(new ManaSpriteComponent(node)
            {
                Texture = Texture, NormalMap = NormalMap, Columns = Columns, Rows = Rows, PixelsPerUnit = PixelsPerUnit
            });
            node.Components.Add(new SpriteDriver(node, this));
            player.AddChild(node);
            _spriteNode = node;
        }

        public override void EndPlay()
        {
            base.EndPlay();
            if (_player == null)
            {
                return;
            }

            _player.PlaneZ = null;
            _player.TurnDegreesPerSecond = _savedTurnRate;
            foreach (var renderer in _hidden)
            {
                renderer.IsVisible = true;
            }
            _hidden.Clear();

            if (_spriteNode != null)
            {
                _player.RemoveChild(_spriteNode);
                _spriteNode = null;
            }

            if (_arm != null)
            {
                _arm.YawRadians = _savedArm.Yaw;
                _arm.PitchRadians = _savedArm.Pitch;
                _arm.ArmLength = _savedArm.Length;
                _arm.ProbeForCollision = _savedArm.Probe;
                _arm.FollowTargetYaw = _savedArm.FollowYaw;
                _arm.SocketOffset = _savedArm.Socket;
                _arm = null;
            }
            _player = null;
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (_player == null)
            {
                return;
            }

            // The boom is made on possession, which can come after BeginPlay; held side-on every frame so
            // the arrow keys and mouse drags that orbit it in 3D do nothing here.
            var arm = _player.GetComponent<ManaSpringArmComponent>();
            if (arm != null && !ReferenceEquals(arm, _arm))
            {
                _arm = arm;
                _savedArm = (arm.YawRadians, arm.PitchRadians, arm.ArmLength, arm.ProbeForCollision, arm.FollowTargetYaw, arm.SocketOffset);
                arm.SnapToTarget();
            }

            // Every frame, not once: a character's model finishes loading after play has begun.
            HideRenderers(_player);

            if (_arm != null)
            {
                _arm.YawRadians = 0.0f;
                _arm.PitchRadians = CameraPitchDegrees * MathF.PI / 180.0f;
                _arm.ArmLength = CameraDistance;
                _arm.ProbeForCollision = false;
                _arm.FollowTargetYaw = false;
                _arm.SocketOffset = new Vector3(0.0f, CameraHeight, 0.0f);
            }
        }

        private void HideRenderers(ManaSceneNode node)
        {
            foreach (var renderer in node.GetComponents<MeshRendererComponent>())
            {
                if (renderer.IsVisible)
                {
                    renderer.IsVisible = false;
                    _hidden.Add(renderer);
                }
            }
            foreach (var child in node.Children)
            {
                if (!ReferenceEquals(child, _spriteNode))
                {
                    HideRenderers(child);
                }
            }
        }

        /// <summary>The frame to show: jumping in the air, running while moving, idle otherwise.</summary>
        public int FrameFor(bool grounded, float speedX, double runSeconds)
        {
            if (!grounded)
            {
                return JumpFrame;
            }
            if (MathF.Abs(speedX) > 0.3f)
            {
                var step = (int)Math.Floor(runSeconds * Math.Max(RunFramesPerSecond, 0.0f));
                return RunFirstFrame + step % Math.Max(1, RunFrameCount);
            }
            return IdleFrame;
        }

        /// <summary>
        /// Rides on the player: cancels its turn and scale so the picture stays square to the camera, and
        /// picks the frame. Ticks before its node's transform, when the player has already moved this frame.
        /// </summary>
        private sealed class SpriteDriver : ManaComponent
        {
            private readonly ManaPlatformer2DComponent _settings;
            private double _runSeconds;
            private bool _facingRight = true;

            public SpriteDriver(ManaSceneNode owner, ManaPlatformer2DComponent settings) : base(owner)
            {
                _settings = settings;
                Name = "2D Player Sprite Driver";
            }

            public override bool UpdatesBeforeNodeTransform => true;

            public override void UpdateComponent()
            {
                base.UpdateComponent();
                if (Owner.Parent is not ManaPawnNode player || !Matrix4x4.Invert(player.WorldTransform, out var inverse))
                {
                    return;
                }

                Owner.LocalTransform = Matrix4x4.CreateTranslation(player.WorldTransform.Translation) * inverse;

                var velocityX = player.Body?.Velocity.X ?? 0.0f;
                if (MathF.Abs(velocityX) > 0.3f)
                {
                    _facingRight = velocityX > 0.0f;
                    _runSeconds += ManaTime.GlobalTime.DeltaTime.TotalSeconds;
                }
                else
                {
                    _runSeconds = 0.0;
                }

                if (Owner.GetComponent<ManaSpriteComponent>() is { } sprite)
                {
                    sprite.Frame = _settings.FrameFor(player.IsGrounded, velocityX, _runSeconds);
                    sprite.FlipX = _facingRight != _settings.SheetFacesRight;
                }
            }
        }
    }
}
