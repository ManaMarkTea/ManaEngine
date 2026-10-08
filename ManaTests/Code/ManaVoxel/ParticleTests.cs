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

        // The fire presets: additive teardrops with a three-colour gradient and turbulence, and a flat cel-shaded one.
        var garden = ManaLevelLoader.Load(ManaLevel.Load(Path.Combine(ManaContentLibrary.Root, "Levels", "Particle Garden.manalevel")),
            context, context.World.DefaultScene, replaceAuthoredNodes: false);
        check("the Particle Garden loads without a warning", garden.Warnings.Count == 0, string.Join("; ", garden.Warnings));
        var realistic = garden.Nodes.First(n => n.Name == "Realistic Fire Flames");
        var flames = realistic.Components.OfType<ManaParticleEmitterComponent>().First();
        check("the realistic fire is additive flames with a mid colour and turbulence",
            flames.BlendMode == ManaParticleBlend.Additive && flames.Shape == ManaParticleShape.Flames && flames.UseMidColor && flames.Turbulence > 0.0f, "");
        var stylized = garden.Nodes.First(n => n.Name == "Stylized Fire Flames").Components.OfType<ManaParticleEmitterComponent>().First();
        check("the stylized fire is hard-edged and banded", stylized.HardEdge && stylized.ColorSteps >= 2 && stylized.BlendMode == ManaParticleBlend.Alpha, "");
        for (var i = 0; i < 40; i++)
        {
            flames.Step(0.05f);
        }
        check("a fire with drag and turbulence keeps its particles alive and bounded", flames.AliveCount > 10 && flames.AliveCount <= flames.MaxParticles, $"{flames.AliveCount}");

        var lamp = realistic.Components.OfType<ManaLightSourceComponent>().First();
        var factors = Enumerable.Range(0, 60).Select(i => lamp.FlickerFactor(i * 0.05)).ToList();
        check("a flickering light wavers but never goes above its full brightness",
            factors.Max() <= 1.0f && factors.Min() < 0.9f && factors.Min() >= 1.0f - lamp.FlickerAmount - 0.001f, $"{factors.Min():0.00}..{factors.Max():0.00}");
        check("and a steady one is always 1", new ManaLightSourceComponent(context).FlickerFactor(3.0) == 1.0f, "");

        fountain.Enabled = false;
        for (var i = 0; i < 20; i++)
        {
            fountain.Step(0.25f);
        }
        check("with emission off, they all die of old age", fountain.AliveCount == 0, $"{fountain.AliveCount}");
    }
}
