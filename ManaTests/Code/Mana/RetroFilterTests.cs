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

        filter.Enabled = true;
        world.FindPostEffects(null, out var screenScoped);
        Check("a Screen script covers the whole screen", world.FindRetroFilter() is { Scoped: false } && screenScoped == 0);

        filter.Scope = ManaPostEffectScope.Children;
        var sceneBits = new Dictionary<ManaScene, byte>();
        var scopedLook = world.FindPostEffects(sceneBits, out var childrenScoped);
        Check("a Children script scopes its slot, and no scene as a whole",
            scopedLook is { Scoped: true } && ManaPostEffects.Has(childrenScoped, ManaPostEffects.Retro) && sceneBits.Count == 0);
        Check("its bits are found on its own node only for its own scope",
            ManaPostEffectComponent.Bits(holder, ManaPostEffectScope.Children) == ManaPostEffects.Bit(ManaPostEffects.Retro)
                && ManaPostEffectComponent.Bits(holder, ManaPostEffectScope.Siblings) == 0);

        filter.Scope = ManaPostEffectScope.Scene;
        world.FindPostEffects(sceneBits, out _);
        Check("a Scene script marks its scene", ManaPostEffects.Has(sceneBits.GetValueOrDefault(scene), ManaPostEffects.Retro));

        // A second, Screen-scoped script anywhere makes the whole screen go through.
        var second = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Second" };
        second.Components.Add(new ManaRetroFilterComponent(second));
        scene.Children.Add(second);
        Check("any Screen script unscopes it", world.FindRetroFilter() is { Scoped: false });
        scene.Children.Remove(second);
        filter.Scope = ManaPostEffectScope.Screen;

        world.RetroFilterOverride = ManaRetroFilterSettings.Preset("vhs");
        Check("the script command's look wins over a script", world.RetroFilterOverride is { Glitch: > 0.5f }
            && ManaRetroFilterSettings.PresetNames.All(name => ManaRetroFilterSettings.Preset(name) != null)
            && ManaRetroFilterSettings.Preset("sepia") == null);

        Check("a portrait window size reads as width by height",
            ManaLaunchOptions.ParseWindowSize("1080x1920") == (1080, 1920) && ManaLaunchOptions.ParseWindowSize("1080") == null
                && ManaLaunchOptions.ParseWindowSize("0x0") == null);

        var settings = new ManaRetroFilterSettings { Lines = 240 };
        Check("big pixels are whole screen pixels",
            settings.PixelSize(1080) == 5 && settings.PixelSize(2160) == 9 && settings.PixelSize(100) == 1,
            $"{settings.PixelSize(1080)} {settings.PixelSize(2160)} {settings.PixelSize(100)}");
    }
}
