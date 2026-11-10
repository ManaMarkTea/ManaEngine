using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ManaCore.Diagnostics;
using Mana.Code.Models;
using ManaUI;

namespace TargetPractice
{
    /// <summary>
    /// The engine's sample game: walk about a level and shoot the targets in it with a pea shooter. It is small on
    /// purpose, to show the parts of a game:
    /// <list type="bullet">
    /// <item>a level game mode -- this class, which a level lists under <c>gameModes</c> and which the editor, the play
    /// window and the browser all run the same way (<see cref="BeginPlay"/>, <see cref="Tick"/>, <see cref="EndPlay"/>);</item>
    /// <item>the host's character (<see cref="WantsPlayerCharacter"/>), walked and followed by the camera for free, here
    /// with the camera moved over its shoulder and a gun in its hand (<see cref="AimingArm"/>);</item>
    /// <item>physics: each pea is a ball in the physics world (<see cref="PeaShooter"/>);</item>
    /// <item>settings that save with the level and show in the Inspector (the properties below);</item>
    /// <item>the level as the game's content: targets are nodes named "Target ...", moved and added in the editor;</item>
    /// <item>a HUD in ManaUI, and a script command so an automation file can play it.</item>
    /// </list>
    /// The rules are <see cref="TargetRange"/>; this class is the input, the aim and the screen.
    /// </summary>
    public sealed class TargetPracticeMode : ManaLevelGameMode
    {
        public const string ModeType = "TargetPractice";
        public const string DisplayName = "Target Practice";
        public const string Description = "Walk about and shoot the targets (nodes named Target...) with a pea shooter. Click or F fires at the crosshair; R starts over.";

        private const string GunAsset = "zip:kenney_space-kit.zip/Models/GLTF format/weapon_gun.glb";
        private const string GunArm = "arm-right";

        // In the arm's own units: the arm pivots at the shoulder and hangs 1 down its -Y; its front (+Z) faces up when
        // raised, so the gun sits on top of the fist.
        private static readonly Vector3 Hand = new(-0.2f, -0.9f, 0.2f);
        private const float GunScale = 1.6f;

        // The gun's barrel is its -Z: along the arm's -Y, toward the hand. Its top (+Y) along the arm's front (+Z).
        private static readonly Matrix4x4 GunMount = new(
            -1.0f, 0.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        /// <summary>A node whose name starts with this is a target.</summary>
        [Category("Rules")]
        public string TargetPrefix { get; set; } = "Target";

        /// <summary>How long a hit target takes to shrink away, seconds.</summary>
        [Range(0.05, 1.0), Category("Rules")]
        public float PopSeconds { get; set; } = 0.2f;

        /// <summary>How far right of the character the camera sits, metres, so it does not hide what it aims at.</summary>
        [Range(0.0, 2.0), Category("Camera")]
        public float ShoulderOffset { get; set; } = 0.6f;

        /// <summary>How far behind the character the camera sits, metres.</summary>
        [Range(1.5, 10.0), Category("Camera")]
        public float CameraDistance { get; set; } = 4.0f;

        public override bool WantsPlayerCharacter => true;

        // The host's coins-and-health HUD means nothing here; this game draws its own.
        public override bool ShowsPlayHud => false;

        private ManaPlaySession? _session;
        private TargetRange? _range;
        private PeaShooter? _peas;
        private IKeyboard? _keyboard;
        private IMouse? _mouse;
        private int _shots;

        // The character's kit, set up once the host has spawned it.
        private ManaPawnNode? _player;
        private ManaSpringArmComponent? _boom;
        private float _boomLength;
        private Vector3 _boomSocket;
        private AimingArm? _aiming;
        private ManaSceneNode? _gun;
        private Vector3 _muzzle;

        public override void BeginPlay(ManaPlaySession session)
        {
            _session = session;
            _range = TargetRange.Find(session.Context.World, TargetPrefix, PopSeconds);
            _peas = new PeaShooter(session.Context);
            _shots = 0;
            _keyboard = session.Context.Input?.Keyboards.FirstOrDefault();
            if (_keyboard != null)
            {
                _keyboard.KeyDown += OnKeyDown;
            }
            // The press itself fires, as the event says: polled once a frame, a quick click could fall between frames.
            _mouse = session.Context.Input?.Mice.FirstOrDefault();
            if (_mouse != null)
            {
                _mouse.MouseDown += OnMouseDown;
            }
            ManaLog.WriteDiagnostic($"Target Practice: {_range.Total} target(s).");

            // target_shoot <name> fires from the gun at that target; target_log reports the score.
            session.Commands?.Register("target_shoot", command =>
            {
                var node = _range?.NodeOf(command.Name ?? string.Empty);
                if (node == null)
                {
                    ManaLog.WriteDiagnostic($"Command file: target_shoot found no standing target '{command.Name}'");
                    return;
                }
                LaunchAt(node.WorldTransform.Translation);
                ManaLog.WriteDiagnostic($"Command file: target_shoot '{command.Name}' fired");
            });
            session.Commands?.Register("target_log", _ =>
                ManaLog.WriteDiagnostic($"Command file: targets {_range?.Down}/{_range?.Total}, shots {_shots}, peas {_peas?.InFlight}, gun {_gun != null}, won {_range?.Won}, time {_range?.Time:F1}, standing [{string.Join(", ", _range?.Standing ?? Array.Empty<string>())}]"));
        }

        public override void Tick(float deltaSeconds)
        {
            if (_session == null || _range == null)
            {
                return;
            }
            var player = _session.Context.World.GameState.Player;
            if (player != _player)
            {
                // The host spawns the character after the modes begin (and again on a respawn), so the kit waits for it.
                TakeOff();
                Equip(player);
            }
            Aim();
            _range.Tick(deltaSeconds);
            _peas?.Tick(deltaSeconds, _range);
        }

        public override void EndPlay()
        {
            if (_keyboard != null)
            {
                _keyboard.KeyDown -= OnKeyDown;
                _keyboard = null;
            }
            if (_mouse != null)
            {
                _mouse.MouseDown -= OnMouseDown;
                _mouse = null;
            }
            // The level goes back as it was, so nothing a round did is ever saved into it.
            TakeOff();
            _peas?.Dispose();
            _peas = null;
            _range?.Reset();
            _range = null;
            _session = null;
        }

        public override bool Restart()
        {
            _range?.Reset();
            _peas?.Clear();
            _shots = 0;
            // The host puts the character back at the Player Start.
            return false;
        }

        private void OnMouseDown(IMouse mouse, MouseButton button)
        {
            if (button == MouseButton.Left && _session != null && !_session.UiWantsMouse())
            {
                FireAtCrosshair();
            }
        }

        private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            if (key == Key.F && _session != null && !_session.UiWantsKeyboard())
            {
                FireAtCrosshair();
            }
        }

