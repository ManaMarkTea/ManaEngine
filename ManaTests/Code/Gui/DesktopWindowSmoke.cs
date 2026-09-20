using Mana.Code.UI.ManaUi;
using ManaGraphics.Code.Graphics;
using ManaUI;
using Silk.NET.Maths;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;

namespace ManaTests;

/// <summary>Opt-in native/GPU lifecycle check: dotnet run --project ManaTests -- --popout-smoke.</summary>
public static class DesktopWindowSmoke
{
    public static int Run()
    {
        var options = WindowOptions.Default;
        options.API = new GraphicsAPI(ContextAPI.None, new APIVersion(0, 0));
        options.Title = "ManaUI pop-out smoke test";
        options.Size = new Vector2D<int>(800, 600);
        options.ShouldSwapAutomatically = false;
        options.IsVisible = false;
        using var main = Window.Create(options);
        main.Initialize();
        using var input = main.CreateInput();
        using var gpu = new ManaWebGpuContext(main, input, null);
        using var host = new ManaUiHost(main, input);
        IWindow? child = null;
        CountingRenderer? renderer = null;
        host.EnableDesktopWindows(window =>
        {
            child = window;
            return renderer = new CountingRenderer(gpu.CreateUiSurface(window));
        }, Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico"));
        var dock = new DockHost();
        host.Root.Content = dock;
        var controller = new DockDragController(host.Root, dock);
        var field = new TextBox { Text = "This edit must survive docking back." };
        var content = new StackPanel();
        content.Children.Add(new TextBlock("A real desktop ManaUI window"));
        content.Children.Add(field);
        var item = new DockItem("Pop-out smoke", content) { Id = "smoke" };
        dock.Dock(item, DockSide.Center);
        gpu.UiRoot = host.Root;

        void Frame()
        {
            main.DoEvents();
            main.DoUpdate();
            host.Render(1.0 / 60);
            gpu.ClearFrame(new Silk.NET.WebGPU.Color { R = 0.05, G = 0.06, B = 0.07, A = 1 });
        }
        void Require(bool ok, string description)
        {
            if (!ok) throw new InvalidOperationException(description);
            Console.WriteLine("PASS: " + description);
        }

        try
        {
            Frame();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var dockedTab = item.Owner!;
                var tabPoint = dockedTab.TabRect(0).Center;
                DoubleClick(host.Root, tabPoint, 10 + cycle);
                var external = controller.DesktopWindows.Single();
                for (var i = 0; i < 12; i++) Frame();
                Require(child is { IsInitialized: true } && controller.DesktopWindows.Count == 1,
                    "native window renders on shared device");
                Require(field.Root != host.Root && field.Root != null, "content belongs to the external root");
                Require(field.Root!.Content is DesktopWindowFrame { Dock.RootNode: DockTabGroup group } && group.SelectedItem == item,
                    "pop-out retains the original tab");
                if (OperatingSystem.IsWindows() && child!.Native?.Win32 is { } win32)
                {
                    Require(SendMessage(win32.Hwnd, 0x007F, 1, 0) != 0,
                        "native taskbar/titlebar icon is set");
                }
                var framesBeforeResize = renderer!.Frames;
                external.Size = new Vector2(560, 420);
                main.DoEvents();
                Require(renderer.Frames > framesBeforeResize,
                    "resize repaints without an editor render tick");
                child!.WindowState = WindowState.Minimized;
                Frame();
                child.WindowState = WindowState.Normal;
                for (var i = 0; i < 4; i++) Frame();
                if (cycle == 1)
                {
                    var externalRoot = field.Root!;
                    var tab = ((DockTabGroup)((DesktopWindowFrame)externalRoot.Content!).Dock.RootNode!).TabRect(0);
                    DoubleClick(externalRoot, tab.Center, 1);
                }
                else
                {
                    // The native close button follows the same path as IWindow.Close.
                    child.Close();
                }
                Frame();
                Frame();
                Require(controller.DesktopWindows.Count == 0 && field.Root == host.Root,
                    "close/double-click returns content and releases the window");
                Require(field.Text == "This edit must survive docking back.", "panel state survives round trip");
            }
            controller.PopOut(item);
            controller.PopOut(new DockItem("Second pop-out", new TextBlock("Independent second window")));
            Frame();
            Require(controller.DesktopWindows.Count == 2, "two native panels can render concurrently");
            // Exercise the actual shutdown order: the renderer is released before the host's windows.
            gpu.Dispose();
            host.Dispose();
            Console.WriteLine("PASS: shutdown with a panel still popped out");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static void DoubleClick(UiRoot root, Vector2 point, double time)
    {
        root.Input.InjectPointerPressed(point, PointerButton.Left, time);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
        root.Input.InjectPointerPressed(point, PointerButton.Left, time + 0.1);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
    }

    private sealed class CountingRenderer(IUiWindowRenderer inner) : IUiWindowRenderer
    {
        public int Frames { get; private set; }
        public void Render(UiRoot root) { inner.Render(root); Frames++; }
        public void Dispose() => inner.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
