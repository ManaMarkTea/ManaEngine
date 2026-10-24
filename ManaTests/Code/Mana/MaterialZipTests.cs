using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

public static class MaterialZipChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, string.Empty);
        Check("license is not an image", ManaMaterialLoaderZip.ChannelOf("LICENSE.txt") == null);
        Check("specular is not metallic", ManaMaterialLoaderZip.ChannelOf("concrete_specular.jpg") == null);
        Check("DirectX normals are excluded", ManaMaterialLoaderZip.ChannelOf("wood_normal_dx.png") == null);
        Check("mixed case nested maps recognized", ManaMaterialLoaderZip.ChannelOf("wood/Wood_BaseColor.PNG") == "Albedo");
        Check("ambient occlusion recognized", ManaMaterialLoaderZip.ChannelOf("wood_ambientOcclusion.jpeg") == "Occlusion");
        Check("height is ignored", ManaMaterialLoaderZip.ChannelOf("wood_height.png") == null);
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        context.Physics = new ManaPhysics(context);
        var folder = Path.Combine(Path.GetTempPath(), "ManaMaterialTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "test.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "LICENSE.txt", "maps/wood_BaseColor.PNG", "maps/wood_Normal.png", "wood_height.png", "wood_specular.png" })
                {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write("not decoded until rendered");
                }
            }
            var material = ManaMaterialLoaderZip.LoadFromZip(context, path);
            Check("ZIP ignores text and loads lazily", material.Albedo.TextureValue?.OriginalFilename == "zip:test.zip/maps/wood_BaseColor.PNG");
            Check("normal map enabled", material.Normal.TextureValue != null && material.NormalSelector == 0);
            Check("specular does not make concrete metal", material.Metallic.TextureValue == null && material.Metallic.Value == 0);
            material.InsetDepth = 0.075f;
            var snapshot = ManaLevelMaterial.Capture(material, context, Vector4.One);
            Check("ZIP selection captured", snapshot?.SourceZip == path && snapshot.NormalMap != null);
            var json = JsonSerializer.Serialize(snapshot);
            var restored = JsonSerializer.Deserialize<ManaLevelMaterial>(json);
            var loaded = new ManaMaterial(context);
            loaded.Roughness.TextureValue = new ManaTexture(context) { OriginalFilename = "old.png" };
            loaded.Metallic.Value = 1.0f;
            loaded.Albedo.Value = new Vector4(0.2f, 0.3f, 0.4f, 1.0f);
            restored?.ApplyTo(loaded);
            Check("inset depth survives JSON round trip", loaded.InsetDepth == 0.075f);
            Check("maps survive JSON round trip", loaded.Albedo.TextureValue?.OriginalFilename == material.Albedo.TextureValue?.OriginalFilename
                && loaded.Normal.TextureValue?.OriginalFilename == material.Normal.TextureValue?.OriginalFilename);
            Check("missing ZIP channels clear imported maps", loaded.Roughness.TextureValue == null);
            Check("ZIP snapshots replace imported scalar defaults", loaded.Metallic.Value == 0.0f && loaded.Albedo.Value == Vector4.One);
            Check("derived picture list is not saved", !json.Contains("Pictures", StringComparison.Ordinal));
            var invalid = Path.Combine(folder, "invalid.zip");
            using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
            {
                archive.CreateEntry("license.txt");
            }
            var rejected = false;
            try
            {
                ManaMaterialLoaderZip.ApplyTo(loaded, invalid);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Check("invalid ZIP leaves the current material intact", rejected && loaded.Albedo.TextureValue?.OriginalFilename == material.Albedo.TextureValue?.OriginalFilename);
            foreach (var pack in new[] { "dark-wood-parquet", "black-brick-wall", "concrete" })
            {
                var bundled = ManaMaterialLoaderZip.LoadFromZip(context, $"Assets/Materials/{pack}.zip");
                var texture = bundled.Albedo.TextureValue;
                texture?.LoadCpuPixelData();
                Check($"{pack} has a portable 512px albedo", texture?.Width == 512 && texture.Height == 512 && texture.PixelData.Length == 512 * 512 * 4);
            }
            var scene = new ManaScene(context) { Name = "Imported" };
            world.Scenes.Add(scene);
            var modelLevel = new ManaLevel
            {
                Nodes = [new ManaLevelNode { Name = "Model", Kind = Mana.Code.Levels.ManaLevelSourceKind.Mesh, Asset = "Assets/Models/Donut/Donut.glb" }]
            };
            var result = ManaLevelLoader.Load(modelLevel, context, scene);
            check("mesh fixture loads", result.Nodes.Count == 1 && result.Warnings.Count == 0, string.Join("; ", result.Warnings));
            if (result.Nodes.FirstOrDefault() is { } model)
            {
                Check("untouched imports do not save material overrides", ManaLevelCapture.Capture(world).Nodes.Single().MeshMaterials == null);
                var renderer = ManaLevelMaterial.MeshRenderers(model).First();
                ManaMaterialLoaderZip.ApplyTo(renderer.DynamicMaterial, "Assets/Materials/concrete.zip");
                var saved = ManaLevelCapture.Capture(world);
                Check("selected mesh ZIP is captured", saved.Nodes.Single().MeshMaterials?.Count == 1);
                var otherWorld = new ManaWorld();
                var otherContext = new ManaContext(null!, null!, otherWorld) { DeferGpuUploads = true };
                otherWorld.Context = otherContext;
                otherContext.Physics = new ManaPhysics(otherContext);
                var otherScene = new ManaScene(otherContext);
                otherWorld.Scenes.Add(otherScene);
                var reloaded = ManaLevelLoader.Load(ManaLevel.FromJson(saved.ToJson()), otherContext, otherScene);
                var restoredRenderer = ManaLevelMaterial.MeshRenderers(reloaded.Nodes.Single()).First();
                Check("mesh ZIP survives a level reload", restoredRenderer.Material.Albedo.TextureValue?.OriginalFilename == "zip:concrete.zip/material_albedo.png"
                    && restoredRenderer.Material.SourceZip == "Assets/Materials/concrete.zip");
                Check("mesh material stays stable after reopening", saved.ToJson() == ManaLevelCapture.Capture(otherWorld).ToJson());
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
