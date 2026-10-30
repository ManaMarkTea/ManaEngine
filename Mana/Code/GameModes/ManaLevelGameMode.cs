using Mana.Code.Automation;
using ManaUI;
using Mana.Code.Components;
using Mana.Code.Nodes;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// A level's game rules -- what Unreal calls a GameMode and a level picks in its world settings. A level lists
    /// the modes it runs (<c>gameModes</c> in the .manalevel), each with its own settings, and they run whenever that
    /// level is played: F5 in the editor, Play In New Window, <c>--play</c>.
    /// <para>
    /// Not to be confused with <see cref="BaseGameMode"/>, which is the application around the world (the editor, the
    /// play window). That one owns the window; this one owns a match. Several can run at once -- a race timer beside
    /// a weather system, say -- so each should keep to its own job.
    /// </para>
    /// <para>
    /// Settings are the subclass's public read-write properties: the level saves them by name, and the Inspector
    /// shows them when the level's scene is selected. Document each with a one-line summary; that is its tooltip.
    /// </para>
    /// </summary>
    public abstract class ManaLevelGameMode
    {
        /// <summary>The name this mode is registered and saved under, set when it is made.</summary>
        [System.ComponentModel.Browsable(false)]
        public string Type { get; set; } = string.Empty;

        /// <summary>Stable id within the level, so a saved level keeps its modes in order and apart.</summary>
        [System.ComponentModel.Browsable(false)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Whether this mode wants the host's character: spawned at the Player Start (or where Play From Camera
        /// says), possessed, the camera on its boom. Nothing asks for one by default -- a level with no mode that
        /// does is played with a free debug camera, since not every level is a character game.
        /// </summary>
        public virtual bool WantsPlayerCharacter => false;

        /// <summary>
        /// With <see cref="WantsPlayerCharacter"/>: the character is spawned but not possessed -- no walking keys, no
        /// follow camera -- because this mode moves it and the camera itself (a runner keeps it in lanes). It is the
        /// game state's player all the same, so coins, hazards and triggers work on it as on any other.
        /// </summary>
        public virtual bool DrivesPlayerCharacter => false;

        /// <summary>The on-screen controls a touch device shows while this mode runs.</summary>
        public virtual ManaTouchControls TouchControls => ManaTouchControls.Stick;

        /// <summary>
        /// Whether the editor's floor stays while playing in the editor. A level built on it keeps it; a mode that
        /// builds its own world hides it.
        /// </summary>
        public virtual bool KeepsEditorFloor => true;

        /// <summary>Whether the host's own play HUD (controls hint, coins, health, timer) shows beside this mode's.</summary>
        public virtual bool ShowsPlayHud => WantsPlayerCharacter;

        /// <summary>
        /// Whether Escape and R are this mode's to handle. Otherwise Escape stops play (or closes the play window)
        /// and R restarts the level; a mode that pauses on Escape, say, takes them.
        /// </summary>
        public virtual bool HandlesExitKeys => false;

        /// <summary>
        /// Assets the mode loads by itself rather than through the level's nodes (models, pictures, sounds, and prefabs,
        /// whose own models come with them), by reference. Hosts that must fetch ahead -- the web build -- download these before play.
        /// </summary>
        public virtual IEnumerable<string> Assets => Array.Empty<string>();

        /// <summary>Play has started: build what the match needs. Everything added must go again in <see cref="EndPlay"/>.</summary>
        public virtual void BeginPlay(ManaPlaySession session)
        {
        }

        /// <summary>Once a frame while playing, after input and before the world updates.</summary>
        public virtual void Tick(float deltaSeconds)
        {
        }

        /// <summary>Play has stopped: remove what <see cref="BeginPlay"/> added and put back anything borrowed.</summary>
        public virtual void EndPlay()
        {
        }

        /// <summary>
        /// The player asked to start over (R, a pause menu's Restart). True when the mode did it itself; false lets the
        /// host put the character back at the start.
        /// </summary>
        public virtual bool Restart() => false;

        /// <summary>This mode's on-screen furniture while playing, or null. Built once per play.</summary>
        public virtual UiElement? BuildHud(UiTheme theme) => null;

        /// <summary>
        /// The view model a UI canvas naming this mode binds to: its properties (raising PropertyChanged) and
        /// commands are what the layout's "bind" and "on" entries reach. Null when the mode has none.
        /// </summary>
        [System.ComponentModel.Browsable(false)]
        public virtual object? HudViewModel => null;

        /// <summary>The view model's type, known before play: the designer checks a layout's paths and offers them from it.</summary>
        [System.ComponentModel.Browsable(false)]
        public virtual Type? HudViewModelType => HudViewModel?.GetType();

        public virtual void ConnectHudCanvas(UiMarkupResult layout) { }


        /// <summary>
        /// The editor has stepped out of the running match to inspect it (F8): the match is frozen and the editor has
        /// the camera and input. Give back anything borrowed that the editor needs; <see cref="Resume"/> takes it again.
        /// </summary>
        public virtual void Suspend()
        {
        }

        /// <summary>Back into the match from <see cref="Suspend"/>.</summary>
        public virtual void Resume()
        {
        }

        /// <summary>
        /// In the editor, while the level is open and not playing: show what play would build (a mode that builds its
        /// world in code would otherwise leave the level looking empty). Called every editor frame -- build once, again
        /// when the settings change; <paramref name="visible"/> false takes it down (play is starting, or the mode went).
        /// Whatever it adds must stay out of the level: a scene of its own, nodes with no level source.
        /// </summary>
        public virtual void EditorPreview(ManaContext context, bool visible)
        {
        }
    }

    /// <summary>What a touch device puts on screen for a mode.</summary>
    public enum ManaTouchControls
    {
        /// <summary>A thumbstick for W/A/S/D, Jump/Run/Camera buttons, drag to look -- a character walking about.</summary>
        Stick,

        /// <summary>Nothing on screen: swipes left, right, up and down press the arrow keys -- a runner's lanes, jump and duck.</summary>
        Swipe
    }

    /// <summary>What a running mode is given: the world, the host's view of input, and a way out.</summary>
    public sealed class ManaPlaySession
    {
        public ManaPlaySession(ManaContext context, Func<bool> uiWantsMouse, Func<bool> uiWantsKeyboard, Action exit, bool standalone)
        {
            Context = context;
            UiWantsMouse = uiWantsMouse;
            UiWantsKeyboard = uiWantsKeyboard;
            Exit = exit;
            Standalone = standalone;
        }

        public ManaContext Context { get; }

        /// <summary>True while the pointer is over the UI: the board should not act on it.</summary>
        public Func<bool> UiWantsMouse { get; }

        public Func<bool> UiWantsKeyboard { get; }

        /// <summary>Leaves play: back to the editor, or closes the play window.</summary>
        public Action Exit { get; }

        /// <summary>Running as the game on its own (<c>--play</c>) rather than inside the editor.</summary>
        public bool Standalone { get; }

        /// <summary>The script runner when a <c>--command-file</c> is driving the run, for a mode's own commands.</summary>
        public ManaCommandRunner? Commands { get; set; }
    }

    /// <summary>
    /// Runs a set of level game modes through one play: begins them, ticks them, ends them, and holds their HUDs in
    /// one layer a host puts over its own.
    /// </summary>
    public sealed class ManaGameModeRunner
    {
        private readonly List<ManaLevelGameMode> _running = new();
        private readonly Grid _hudLayer = new();
        private UiTheme? _theme;
        private ManaPlaySession? _session;
        private readonly Dictionary<ManaUiCanvasComponent, UiElement> _canvases = new();
        private long _canvasPass;

        public IReadOnlyList<ManaLevelGameMode> Running => _running;

        public bool IsRunning => _running.Count > 0;

        /// <summary>Whether any of these modes wants the host's character; with none, play is a free debug camera.</summary>
        public static bool AnyWantsPlayerCharacter(IEnumerable<ManaLevelGameMode> modes) => modes.Any(mode => mode.WantsPlayerCharacter);

        /// <summary>A mode moves the character itself: the host leaves it unpossessed.</summary>
        public static bool AnyDrivesPlayerCharacter(IEnumerable<ManaLevelGameMode> modes) => modes.Any(mode => mode.DrivesPlayerCharacter);

        /// <summary>The touch controls the running modes want: swipes if any asks for them.</summary>
        public ManaTouchControls TouchControls => _running.Any(mode => mode.TouchControls == ManaTouchControls.Swipe)
            ? ManaTouchControls.Swipe
            : ManaTouchControls.Stick;

        /// <summary>Asks the running modes to start over; true when one did, so the host need not.</summary>
        public bool Restart()
        {
            var handled = false;
            foreach (var mode in _running)
            {
                handled |= mode.Restart();
            }
            return handled;
        }

        // With no modes the host's HUD carries the debug camera's hint.
        public bool ShowsPlayHud => _running.Count == 0 || _running.Any(mode => mode.ShowsPlayHud);

        public bool HandlesExitKeys => _running.Any(mode => mode.HandlesExitKeys);

        public bool KeepsEditorFloor => _running.All(mode => mode.KeepsEditorFloor);

        /// <summary>Where running modes' HUDs go; a host adds this once, over its own play HUD.</summary>
        public UiElement HudLayer => _hudLayer;

        /// <summary>The theme a mode's HUD is built with; set by the host once its UI exists.</summary>
        public void UseTheme(UiTheme theme) => _theme = theme;

        public void Begin(IEnumerable<ManaLevelGameMode> modes, ManaPlaySession session)
        {
            End();
            _session = session;
            foreach (var mode in modes)
            {
                try
                {
                    mode.BeginPlay(session);
                    _running.Add(mode);
                    if (_theme != null && mode.BuildHud(_theme) is { } hud)
                    {
                        _hudLayer.Children.Add(hud);
                    }
                }
                catch (Exception exception)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Game mode '{mode.Type}' failed to start: {exception}");
                }
            }
            FindCanvases();
        }

        // Everything on the scene graph when play starts, before the world has updated once.
        private void FindCanvases()
        {
            if (_session == null || _theme == null)
            {
                return;
            }

            IEnumerable<ManaSceneNode> Walk(ManaSceneNode node)
            {
                yield return node;
                foreach (var child in node.Children)
                {
                    foreach (var descendant in Walk(child))
                    {
                        yield return descendant;
                    }
                }
            }

            List<ManaUiCanvasComponent> live;
            lock (_session.Context.World.SceneLock)
            {
                live = _session.Context.World.Scenes.SelectMany(scene => scene.Children).SelectMany(Walk)
                    .SelectMany(node => node.Components.OfType<ManaUiCanvasComponent>()).ToList();
            }

            _canvasPass = _session.Context.World.UiCanvasPass;
            MatchCanvases(live);
        }

        // Canvases announce themselves as the world updates, so one placed or removed mid-play is noticed for the
        // price of a short list rather than a walk of every node, every frame.
        private void SyncCanvases()
        {
            if (_session?.Context.World is not { } world || _theme == null || world.UiCanvasPass == _canvasPass)
            {
                return;
            }

            _canvasPass = world.UiCanvasPass;
            MatchCanvases(world.UiCanvases);
        }

        private void MatchCanvases(IReadOnlyList<ManaUiCanvasComponent> live)
        {
            if (_canvases.Count == live.Count && live.All(_canvases.ContainsKey))
            {
                return;
            }

            foreach (var old in _canvases.Keys.Where(canvas => !live.Contains(canvas)).ToArray())
            {
                _hudLayer.Children.Remove(_canvases[old]);
                _canvases.Remove(old);
            }

            foreach (var canvas in live)
            {
                if (_canvases.ContainsKey(canvas) || _theme == null)
                {
                    continue;
                }

                var mode = _running.FirstOrDefault(mode => string.Equals(mode.Type, canvas.BindingSource, StringComparison.OrdinalIgnoreCase));
                var layout = canvas.Build(_theme, mode?.HudViewModel);
                foreach (var problem in layout.Problems)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"UI canvas '{canvas.Owner.Name}': {problem}");
                }

                var host = new Border { Child = layout.Root };
                host.BindPolled(b => b.Visibility, () => canvas.Visible switch
                {
                    true => Visibility.Visible,
                    false => Visibility.Collapsed
                });
                _canvases.Add(canvas, host);
                _hudLayer.Children.Add(host);
                mode?.ConnectHudCanvas(layout);
            }
        }

        public void Tick(float deltaSeconds)
        {
            foreach (var mode in _running)
            {
                mode.Tick(deltaSeconds);
            }
            SyncCanvases();
        }

        public void Suspend()
        {
            foreach (var mode in _running)
            {
                mode.Suspend();
            }
        }

        public void Resume()
        {
            foreach (var mode in _running)
            {
                mode.Resume();
            }
        }

        public void End()
        {
            // In reverse, so a mode that started on top of another comes off first.
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                try
                {
                    _running[i].EndPlay();
                }
                catch (Exception exception)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Game mode '{_running[i].Type}' failed to stop: {exception}");
                }
            }

            _session = null;
            _canvases.Clear();
            _running.Clear();
            _hudLayer.Children.Clear();
        }
    }
}
