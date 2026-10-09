using Mana.Code.Automation;
using ManaUI;

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

        /// <summary>This mode's on-screen furniture while playing, or null. Built once per play.</summary>
        public virtual UiElement? BuildHud(UiTheme theme) => null;
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

        public IReadOnlyList<ManaLevelGameMode> Running => _running;

        public bool IsRunning => _running.Count > 0;

        /// <summary>Whether any of these modes wants the host's character; with none, play is a free debug camera.</summary>
        public static bool AnyWantsPlayerCharacter(IEnumerable<ManaLevelGameMode> modes) => modes.Any(mode => mode.WantsPlayerCharacter);

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
        }

        public void Tick(float deltaSeconds)
        {
            foreach (var mode in _running)
            {
                mode.Tick(deltaSeconds);
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

            _running.Clear();
            _hudLayer.Children.Clear();
        }
    }
}
