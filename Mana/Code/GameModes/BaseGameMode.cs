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

        /// <summary>
        /// True when any UI layer owns the mouse, so the scene must not act on it -- no picking, no camera
        /// orbit, no voxel placement.
        /// <para>
        /// There are two layers now, and asking only ImGui is worse than useless: while ManaUI owns the
        /// pointer, ImGui is deliberately told there is no mouse at all, so its WantCaptureMouse reads
        /// false at exactly the moments it most needs to read true. Every gate has to ask this rather than
        /// ImGui directly, or clicking a ManaUI panel also picks in the scene behind it -- which then
        /// destroys the panel that was mid-drag.
        /// </para>
        /// </summary>
        public bool UiWantsMouse => ManaUi is { IsPointerOverUi: true } || ImGuiWantsMouse();

        /// <summary>True when any UI layer wants raw keystrokes, so editor shortcuts should stand down.</summary>
        public bool UiWantsKeyboard => ManaUi is { IsKeyboardCaptured: true } || ImGuiWantsKeyboard();

        private static bool ImGuiWantsMouse()
        {
#if MANA_BROWSER
            return false;
#else
            return HasImGuiContext() && ImGuiNET.ImGui.GetIO().WantCaptureMouse;
#endif
        }

        private static bool ImGuiWantsKeyboard()
        {
#if MANA_BROWSER
            return false;
#else
            return HasImGuiContext() && ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;
#endif
        }

        /// <summary>Guards the headless case, where there is no ImGui context to ask.</summary>
        private static bool HasImGuiContext()
        {
#if MANA_BROWSER
            // No real ImGui context ever exists in-browser (NullManaImGuiController never
            // creates one) -- see ManaGraphics.csproj.
            return false;
#else
            try
            {
                return ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
#endif
        }

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

#if !MANA_BROWSER
            // Before ProcessPendingFontPreset: a scale change queues a font rebuild, which this next call
            // then performs in the same frame rather than a frame late.
            SyncImGuiUiScale();
            ProcessPendingFontPreset();
#endif

            AttachManaUiToImGui();

            using (ManaFrameProfiler.Measure("ImGui Update"))
            {
                _ImGui.Update((float)deltaTime);
            }

            if (IsLoading)
            {
                LogFirstLoadingFrameOnce();
                ManaGameModeGraphicsBridge.ClearLoadingFrame(Context, GraphicsBackend);

                // A mode with ManaUI enabled shows the loading screen through it -- see
                // WrapManaUiWithLoadingScreen, which wrapped the tree in one before the first frame ever
                // ran. A mode without it falls back to the ImGui window this always used to be.
                if (ManaUi != null)
                {
                    ManaUi.Render(deltaTime);
                }
#if !MANA_BROWSER
                else
                {
                    ManaLoadingScreenView.Draw(Loading);
                }
#endif

                _ImGui.Render();

                // OpenGL draws immediately and nothing else will submit this frame's ManaUI draw list, so
                // it has to happen here too. The WebGPU path submits inside its own frame regardless of
                // which branch this is, so the call is a no-op there.
                ManaGameModeGraphicsBridge.DrawManaUi(Context, GraphicsBackend, ManaUi?.Root);
            }
            else
            {
                // A mode with ManaUI enabled has ported every panel it actually shows; ImGui's own menu
                // bar, dockspace and panels stand down entirely rather than sitting underneath ManaUI
                // unseen but still live -- an undocked ImGui window still claims the mouse whenever it
                // ends up under the cursor, and the two dock layouts have no idea where the other one's
                // columns are. _ImGui itself stays running (Update/Render still happen below) so nothing
                // else in this method has to know ImGui is idle; it simply has no windows open to draw.
                if (!EnableManaUi)
                {
                    using (ManaFrameProfiler.Measure("ImGui Panels"))
                    {
                        DrawMainMenuBar();
#if !MANA_BROWSER
                        DrawDockSpace();
#endif
                        DrawImGui(deltaTime);
#if !MANA_BROWSER
                        Context.World.DrawImGui();
#endif
                    }
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

                using (ManaFrameProfiler.Measure("ImGui Render"))
                {
                    _ImGui.Render();
                }

                // OpenGL draws immediately, so ManaUI goes here to land on top of ImGui. The WebGPU path
                // submits inside its own frame instead, and this is a no-op there.
                ManaGameModeGraphicsBridge.DrawManaUi(Context, GraphicsBackend, ManaUi?.Root);
            }
        }

        /// <summary>
        /// Tells ImGui to stand down while ManaUI owns the pointer or the keyboard.
        /// <para>
        /// ManaUI draws after ImGui and so sits on top of it, but the two read input independently -- ImGui
        /// polls the mouse rather than consuming events, so there is nothing ManaUI can mark as handled.
        /// Without this every click lands in both, and pressing a ManaUI button also presses whatever ImGui
        /// happened to have underneath it.
        /// </para>
        /// <para>
        /// Wired here rather than where the controller is built, because each backend constructs its own and
        /// this has to hold for all of them. The predicates are read per event and per frame, so they always
        /// reflect ManaUI's live state rather than a snapshot.
        /// </para>
        /// </summary>
        private void AttachManaUiToImGui()
        {
            if (_manaUiAttachedToImGui || ManaUi == null)
            {
                return;
            }

            _manaUiAttachedToImGui = true;

            var host = ManaUi;

            // IsPointerOverUi is true while anything holds pointer capture as well as on plain hover, so a
            // drag that starts on a ManaUI slider and wanders off it keeps ImGui out until the button
            // comes up.
            _ImGui.IsPointerOwnedByOverlay = () => host.IsPointerOverUi;
            _ImGui.IsKeyboardOwnedByOverlay = () => host.IsKeyboardCaptured;
        }

        private bool _manaUiAttachedToImGui;

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
            ManaGameModeGraphicsBridge.DisposeManaUi();
            ManaUi?.Dispose();
            ManaUi = null;
            ManaGameModeGraphicsBridge.Dispose(Context, _ImGui);
        }
    }

}

