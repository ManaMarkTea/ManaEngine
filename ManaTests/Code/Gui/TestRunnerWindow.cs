using Mana.Code.UI.ManaUi;
using ManaGraphics.Code.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Hosts the test runner in a real window, rendered by ManaUI on the WebGPU backend.
/// <para>
/// No scene, no ImGui -- just a window, the graphics context and a UI tree. That it takes this little
/// setup is itself a useful check on the framework's coupling.
/// </para>
/// </summary>
public static class TestRunnerWindow
{
    public static int Show(ManaFontManager fonts, string? systemFilter)
    {
        var vm = new TestRunnerViewModel(fonts, systemFilter);

        // Run once before the window opens, so it comes up showing results rather than an empty list.
        vm.RunTests();

        ManaUiHost? host = null;

        try
        {
            new GameSilkWebGpu(
                title: "ManaTests",
                requestedBackend: null,
                renderSceneProvider: null,
                inputReady: (window, input) =>
                {
                    host = new ManaUiHost(window, input);

                    // Opaque, unlike the in-editor host: this is a standalone tool window with nothing
                    // behind it to show through.
                    host.Root.Background = host.Root.Theme.Background;
                    host.Root.Content = TestRunnerView.Build(host.Root.Theme, vm);
                },
                beforeRender: deltaTime => host?.Render(deltaTime),
                webGpuReady: (context, _) =>
                {
                    if (host != null)
                    {
                        context.UiRoot = host.Root;
                    }
                }).Run();
        }
        catch (Exception ex)
        {
            // The GUI is a convenience; if the graphics stack will not start, say so and fall back to
            // reporting the result that was already computed.
            Console.WriteLine($"Could not open the GUI runner: {ex.Message}");
            Console.WriteLine(vm.Summary);
            return vm.AllPassed ? 0 : 1;
        }
        finally
        {
            host?.Dispose();
        }

        Console.WriteLine(vm.Summary);
        return vm.AllPassed ? 0 : 1;
    }
}
