using Mana.Code.Graphics;
using Mana.Code.UI;
using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaCore.Loading;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        public ManaLoadingCoordinator LoadingCoordinator { get; }
        public ManaLoadingViewModel Loading => LoadingCoordinator.Progress;
        public bool IsLoading => LoadingCoordinator.IsLoading;

        public virtual bool DeferGpuUploadsOnStartup => false;

        public long UpdateFrame = 0;
        public long RenderFrame = 0;
        public ManaGraphicsDiagnosticsProvider? GraphicsDiagnosticsProvider { get; protected set; }

        public IManaImGuiController _ImGui = null!;
        public IWindow _Window = null!;

        /// <summary>
        /// The ManaUI tree, or null when <see cref="EnableManaUi"/> is off. Runs alongside ImGui rather than
        /// replacing it, so panels can be ported one at a time.
        /// </summary>
        public ManaUiHost? ManaUi { get; private set; }

        /// <summary>Opt in per game mode. Off by default so existing modes are untouched.</summary>
        protected virtual bool EnableManaUi => false;

        public ManaContext Context = null!;


        public ManaGraphicsBackendKind GraphicsBackend { get; }

        public BaseGameMode(ManaGraphicsBackendKind graphicsBackend = ManaGraphicsBackendKind.OpenGL)
        {
            GraphicsBackend = graphicsBackend;
            LoadingCoordinator = new ManaLoadingCoordinator(
                InitializeRendererForLoading,
                FinalizeGpuUploads,
                CanCompleteLoading,
                OnLoadingComplete);
        }

        public virtual void LoadForGraphicsBackend(IWindow window, IInputContext input, IManaGraphicsDevice graphicsDevice, bool initializeWorldRenderer = false)
        {
            var world = new ManaWorld();
            Context = new ManaContext(window, input, world, graphicsDevice);
            Context.GameMode = this;
            Context.DeferGpuUploads = DeferGpuUploadsOnStartup;

            world.Initialize(Context, DeferGpuUploadsOnStartup ? false : initializeWorldRenderer);

            Context.Physics = new ManaPhysics(Context);

            if (EnableManaUi)
            {
                ManaUi = new ManaUiHost(window);
                BuildManaUi(ManaUi);
            }

            Load();
        }

        /// <summary>
        /// Populate the ManaUI tree. Called once, after the host exists and before <see cref="Load"/>.
        /// </summary>
        protected virtual void BuildManaUi(ManaUiHost host)
        {
        }

        public virtual void Load()
        {
            LoadContent();
        }

        protected virtual void LoadContent()
        {
        }

        public void update(double deltaTime)
        {
            ManaTime.GlobalTime.UpdateTime(deltaTime);

            UpdateFrame++;

            if (IsLoading)
            {
                LoadingCoordinator.Update();
            }
            else
            {
                Context.Physics.Update();
                Update(deltaTime);
                Context.World.Update();
            }
        }

        public abstract void Update(double deltaTime);

        protected virtual void DrawMainMenuBar()
        {
        }

        protected virtual void DrawImGui(double deltaTime)
        {
        }

        protected virtual void InitializeDockLayout(uint dockspaceId, Vector2 dockspaceSize)
        {
        }

        // Bump when InitializeDockLayout changes so saved layouts on disk get discarded and rebuilt.
        protected virtual int DockLayoutVersion => 1;

        public virtual void Render(double deltaTime)
        {
            RenderFrame++;

            // Before ProcessPendingFontPreset: a scale change queues a font rebuild, which this next call
            // then performs in the same frame rather than a frame late.
            SyncImGuiUiScale();
            ProcessPendingFontPreset();

            _ImGui.Update((float)deltaTime);

            if (IsLoading)
            {
                ManaGameModeGraphicsBridge.ClearLoadingFrame(Context, GraphicsBackend);

                ManaLoadingScreenView.Draw(Loading);
                _ImGui.Render();
            }
            else
            {
                DrawMainMenuBar();
                DrawDockSpace();
                DrawImGui(deltaTime);
                Context.World.DrawImGui();

                // Recorded before the backend draws, so the draw list the GPU submits is this frame's.
                ManaUi?.Render();

                ManaGameModeGraphicsBridge.DrawWorld(Context, GraphicsBackend);
                _ImGui.Render();
            }
        }

        protected virtual void OnLoadingComplete() { }

        protected virtual bool CanCompleteLoading() => true;

        private void InitializeRendererForLoading()
        {
            ManaGameModeGraphicsBridge.InitializeRendererForLoading(Context, GraphicsBackend);
        }

        protected virtual void FinalizeGpuUploads()
        {
            ManaGameModeGraphicsBridge.FinalizeGpuUploads(Context, GraphicsBackend);
        }

        public virtual void Closing()
        {
            ManaUi?.Dispose();
            ManaUi = null;
            ManaGameModeGraphicsBridge.Dispose(Context, _ImGui);
        }
    }

}

