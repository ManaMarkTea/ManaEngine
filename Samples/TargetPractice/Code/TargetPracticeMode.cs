using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ManaCore.Diagnostics;
using ManaUI;

namespace TargetPractice
{
    /// <summary>
    /// The engine's sample game: walk about a level and shoot the targets in it. It is small on purpose, to show the
    /// parts of a game:
    /// <list type="bullet">
    /// <item>a level game mode -- this class, which a level lists under <c>gameModes</c> and which the editor, the play
    /// window and the browser all run the same way (<see cref="BeginPlay"/>, <see cref="Tick"/>, <see cref="EndPlay"/>);</item>
    /// <item>the host's character (<see cref="WantsPlayerCharacter"/>), walked and followed by the camera for free;</item>
    /// <item>settings that save with the level and show in the Inspector (the properties below);</item>
    /// <item>the level as the game's content: targets are nodes named "Target ...", moved and added in the editor;</item>
    /// <item>a HUD in ManaUI, and a script command so an automation file can play it.</item>
    /// </list>
    /// The rules are <see cref="TargetRange"/>; this class is the input, the camera ray and the screen.
    /// </summary>
    public sealed class TargetPracticeMode : ManaLevelGameMode
    {
        public const string ModeType = "TargetPractice";
        public const string DisplayName = "Target Practice";
        public const string Description = "Walk about and shoot the targets (nodes named Target...). Click or F fires at the pointer; R starts over.";

        private const float ClickSeconds = 0.35f;
        private const float ClickPixels = 6.0f;

        /// <summary>A node whose name starts with this is a target.</summary>
        [Category("Rules")]
        public string TargetPrefix { get; set; } = "Target";

        /// <summary>How long a hit target takes to shrink away, seconds.</summary>
        [Range(0.05, 1.0), Category("Rules")]
        public float PopSeconds { get; set; } = 0.2f;

        public override bool WantsPlayerCharacter => true;

        // The host's coins-and-health HUD means nothing here; this game draws its own.
        public override bool ShowsPlayHud => false;

        private ManaPlaySession? _session;
        private TargetRange? _range;
        private IKeyboard? _keyboard;
        private Vector2 _pointer;
        private Vector2 _pressedAt;
        private float _heldFor;
        private bool _wasDown;
        private int _shots;

        public override void BeginPlay(ManaPlaySession session)
        {
            _session = session;
            _range = TargetRange.Find(session.Context.World, TargetPrefix, PopSeconds);
            _shots = 0;
            _wasDown = false;
            _keyboard = session.Context.Input?.Keyboards.FirstOrDefault();
            if (_keyboard != null)
            {
                _keyboard.KeyDown += OnKeyDown;
            }
            ManaLog.WriteDiagnostic($"Target Practice: {_range.Total} target(s).");

            // target_shoot <name> fires at that target from the camera; target_log reports the score.
            session.Commands?.Register("target_shoot", command =>
            {
                var node = _range?.NodeOf(command.Name ?? string.Empty);
                if (node == null)
                {
                    ManaLog.WriteDiagnostic($"Command file: target_shoot found no standing target '{command.Name}'");
                    return;
                }
                var eye = session.Context.World.MainCamera.Owner.WorldTransform.Translation;
                var aim = node.WorldTransform.Translation - eye;
                var hit = Shoot(eye, Vector3.Normalize(aim));
                ManaLog.WriteDiagnostic($"Command file: target_shoot '{command.Name}' hit {hit}");
            });
            session.Commands?.Register("target_log", _ =>
                ManaLog.WriteDiagnostic($"Command file: targets {_range?.Down}/{_range?.Total}, shots {_shots}, won {_range?.Won}, time {_range?.Time:F1}"));
        }

        public override void Tick(float deltaSeconds)
        {
            if (_session == null || _range == null)
            {
                return;
            }
            _range.Tick(deltaSeconds);
            ReadMouse(deltaSeconds);
        }

        public override void EndPlay()
        {
            if (_keyboard != null)
            {
                _keyboard.KeyDown -= OnKeyDown;
                _keyboard = null;
            }
            // The level goes back as it was, so nothing a round did is ever saved into it.
            _range?.Reset();
            _range = null;
            _session = null;
        }

        public override bool Restart()
        {
            _range?.Reset();
            _shots = 0;
            // The host puts the character back at the Player Start.
            return false;
        }

        // A left click fires where the pointer was. A left drag orbits the camera (the host's mouse scheme), so a press is
        // a shot only if it is let go quickly and has not travelled: the pointer is read before the press, because the
        // look drag grabs it.
        private void ReadMouse(float deltaSeconds)
        {
            var mouse = _session!.Context.Input?.Mice.FirstOrDefault();
            if (mouse == null)
            {
                return;
            }
            var down = mouse.IsButtonPressed(MouseButton.Left);
            if (!down)
            {
                if (_wasDown && _heldFor <= ClickSeconds && Vector2.Distance(mouse.Position, _pressedAt) <= ClickPixels)
                {
                    FireAtPointer(_pressedAt);
                }
                _pointer = mouse.Position;
            }
            else if (!_wasDown)
            {
                _pressedAt = _pointer;
                _heldFor = 0.0f;
                if (_session.UiWantsMouse())
                {
                    // Pressed on a button or panel: not a shot.
                    _pressedAt = new Vector2(float.MinValue, float.MinValue);
                }
            }
            else
            {
                _heldFor += deltaSeconds;
            }
            _wasDown = down;
        }

        private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            if (key == Key.F && _session != null && !_session.UiWantsKeyboard())
            {
                FireAtPointer(_pointer);
            }
        }

        private void FireAtPointer(Vector2 pointer)
        {
            if (_session == null || pointer.X < 0.0f)
            {
                return;
            }
            var ray = _session.Context.World.MainCamera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            Shoot(ray.Origin, ray.Direction);
        }

        private bool Shoot(Vector3 origin, Vector3 direction)
        {
            if (_session == null || _range == null)
            {
                return false;
            }
            _shots++;
            return _range.Shoot(_session.Context.World, origin, direction, _session.Context.World.GameState.Player);
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
            grid.Children.Add(Card(Label(() => "WASD  Walk     SPACE  Jump     CLICK or F  Shoot     RIGHT-DRAG  Look     R  Start over     ESC  Quit", 16, Vector4.One))
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
