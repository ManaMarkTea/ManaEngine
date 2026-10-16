using Mana.Code.Graphics;
using ManaCore.Graphics;
using Silk.NET.Windowing;

namespace Mana.Code
{
    public class ManaContext
    {
        /// <summary>Silent in headless contexts; the game mode installs the platform device at startup.</summary>
        public ManaCore.Audio.IManaAudio Audio { get; set; } = ManaAudio.ManaAudioFactory.CreateSilent();
        public IManaGraphicsDevice? GraphicsDevice { get; private set; }
        public ManaGraphicsBackendKind GraphicsBackend => GraphicsDevice?.Backend ?? ManaGraphicsBackendKind.None;
        public IInputContext Input { get; set; } = null!;
        public IWindow Window { get; set; } = null!;

        public ManaWorld World { get; private set; } = null!;

        public bool DeferGpuUploads { get; set; }

        public ManaPhysics Physics { get; set; } = null!;

        public BaseGameMode GameMode { get; set; } = null!;

        public ManaContext(BaseGameMode gameMode)
        {
            GameMode = gameMode;
        }

        public ManaContext(IWindow window, IInputContext input, ManaWorld world, IManaGraphicsDevice? graphicsDevice = null)
        {
            Window = window;
            Input = input;
            World = world;
            GraphicsDevice = graphicsDevice;
        }

    }
}
