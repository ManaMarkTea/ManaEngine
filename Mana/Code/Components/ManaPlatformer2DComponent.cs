using System.ComponentModel;
using Mana.Code.Content;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Makes a level play side-on. While playing, the player is held to the plane it spawns on
    /// (A/D run, Space jumps), drawn from a sprite sheet asset instead of its 3D model -- its idle, run and jump
    /// animations, facing the way it last moved -- and watched by a camera from +Z. Put it on any node, typically the
    /// Player Start; everything it changes is put back when play stops.
    /// </summary>
    public sealed class ManaPlatformer2DComponent : ManaComponent
    {
        /// <summary>The sample character Kenney's pack provides: Content/Sprites/Characters/Green Alien.manasheet.</summary>
        public static readonly ManaAssetRef GreenAlien = new("5c2e8f0d3b7a4e6f9d1c2b3a4f5e6d7c", "Sprites/Characters/Green Alien.manasheet");

        /// <summary>The player's sprite sheet: its pictures, size and animations are set up in the sheet.</summary>
        [AssetKind(ManaSpriteSheet.Extension), Category("Character")]
        public ManaAssetRef Sheet { get; set; } = GreenAlien;

        /// <summary>The sheet's animation played standing still.</summary>
        [Category("Character")]
        public string IdleAnimation { get; set; } = "idle";

        [Category("Character")]
        public string RunAnimation { get; set; } = "walk";

        /// <summary>Played in the air, going up or coming down.</summary>
        [Category("Character")]
        public string JumpAnimation { get; set; } = "jump";

        /// <summary>Whether the sheet's pictures face right; a left-facing one is mirrored the other way round.</summary>
        [Category("Character")]
        public bool SheetFacesRight { get; set; } = true;

        /// <summary>Crisp square pixels; off for smooth art like Kenney's.</summary>
        [Category("Character")]
        public bool PixelArt { get; set; }

        /// <summary>Drawn as painted, ignoring the lights -- right for flat art like Kenney's.</summary>
        [Category("Character")]
        public bool Unlit { get; set; } = true;

        /// <summary>The player's full, held jump in metres while playing side-on; 0 keeps the character's own.</summary>
        [Category("Movement")]
        public float JumpHeight { get; set; } = 4.5f;

        [Category("Camera")]
        public float CameraDistance { get; set; } = 12.0f;

        /// <summary>How far the camera looks down, in perspective only; orthographic always looks level.</summary>
        [Category("Camera")]
        public float CameraPitchDegrees { get; set; } = -5.0f;

        [Category("Camera")]
        public float CameraHeight { get; set; } = 1.5f;

        /// <summary>Vertical follow responsiveness per second; lower softens jumps, 0 follows height immediately.</summary>
        [Category("Camera")]
        public float CameraVerticalLagSpeed { get; set; } = 3.0f;

        /// <summary>Flat, no-perspective view -- every layer the same size however far back. Off shows depth.</summary>
        [Category("Camera")]
        public bool Orthographic { get; set; } = true;

        /// <summary>How many metres of the level the orthographic view shows top to bottom.</summary>
        [Category("Camera")]
        public float ViewHeight { get; set; } = 12.0f;

        private ManaPawnNode? _player;
        private ManaSceneNode? _spriteNode;
        private ManaSpringArmComponent? _arm;
        private readonly List<MeshRendererComponent> _hidden = new();
        private float _savedTurnRate;
        private float _savedJumpHeight;
        private CameraComponent? _camera;
        private (bool Orthographic, float Height) _savedProjection;
        private (float Yaw, float Pitch, float Length, bool Probe, bool FollowYaw, Vector3 Socket, float? VerticalLag) _savedArm;

        public ManaPlatformer2DComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Platformer 2D";
        }

        public ManaPawnNode? Player => _player;

        /// <summary>The way the character last moved, for things that follow its aim (a bow).</summary>
        public bool FacingRight { get; private set; } = true;
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
            _savedJumpHeight = player.JumpHeight;
            if (JumpHeight > 0.0f)
            {
                player.JumpHeight = JumpHeight;
            }

            _hidden.Clear();
            HideRenderers(player);

            var node = new ManaSceneNode(Owner.Context, Matrix4x4.Identity) { Name = "2D Player Sprite", IsEditorTransient = true };
            node.Components.Add(new ManaSpriteSheetComponent(node) { Sheet = Sheet, Animation = IdleAnimation, PixelArt = PixelArt, Unlit = Unlit });
            node.Components.Add(new SpriteDriver(node, this));
            player.AddChild(node);
            _spriteNode = node;

            if (Orthographic && Owner.Context.World.MainCamera is { } camera)
            {
                _camera = camera;
                _savedProjection = (camera.OrthoGraphic, camera.OrthographicHeight);
                camera.OrthographicHeight = MathF.Max(1.0f, ViewHeight);
                camera.SetProjectionMode(true);
                camera.UpdateProjection();
            }
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
            _player.JumpHeight = _savedJumpHeight;
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

            if (_camera != null)
            {
                _camera.OrthographicHeight = _savedProjection.Height;
                _camera.SetProjectionMode(_savedProjection.Orthographic);
                _camera.UpdateProjection();
                _camera = null;
            }

            if (_arm != null)
            {
                _arm.YawRadians = _savedArm.Yaw;
                _arm.PitchRadians = _savedArm.Pitch;
                _arm.ArmLength = _savedArm.Length;
                _arm.ProbeForCollision = _savedArm.Probe;
                _arm.FollowTargetYaw = _savedArm.FollowYaw;
                _arm.SocketOffset = _savedArm.Socket;
                _arm.VerticalSocketLagSpeed = _savedArm.VerticalLag;
                _arm.SnapToTarget();
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
                _savedArm = (arm.YawRadians, arm.PitchRadians, arm.ArmLength, arm.ProbeForCollision, arm.FollowTargetYaw, arm.SocketOffset, arm.VerticalSocketLagSpeed);
                arm.SnapToTarget();
            }

            // Every frame, not once: a character's model finishes loading after play has begun.
            HideRenderers(_player);

            if (_arm != null)
            {
                _arm.YawRadians = 0.0f;
                // Looking level in orthographic keeps the ground plane edge-on; any pitch shows it filling the view.
                _arm.PitchRadians = Orthographic ? 0.0f : CameraPitchDegrees * MathF.PI / 180.0f;
                _arm.ArmLength = CameraDistance;
                _arm.ProbeForCollision = false;
                _arm.FollowTargetYaw = false;
                _arm.SocketOffset = new Vector3(0.0f, CameraHeight, 0.0f);
                _arm.VerticalSocketLagSpeed = CameraVerticalLagSpeed;
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
                // Sprites riding on the player (its own, a held bow) are 2D art, not the 3D model being hidden.
                if (!ReferenceEquals(child, _spriteNode) && child.GetComponent<ManaSpriteBase>() == null)
                {
                    HideRenderers(child);
                }
            }
        }

        /// <summary>The animation to play: jumping in the air, running while moving, idle otherwise.</summary>
        public string AnimationFor(bool grounded, float speedX)
        {
            if (!grounded)
            {
                return JumpAnimation;
            }
            return MathF.Abs(speedX) > 0.3f ? RunAnimation : IdleAnimation;
        }

        /// <summary>
        /// Rides on the player: cancels its turn and scale so the picture stays square to the camera, and
        /// picks the frame. Ticks before its node's transform, when the player has already moved this frame.
        /// </summary>
        private sealed class SpriteDriver : ManaComponent
        {
            private readonly ManaPlatformer2DComponent _settings;
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
                }
                _settings.FacingRight = _facingRight;

                if (Owner.GetComponent<ManaSpriteSheetComponent>() is { } sprite)
                {
                    sprite.Animation = _settings.AnimationFor(player.IsGrounded, velocityX);
                    sprite.FlipX = _facingRight != _settings.SheetFacesRight;
                }
            }
        }
    }
}
