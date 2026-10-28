namespace Mana.Code.GameModes
{
    /// <summary>
    /// A character game: play spawns the character at the Player Start, possesses it and puts the camera behind it,
    /// with the coins, health and timer HUD -- what Play always did before levels chose their rules. Add it to a level
    /// that is walked, run or platformed through; leave it off and play is a free debug camera.
    /// <para>
    /// The spawning itself is the host's (the editor's play mode, the play window, the browser player): this mode is the
    /// level asking for it, with its settings.
    /// </para>
    /// </summary>
    public sealed class ManaCharacterGameMode : ManaLevelGameMode
    {
        public const string ModeType = "Character";

        public override bool WantsPlayerCharacter => true;

        /// <summary>Jumps before landing again: 1 is a single jump, 2 a double jump.</summary>
        [System.ComponentModel.DataAnnotations.Range(1, 4)]
        public int MaxJumps { get; set; } = 2;

        /// <summary>
        /// The editor's floor stays solid under the level. Off for a level whose gaps are falls (a side-on platformer):
        /// past its ground there is nothing to stand on.
        /// </summary>
        public bool Floor { get; set; } = true;

        public override bool KeepsEditorFloor => Floor;

        /// <summary>The character wears a cloth cape that trails as it runs and blows in the wind.</summary>
        public bool Cape { get; set; }

        public System.Numerics.Vector4 CapeColor { get; set; } = new(0.62f, 0.08f, 0.1f, 1.0f);

        private ManaPlaySession? _session;
        private Nodes.ManaPawnNode? _caped;
        private Nodes.ManaSceneNode? _cape;

        public override void BeginPlay(ManaPlaySession session)
        {
            _session = session;
        }

        // Checked every frame rather than once: the player can be swapped for another character, or turned side-on
        // (Platformer2D draws a sprite instead of the model, which a cape would float behind).
        public override void Tick(float deltaSeconds)
        {
            var player = _session?.Context.World.GameState.Player;
            if (!Cape || player?.PlaneZ != null)
            {
                player = null;
            }
            if (player == _caped)
            {
                // Recoloured live from the Inspector while playing.
                if (_cape?.GetComponent<Components.ManaClothComponent>() is { } cloth)
                {
                    cloth.Color = CapeColor;
                }
                return;
            }
            TakeOffCape();
            if (player != null)
            {
                _cape = Components.ManaCape.Attach(player, CapeColor);
                _caped = player;
            }
        }

        public override void EndPlay()
        {
            TakeOffCape();
            _session = null;
        }

        private void TakeOffCape()
        {
            if (_cape != null)
            {
                Components.ManaCape.Remove(_cape);
            }
            _cape = null;
            _caped = null;
        }
    }
}
