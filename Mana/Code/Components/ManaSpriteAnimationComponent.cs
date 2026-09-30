using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Plays the frames of its node's Sprite sheet: <see cref="FrameCount"/> of them from <see cref="FirstFrame"/>,
    /// at <see cref="FramesPerSecond"/>, looping or holding the last. Runs in the editor too, so a sheet can be
    /// checked where it is placed.
    /// </summary>
    public sealed class ManaSpriteAnimationComponent : ManaComponent
    {
        public float FramesPerSecond { get; set; } = 8.0f;
        public int FirstFrame { get; set; }

        /// <summary>How many frames to play; 0 plays every frame from the first to the end of the sheet.</summary>
        public int FrameCount { get; set; }

        public bool Loop { get; set; } = true;
        public bool Playing { get; set; } = true;

        private double _seconds;

        public ManaSpriteAnimationComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite Animation";
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner.GetComponent<ManaSpriteComponent>() is not { } sprite)
            {
                return;
            }

            if (Playing)
            {
                _seconds += ManaTime.GlobalTime.DeltaTime.TotalSeconds;
            }

            sprite.PlayingFrame = FrameAt(_seconds, sprite.FrameCount);
        }

        /// <summary>The sheet frame shown <paramref name="seconds"/> into the animation.</summary>
        public int FrameAt(double seconds, int sheetFrames)
        {
            var first = Math.Clamp(FirstFrame, 0, Math.Max(0, sheetFrames - 1));
            var count = FrameCount > 0 ? Math.Min(FrameCount, sheetFrames - first) : sheetFrames - first;
            if (count <= 1)
            {
                return first;
            }

            var step = (int)Math.Floor(seconds * Math.Max(FramesPerSecond, 0.0f));
            return first + (Loop ? step % count : Math.Min(step, count - 1));
        }
    }
}
