using Mana.Code.Graphics;
using Mana.Code.UI;
using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaUI;
using ManaCore.Loading;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using ManaCore.Diagnostics;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        public ManaLoadingCoordinator LoadingCoordinator { get; }
        public ManaLoadingViewModel Loading => LoadingCoordinator.Progress;
        public bool IsLoading => LoadingCoordinator.IsLoading;

        public virtual bool DeferGpuUploadsOnStartup => false;

        /// <summary>
        /// The window's titlebar/taskbar/alt-tab icon, or null for the platform default. Separate from the
        /// exe's own <c>ApplicationIcon</c> -- GLFW never reads that resource, so a window shows the bare
        /// default glyph unless something calls <c>SetWindowIcon</c> explicitly.
        /// </summary>
        protected virtual string? WindowIconPath => null;

        public long UpdateFrame = 0;
        public long RenderFrame = 0;
        public ManaGraphicsDiagnosticsProvider? GraphicsDiagnosticsProvider { get; protected set; }

        public IWindow _Window = null!;

        /// <summary>The UI tree, or null when <see cref="EnableManaUi"/> is off.</summary>
        public ManaUiHost? ManaUi { get; private set; }

        /// <summary>Opt in per game mode. Off by default so existing modes are untouched.</summary>
        protected virtual bool EnableManaUi => false;

        /// <summary>
        /// True when the UI owns the mouse, so the scene must not act on it -- no picking, no camera orbit,
        /// no voxel placement. One layer, one answer: this used to reconcile ManaUI's with ImGui's, which
        /// disagreed by design (ImGui was told there was no mouse at all while ManaUI held the pointer, so
        /// its own WantCaptureMouse read false exactly when it most needed to read true).
        /// </summary>
        public bool UiWantsMouse => ManaUi is { IsPointerOverUi: true };

        /// <summary>True when the UI wants raw keystrokes, so editor shortcuts should stand down.</summary>
        public bool UiWantsKeyboard => ManaUi is { IsKeyboardCaptured: true };

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
            Context.Audio.Dispose();
            Context.Audio = ManaAudio.ManaAudioFactory.Create(ManaLog.WriteDiagnostic);

            world.Initialize(Context, DeferGpuUploadsOnStartup ? false : initializeWorldRenderer);

            Context.Physics = new ManaPhysics(Context);

            if (EnableManaUi)
            {
                ManaUi = new ManaUiHost(window, input);
                BuildManaUi(ManaUi);
                WrapManaUiWithLoadingScreen();
            }

            Load();
        }

        /// <summary>
        /// Populate the ManaUI tree. Called once, after the host exists and before <see cref="Load"/>.
        /// </summary>
        protected virtual void BuildManaUi(ManaUiHost host)
        {
        }

        /// <summary>
        /// Puts the loading screen over whatever <see cref="BuildManaUi"/> just built.
        /// <para>
        /// Shared by both graphics backends rather than each remembering to call it: a mode that opted into
        /// ManaUI gets the loading screen as part of that, not as a second thing to wire up.
        /// </para>
        /// </summary>
        private void WrapManaUiWithLoadingScreen()
        {
            var host = ManaUi;
            if (host == null)
            {
                return;
            }

            var content = host.Root.Content ?? new Border();

            // Still parented to the root's own content layer -- Root.Content is a shorthand for that
            // layer's child, not a detached handle. Reusing it as a child of the overlay without freeing it
            // first is reparenting it onto its own descendant.
            content.RemoveFromParent();

            var screen = ManaLoadingScreenManaView.Build(host.Root.Theme, Loading);

            host.Root.Content = new ManaUiLoadingOverlay(content, screen, () => IsLoading);
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
            if (ManaTime.FixedFrameSeconds is { } fixedStep)
            {
                deltaTime = fixedStep;
            }

            // Closes the PREVIOUS tick, not this one, and does it here rather than at the end of Render for
            // a specific reason: on the WebGPU path the real GPU frame is submitted after Render returns
            // (GameSilkWebGpu.Render calls ClearFrame last), so closing in Render filed every GPU scope
            // under the following frame. deltaTime is the gap since the last tick, which is exactly the
            // duration of the frame being closed.
            ManaFrameProfiler.EndFrame(deltaTime * 1000.0);

            ManaTime.GlobalTime.UpdateTime(deltaTime);

            UpdateFrame++;

            // "Frame: " prefixed scopes are the top-level halves of a frame; the profiler subtracts them from
            // the frame's wall clock to report what is left as unaccounted (window message pump, input
            // polling, vsync wait -- none of which is ours to scope).
            using var frameScope = ManaFrameProfiler.Measure("Frame: Update");

            if (IsLoading)
            {
                LoadingCoordinator.Update();
            }
            else
            {
                using (ManaFrameProfiler.Measure("Physics"))
                {
                    Context.Physics.Update();
                }

                using (ManaFrameProfiler.Measure("Mode Update"))
                {
                    Update(deltaTime);
                }

                using (ManaFrameProfiler.Measure("World Update"))
                {
                    Context.World.Update();
                }
            }

            var camera = Context.World.MainCamera;
            if (camera != null)
            {
                Context.Audio.SetListener(camera.Owner.Position, camera.Owner.Forward, camera.Owner.Up);
            }
            Context.Audio.Update();
        }

        public abstract void Update(double deltaTime);

        private bool _loggedFirstLoadingFrame;

        /// <summary>
        /// One-shot startup-timeline marker: the earliest point in a frame where the loading screen's draw
        /// list could exist. Compared against ManaLoadingCoordinator.Begin's marker, the gap between the two
        /// is time the screen was allowed to show and no frame had reached this line yet -- window and
        /// graphics device bring-up is the leading suspect, since it runs once, synchronously, before the
        /// frame loop calls this method for the first time at all.
        /// </summary>
        private void LogFirstLoadingFrameOnce()
        {
            if (_loggedFirstLoadingFrame)
            {
                return;
            }

            _loggedFirstLoadingFrame = true;
            ManaCore.Diagnostics.ManaLog.WriteDiagnosticElapsed("First loading frame reached in BaseGameMode.Render.");
        }

        public virtual void Render(double deltaTime)
        {
            RenderFrame++;

            using var frameScope = ManaFrameProfiler.Measure("Frame: Render");

            if (IsLoading)
            {
                LogFirstLoadingFrameOnce();
                ManaGameModeGraphicsBridge.ClearLoadingFrame(Context, GraphicsBackend);

                // The loading screen is part of the ManaUI tree -- see WrapManaUiWithLoadingScreen, which
                // wrapped it in one before the first frame ever ran. A mode that opted out of ManaUI shows
                // no loading screen at all now; the ImGui window that used to stand in for one is gone.
                ManaUi?.Render(deltaTime);

                // OpenGL draws immediately and nothing else will submit this frame's ManaUI draw list, so
                // it has to happen here too. The WebGPU path submits inside its own frame regardless of
                // which branch this is, so the call is a no-op there.
                ManaGameModeGraphicsBridge.DrawManaUi(Context, GraphicsBackend, ManaUi?.Root);
                return;
            }

            // Recorded before the backend draws, so the draw list the GPU submits is this frame's.
            using (ManaFrameProfiler.Measure("ManaUI Layout + Record"))
            {
                ManaUi?.Render(deltaTime);
            }

            // Nests the backend's own finer-grained scopes (scene collect, upload, draw, present).
            using (ManaFrameProfiler.Measure("DrawWorld Total"))
            {
                ManaGameModeGraphicsBridge.DrawWorld(Context, GraphicsBackend);
            }

            ManaGameModeGraphicsBridge.DrawManaUi(Context, GraphicsBackend, ManaUi?.Root);
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
            Context.Audio.Dispose();
            ManaGameModeGraphicsBridge.DisposeManaUi();
            ManaUi?.Dispose();
            ManaUi = null;
            ManaGameModeGraphicsBridge.Dispose(Context);
        }
    }

}