        // The camera sits over the right shoulder, and the character faces and points its arm the way it looks.
        private void Aim()
        {
            if (_boom == null)
            {
                return;
            }
            var forward = _boom.GetForward();
            var right = new Vector3(-forward.Z, 0.0f, forward.X);
            if (right.LengthSquared() > 1e-6f)
            {
                right = Vector3.Normalize(right);
            }
            _boom.SocketOffset = _boomSocket + right * ShoulderOffset;
            if (_aiming != null)
            {
                _aiming.Aim = forward;
            }
        }

        // The pea goes from the gun to whatever is under the crosshair, the middle of the screen.
        private void FireAtCrosshair()
        {
            if (_session == null)
            {
                return;
            }
            var camera = _session.Context.World.MainCamera.Owner;
            var eye = camera.WorldTransform.Translation;
            var look = camera.Forward;
            if (_boom != null)
            {
                eye = _boom.CameraPosition;
                look = _boom.GetForward();
            }
            var exclude = new HashSet<ManaSceneNode>(_peas?.Nodes ?? Enumerable.Empty<ManaSceneNode>());
            if (_player != null)
            {
                exclude.UnionWith(TargetRange.Subtree(_player));
            }
            var point = eye + look * 100.0f;
            var hit = ManaScenePicker.PickWithHit(_session.Context.World.Scenes, eye, look, exclude);
            if (hit != null)
            {
                point = hit.Value.Position;
            }
            LaunchAt(point);
        }

        // Spits a pea from the gun at a point, lifted by the drop it will have on the way so it lands there.
        private void LaunchAt(Vector3 point)
        {
            if (_session == null || _peas == null || _range == null || _range.Won)
            {
                return;
            }
            var muzzle = Muzzle();
            var distance = Math.Min(Vector3.Distance(muzzle, point), 60.0f);
            if (distance < 0.01f)
            {
                return;
            }
            var flight = distance / PeaShooter.Speed;
            var heading = Vector3.Normalize(point - muzzle);
            heading.Y += 0.5f * PeaShooter.EffectiveGravity * flight * flight / distance;
            _shots++;
            _peas.Fire(muzzle, Vector3.Normalize(heading));
        }

