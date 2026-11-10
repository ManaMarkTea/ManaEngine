using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
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
    /// <item>content as data: targets, crates, the gun and the pea are prefabs (Content/Prefabs/Target Practice), the
    /// HUD a .manaui layout bound to <see cref="TargetPracticeHud"/>, sounds AudioSources on cues;</item>
    /// <item>script commands so an automation file can play it.</item>
    /// </list>
    /// The rules are <see cref="TargetRange"/>; this class is the input, the aim and the screen.
    /// </summary>
    public sealed class TargetPracticeMode : ManaLevelGameMode
    {
        public const string ModeType = "TargetPractice";
        public const string DisplayName = "Target Practice";
        public const string Description = "Walk about and shoot the targets (nodes named Target...) with a pea shooter. The mouse aims, click or F fires; R starts over.";

        private const string GunArm = "arm-right";

        /// <summary>The node in the gun prefab peas leave from.</summary>
        public const string MuzzleName = "Muzzle";

        // Metres a shot is aimed for when the crosshair is on nothing at all.
        private const float DefaultRange = 30.0f;

        // In the arm's own units: the arm pivots at the shoulder and hangs 1 down its -Y; its front (+Z) faces up when
        // raised, so the gun sits on top of the fist.
        private static readonly Vector3 Hand = new(-0.2f, -0.9f, 0.2f);

        // The gun prefab's barrel is its -Z: along the arm's -Y, toward the hand. Its top (+Y) along the arm's front (+Z).
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

        /// <summary>The world's downward pull while playing, m/s squared; the engine's -64 is a platformer's, too snappy here.</summary>
        [Range(5.0, 64.0), Category("Movement")]
        public float Gravity { get; set; } = 24.0f;

        /// <summary>How high a held jump goes, metres.</summary>
        [Range(0.5, 4.0), Category("Movement")]
        public float JumpHeight { get; set; } = 1.6f;

        /// <summary>
        /// The gun put in the character's hand: its grip at the prefab's origin, barrel along -Z, a "Muzzle" node at
        /// the end, and an AudioSource on the Shoot cue for its sound.
        /// </summary>
        [Category("Content")]
        public string GunPrefab { get; set; } = "Prefabs/Target Practice/Pea Shooter.manaprefab";

        /// <summary>What a pea looks like; its physics is the game's.</summary>
        [Category("Content")]
        public string PeaPrefab { get; set; } = "Prefabs/Target Practice/Pea.manaprefab";

        /// <summary>The pixel effect a target goes down in (a pixelfx id); empty for none.</summary>
        [Category("Content")]
        public string HitEffect { get; set; } = "impacts/hit-spark";

        /// <summary>The pixel effect over each target when the last one falls; empty for none.</summary>
        [Category("Content")]
        public string WinEffect { get; set; } = "explosions/firework-burst";

        public override IEnumerable<string> Assets => [GunPrefab, PeaPrefab];

        public override bool WantsPlayerCharacter => true;

        // The host's coins-and-health HUD means nothing here; this game draws its own.
        public override bool ShowsPlayHud => false;

        private readonly TargetPracticeHud _hud = new();
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
        private ManaSceneNode? _muzzle;
        private bool _celebrated;
        private Vector3 _savedGravity = ManaPhysics.DefaultGravity;
        private float _savedJumpHeight;
        private (float Yaw, float Pitch, int Frames)? _lookGlide;

        public override void BeginPlay(ManaPlaySession session)
        {
            _session = session;
            _range = TargetRange.Find(session.Context.World, TargetPrefix, PopSeconds);
            _peas = new PeaShooter(session.Context, new ManaModelLibrary(session.Context).Model(PeaPrefab));
            _peas.Hit += OnHit;
            _shots = 0;
            _celebrated = false;
            _savedGravity = session.Context.Physics.Gravity;
            session.Context.Physics.Gravity = new Vector3(0.0f, -Gravity, 0.0f);
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
                // Led: aimed where a moving one will be when the pea gets there (twice, as the lead changes the distance).
                var aim = node.WorldTransform.Translation;
                for (var pass = 0; pass < 2; pass++)
                {
                    var flight = Vector3.Distance(Muzzle(), aim) / PeaShooter.Speed;
                    aim = node.WorldTransform.Translation + _range!.VelocityOf(node) * flight;
                }
                LaunchAt(aim);
                ManaLog.WriteDiagnostic($"Command file: target_shoot '{command.Name}' fired");
            });
            // target_look [yaw, pitch] in degrees points the camera, as the mouse would.
            session.Commands?.Register("target_look", command =>
            {
                if (_boom == null || command.Position == null || command.Position.Length < 2)
                {
                    return;
                }
                var yaw = command.Position[0].ToRadians();
                var pitch = Math.Clamp(command.Position[1], _boom.MinPitchDegrees, _boom.MaxPitchDegrees).ToRadians();
                if (command.Frames > 0)
                {
                    // Glides there a frame at a time, as a moving mouse would.
                    _lookGlide = (yaw, pitch, command.Frames);
                    return;
                }
                _boom.YawRadians = yaw;
                _boom.PitchRadians = pitch;
                ManaLog.WriteDiagnostic($"Command file: target_look camera at {_boom.CameraPosition}, arm {_boom.CurrentArmLength:F2}");
            });
            session.Commands?.Register("target_log", _ =>
                ManaLog.WriteDiagnostic($"Command file: targets {_range?.Down}/{_range?.Total}, shots {_shots}, peas {_peas?.InFlight}, gun {_gun != null}, won {_range?.Won}, time {_range?.Time:F1}, standing [{string.Join(", ", _range?.Standing ?? Array.Empty<string>())}], camera {_boom?.CameraPosition}, arm {_boom?.CurrentArmLength:F2}"));
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
            if (_lookGlide is var (yaw, pitch, frames) && _boom != null)
            {
                _boom.YawRadians += (yaw - _boom.YawRadians) / frames;
                _boom.PitchRadians += (pitch - _boom.PitchRadians) / frames;
                _lookGlide = null;
                if (frames > 1)
                {
                    _lookGlide = (yaw, pitch, frames - 1);
                }
            }
            Aim();
            _range.Tick(deltaSeconds);
            _peas?.Tick(deltaSeconds, _range);
            if (_range.Won && !_celebrated)
            {
                Celebrate();
            }
            _hud.Refresh(_range.Down, _range.Total, _range.Time, _shots, _range.Won);
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
            if (_session != null)
            {
                _session.Context.Physics.Gravity = _savedGravity;
            }
            _range?.Reset();
            _range = null;
            _session = null;
        }

        // Ejected to inspect the match (F8): the editor needs the pointer.
        public override void Suspend()
        {
            if (_boom != null)
            {
                _boom.ActionLook = false;
            }
        }

        public override void Resume()
        {
            if (_boom != null)
            {
                _boom.ActionLook = true;
            }
        }

        public override bool Restart()
        {
            _range?.Reset();
            _peas?.Clear();
            _shots = 0;
            _celebrated = false;
            // The host puts the character back at the Player Start.
            return false;
        }

        // The target's own sound (an AudioSource on its EnemyDefeat cue, in the prefab) and a burst of sparks where the pea struck.
        private void OnHit(ManaSceneNode target, Vector3 where)
        {
            Cue(target, ManaSoundCue.EnemyDefeat);
            if (HitEffect.Length > 0 && _peas != null)
            {
                ManaPixelFxSpawner.Spawn(_peas.Effects, HitEffect, where, scale: 0.5f);
            }
        }

        // A placed prefab is a node holding the prefab's own root, so the cue goes to whichever node in it listens.
        private static void Cue(ManaSceneNode placed, ManaSoundCue cue)
        {
            foreach (var node in TargetRange.Subtree(placed).ToList())
            {
                ManaAudioCueRouter.EmitAt(node, cue);
            }
        }

        // The last one down: whatever in the level listens for the Goal cue (a fanfare), and fireworks where each target stood.
        private void Celebrate()
        {
            _celebrated = true;
            _session?.Context.World.AudioCues.Emit(ManaSoundCue.Goal);
            if (WinEffect.Length == 0 || _peas == null || _range == null)
            {
                return;
            }
            foreach (var node in _range.Nodes)
            {
                ManaPixelFxSpawner.Spawn(_peas.Effects, WinEffect, node.WorldTransform.Translation + new Vector3(0.0f, 1.5f, 0.0f), scale: 1.5f);
            }
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
            // How far the crosshair points decides how much the pea is lifted for its drop. It is what the ray meets, or
            // nearer, a standing target the ray passes close by (one seen against the sky or a far wall); with neither,
            // a usual range.
            var distance = DefaultRange;
            var hit = ManaScenePicker.PickWithHit(_session.Context.World.Scenes, eye, look, exclude);
            if (hit != null)
            {
                distance = Vector3.Distance(eye, hit.Value.Position);
            }
            var near = _range?.RangeAlong(eye, look, TargetRange.DiscRadius + 1.5f);
            if (near != null && near < distance)
            {
                distance = near.Value;
            }
            LaunchAt(eye + look * distance);
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
            if (_gun != null)
            {
                Cue(_gun, ManaSoundCue.Shoot);
            }
        }

        private Vector3 Muzzle()
        {
            if (_muzzle != null)
            {
                return _muzzle.WorldTransform.Translation;
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
                // An action game's mouse: the view always follows it, the pointer held.
                _boom.ActionLook = true;
            }
            _savedJumpHeight = player.JumpHeight;
            player.JumpHeight = JumpHeight;

            var arm =TargetRange.Subtree(player).FirstOrDefault(n => n.Name == GunArm);
            if (arm == null)
            {
                return;
            }
            _aiming = new AimingArm(player, arm);
            player.Components.Add(_aiming);

            // The gun is a prefab, its sound and muzzle in it; placed into the peas' scene, then handed to the arm.
            if (_peas == null)
            {
                return;
            }
            var gun = ManaPrefabs.Spawn(_session!.Context, _peas.Scene, GunPrefab, GunMount * Matrix4x4.CreateTranslation(Hand));
            if (gun == null)
            {
                return;
            }
            lock (_session.Context.World.SceneLock)
            {
                _peas.Scene.Children.Remove(gun);
                arm.AddChild(gun);
            }
            gun.UpdateTransform();
            _gun = gun;
            _muzzle = ManaPrefabs.Find(gun, MuzzleName);
        }

        // Puts the character back as the host made it.
        private void TakeOff()
        {
            if (_gun != null)
            {
                ManaPrefabs.Despawn(_gun);
                _gun = null;
                _muzzle = null;
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
                _boom.ActionLook = false;
                _boom = null;
            }
            if (_player != null)
            {
                _player.JumpHeight = _savedJumpHeight;
            }
            _player = null;
        }

        // The HUD is a layout (Content/UI/Target Practice HUD.manaui) shown by a UiCanvas script in the level; this is what it binds to.
        // Its properties are found by name, which the trimmed web build would strip without this.
        public override object? HudViewModel
        {
            [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TargetPracticeHud))]
            get => _hud;
        }

        public override Type? HudViewModelType => typeof(TargetPracticeHud);
    }
}
