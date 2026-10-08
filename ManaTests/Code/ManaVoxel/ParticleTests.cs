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

        check("the GPU demo appears in the level picker", ManaLevelLibrary.List().Any(path => ManaLevelLibrary.DisplayName(path) == "GPU Particles"), "");
        var gpuLevel = ManaLevelLoader.Load(ManaLevel.Load(Path.Combine(ManaContentLibrary.Root, "Levels", "GPU Particles.manalevel")),
            context, context.World.DefaultScene, replaceAuthoredNodes: false);
        check("the GPU stress level loads without warnings", gpuLevel.Warnings.Count == 0, string.Join("; ", gpuLevel.Warnings));
        var gpuNode = gpuLevel.Nodes.First(n => n.Name == "GPU Fountain");
        var gpu = gpuNode.Components.OfType<ManaGpuParticleEmitterComponent>().Single();
        gpu.Count = 1_000_000;
        var packets = new List<ManaCore.Graphics.ManaGpuParticleEmitter>();
        gpuNode.CollectGpuParticles(packets);
        var gpuMeshes = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
        gpuNode.CollectRenderMeshInstances(gpuMeshes);
        check("a million GPU particles collect as one emitter and no CPU meshes", packets.Count == 1 && packets[0].Count == 1_000_000 && gpuMeshes.Count == 0, "");
        gpu.Step(0.5f);
        check("GPU phase advances without particle arrays", Math.Abs(gpu.Describe().Seconds - 0.5f) < 0.001f, "");
        gpu.Enabled = false;
        gpu.Step(0.5f);
        packets.Clear(); gpuNode.CollectGpuParticles(packets);
        check("disabled GPU emitters disappear and pause their phase", packets.Count == 0 && Math.Abs(gpu.Describe().Seconds - 0.5f) < 0.001f, "");
        gpu.Enabled = true;
        gpu.Count = int.MaxValue;
        gpu.Lifetime = float.NaN;
        gpu.Step(float.NaN);
        check("GPU settings bound unsafe counts and lifetime", gpu.Describe().Count == ManaGpuParticleEmitterComponent.MaximumCount && float.IsFinite(gpu.Describe().Seconds), "");
        check("GPU bounce is optional by default", gpu.Describe().BounceSettings.X == 0 && gpu.Describe().BounceSettings.Y == 0, "");
        gpu.BouncePlane1Enabled = true; gpu.BouncePlane1Position = new(0, 2, 0);
        gpu.BouncePlane1Normal = new(0, 4, 0); gpu.Bounciness = 2;
        check("GPU planes normalize world normals and clamp restitution", gpu.Describe().BouncePlane1 == new Vector4(0, 1, 0, -2)
            && gpu.Describe().BounceSettings.Z == 1, "");
        gpu.Count = 250_000; gpu.Lifetime = 4; gpu.WindInfluence = 0.25f;
        var capturedGpu = ManaLevelCapture.Capture(context.World, "GPU Roundtrip");
        var savedGpu = capturedGpu.Nodes.First(n => n.Name == "GPU Fountain").Scripts!.Single(s => s.Type == "GpuParticleEmitter");
        check("GPU emitter settings survive level capture", savedGpu.Properties["count"].GetInt32() == 250_000 && !savedGpu.Properties.ContainsKey("seconds"), "");
        var reloadedGpu = ManaLevelLoader.Load(capturedGpu, context, context.World.DefaultScene, replaceAuthoredNodes: false)
            .Nodes.First(n => n.Name == "GPU Fountain").Components.OfType<ManaGpuParticleEmitterComponent>().Single();
        check("GPU settings reload with their count, lifetime and colors", reloadedGpu.Count == 250_000 && reloadedGpu.Lifetime == 4
            && reloadedGpu.StartColor == gpu.StartColor && reloadedGpu.Describe().Seconds == 0, "");

        check("GPU bounce settings survive level save and reload", reloadedGpu.BouncePlane1Enabled
            && reloadedGpu.BouncePlane1Position == new Vector3(0, 2, 0) && reloadedGpu.Bounciness == 2, "");
        var bounceLevel = ManaLevelLoader.Load(ManaLevel.Load(Path.Combine(ManaContentLibrary.Root, "Levels", "GPU Particle Bounce.manalevel")),
            context, context.World.DefaultScene, replaceAuthoredNodes: false);
        check("the two-plane GPU bounce demo loads without warnings", bounceLevel.Warnings.Count == 0, string.Join("; ", bounceLevel.Warnings));

        check("GPU wind influence survives save and reload", reloadedGpu.WindInfluence == 0.25f && reloadedGpu.Describe().WindInfluence == 0.25f, "");
        reloadedGpu.WindInfluence = float.NaN;
        check("GPU wind influence sanitizes invalid settings", reloadedGpu.Describe().WindInfluence == 1, "");

        var movingNode = new Mana.Code.Nodes.ManaSceneNode(context, Matrix4x4.Identity);
        var movingGpu = new ManaGpuParticleEmitterComponent(movingNode) { Lifetime = 1, Count = 100 };
        movingGpu.Step(0.5f);
        var beforeMove = movingGpu.Describe();
        var originalBirth = beforeMove.SpawnTransforms.Span[0];
        var movedPose = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(10, 2, 3);
        movingNode.WorldTransform = movedPose;
        movingGpu.Step(0.15f);
        var afterMove = movingGpu.Describe();
        check("moving and rotating an emitter preserves old birth poses", afterMove.SpawnTransforms.Span[0] == originalBirth, "");
        check("new birth poses track emitter movement", afterMove.SpawnTransforms.Span[(int)afterMove.SpawnHead].Translation.X > 9, "");
        check("rotating the emitter does not rotate gravity", afterMove.Gravity == new Vector3(0, -3, 0), "");
        movingGpu.Step(0.45f);
        var wrapped = movingGpu.Describe();
        var newBirthIndex = (int)((wrapped.SpawnHead + 256 - wrapped.SpawnTickWithinLifetime) % 256);
        check("new lifetimes use the moved pose across ring wrap", wrapped.SpawnTransforms.Span[newBirthIndex] == movedPose, "");
        movingGpu.Step(2);
        var stationaryRevision = movingGpu.Describe().SpawnRevision;
        movingGpu.Step(0.1f);
        check("stationary emitters stop uploading history once old poses expire", movingGpu.Describe().SpawnRevision == stationaryRevision, "");
        check("spawn history size is independent of particle count", movingGpu.Describe().SpawnTransforms.Length == ManaGpuParticleEmitterComponent.SpawnHistoryLength, "");
    }
}