        private Vector3 Muzzle()
        {
            if (_gun != null)
            {
                return Vector3.Transform(_muzzle, _gun.WorldTransform);
            }
            if (_player != null)
            {
                // No gun to fire from: out in front of the chest, clear of the character's own body.
                return _player.WorldTransform.Translation + new Vector3(0.0f, 1.1f, 0.0f) + _player.FacingDirection * 0.6f;
            }
            return _session!.Context.World.MainCamera.Owner.WorldTransform.Translation;
        }

        // Moves the camera over the shoulder and puts the gun in the right hand: the engine's weapon_gun model, a child of
        // the arm so it swings with it. A build without the model loader, or a character with no such arm, aims without it.
        private void Equip(ManaPawnNode? player)
        {
            _player = player;
            if (player == null)
            {
                return;
            }
            _boom = player.GetComponent<ManaSpringArmComponent>();
            if (_boom != null)
            {
                _boomLength = _boom.ArmLength;
                _boomSocket = _boom.SocketOffset;
                _boom.ArmLength = CameraDistance;
            }

            var arm = TargetRange.Subtree(player).FirstOrDefault(n => n.Name == GunArm);
            if (arm == null)
            {
                return;
            }
            _aiming = new AimingArm(player, arm);
            player.Components.Add(_aiming);

            var model = new ManaModelLibrary(_session!.Context).Model(GunAsset);
            if (model == null)
            {
                return;
            }
            var centre = (model.BoundsMin + model.BoundsMax) * 0.5f;
            var grip = new Vector3(centre.X, model.BoundsMin.Y, centre.Z);
            var placement = Matrix4x4.CreateTranslation(-grip) * Matrix4x4.CreateScale(GunScale) * GunMount * Matrix4x4.CreateTranslation(Hand);
            var gun = model.Instantiate(_session.Context, "Pea Shooter", placement);
            arm.AddChild(gun);
            gun.UpdateTransform();
            _gun = gun;
            // The muzzle in the gun node's own space: the model's front end, through the placement the node already has.
            Matrix4x4.Invert(gun.LocalTransform, out var toGun);
            _muzzle = Vector3.Transform(Vector3.Transform(new Vector3(centre.X, centre.Y, model.BoundsMin.Z), placement), toGun);
        }

        // Puts the character back as the host made it.
        private void TakeOff()
        {
            if (_gun != null)
            {
                _gun.Parent?.RemoveChild(_gun);
                _gun = null;
            }
            if (_aiming != null)
            {
                _aiming.Owner.Components.Remove(_aiming);
                _aiming = null;
            }
            if (_boom != null)
            {
                _boom.ArmLength = _boomLength;
                _boom.SocketOffset = _boomSocket;
                _boom = null;
            }
            _player = null;
        }

        public override UiElement BuildHud(UiTheme theme)
        {
            var gold = new Vector4(1.0f, 0.82f, 0.35f, 1.0f);
            TextBlock Label(Func<string> text, float size, Vector4 color)
            {
                var label = new TextBlock().FontSize(size).Foreground(color);
                label.BindPolled(t => t.Text, text);
                return label;
            }
            Border Card(UiElement child) => new Border().Fill(new Vector4(0.03f, 0.05f, 0.09f, 0.85f)).Outline(gold).Rounded(6).PaddingOf(18, 10).Content(child);

            var grid = new Grid { IsHitTestVisible = false };
            grid.Children.Add(Card(Label(() => Score(), 26, Vector4.One)).Top().Left().Margin(20));
            grid.Children.Add(Label(() => Message(), 34, gold).CenterH().Top().Margin(0, 90, 0, 0));
            grid.Children.Add(Label(() => "+", 30, Vector4.One).Center());
            grid.Children.Add(Card(Label(() => "WASD  Walk     SPACE  Jump     CLICK or F  Fire at the crosshair     RIGHT-DRAG  Aim     R  Start over     ESC  Quit", 16, Vector4.One))
                .CenterH().Bottom().Margin(16));
            return grid;
        }

        private string Score()
        {
            if (_range == null)
            {
                return string.Empty;
            }
            return $"TARGETS  {_range.Down} / {_range.Total}     {_range.Time:F1} s";
        }

        private string Message()
        {
            if (_range == null)
            {
                return string.Empty;
            }
            if (_range.Total == 0)
            {
                return $"No targets: name some nodes '{TargetPrefix}...'";
            }
            if (_range.Won)
            {
                return $"All down in {_range.Time:F1} s with {_shots} shots -- R to go again";
            }
            return string.Empty;
        }
    }
}
