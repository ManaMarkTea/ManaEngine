using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>The RetroFilter script: the first enabled one anywhere turns the screen filter on, with its settings clamped.</summary>
public static class RetroFilterChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var scene = new ManaScene(context) { Name = "Retro" };
        world.Scenes.Add(scene);
        Check("no script, no filter", world.FindRetroFilter() == null);

        var group = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Group" };
        var holder = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Screen" };
        var filter = new ManaRetroFilterComponent(holder) { Lines = 180, ColorLevels = 1, Dither = 3.0f };
        holder.Components.Add(filter);
        group.AddChild(holder);
        scene.Children.Add(group);

        var found = world.FindRetroFilter();
        Check("a script deep in the tree is found", found != null && found.Lines == 180, $"{found}");
        Check("its settings are clamped for the shader", found is { ColorLevels: 2, Dither: 1.0f }, $"{found}");

        filter.Enabled = false;
        Check("a disabled script turns it off", world.FindRetroFilter() == null);

        var settings = new ManaRetroFilterSettings { Lines = 240 };
        Check("big pixels are whole screen pixels",
            settings.PixelSize(1080) == 5 && settings.PixelSize(2160) == 9 && settings.PixelSize(100) == 1,
            $"{settings.PixelSize(1080)} {settings.PixelSize(2160)} {settings.PixelSize(100)}");
    }
}
