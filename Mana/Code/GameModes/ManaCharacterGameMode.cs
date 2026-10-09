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
    }
}
