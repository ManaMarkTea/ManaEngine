using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using ManaVoxel.Code.Assets;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>The Particle Emitter script: loads from a level with its colours, spawns, ages, and dies.</summary>
public static class ParticleChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        var path = Path.Combine(ManaContentLibrary.Root, "Levels", "Tests", "Particles.manalevel");
        var result = ManaLevelLoader.Load(ManaLevel.Load(path), context, context.World.DefaultScene, replaceAuthoredNodes: false);
        check("the Particles level loads without a warning", result.Warnings.Count == 0, string.Join("; ", result.Warnings));

        var fountain = result.Nodes.First(n => n.Name == "Fountain").Components.OfType<ManaParticleEmitterComponent>().FirstOrDefault();
        check("the emitter script is attached", fountain != null, "");
        if (fountain == null)
        {
            return;
        }

        check("its colours load from the level", fountain.StartColor.Z > 0.9f && fountain.StartColor.X < 0.7f, $"{fountain.StartColor}");

        fountain.Step(0.5f);
        var born = fountain.AliveCount;
        check("it throws particles at its rate", born > 20 && born <= fountain.MaxParticles, $"{born}");

        for (var i = 0; i < 20; i++)
        {
            fountain.Step(0.25f);
        }
        check("they never outnumber the room made for them", fountain.AliveCount <= fountain.MaxParticles, $"{fountain.AliveCount}");

        fountain.Enabled = false;
        for (var i = 0; i < 20; i++)
        {
            fountain.Step(0.25f);
        }
        check("with emission off, they all die of old age", fountain.AliveCount == 0, $"{fountain.AliveCount}");
    }
}
