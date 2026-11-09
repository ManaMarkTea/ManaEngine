using System.Text.Json.Nodes;
using ManaCore.Imaging;
using ManaUI;
using ManaEditor.Code.Imports;

namespace ManaTests;

/// <summary>The scene importer's own steps, each on a small made-up case: none runs FBX2glTF or a real pack.</summary>
public static class SceneImportChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var folder = Directory.CreateTempSubdirectory("mana-import-");
        try
        {
            Pivots(folder.FullName, check);
            Glows(folder.FullName, check);
            Bc5Flip(check);
            Hdr(check);
            Manifest(folder.FullName, check);
            SceneFileMaterials(folder.FullName, check);
            check("pack names read as words", ManaSceneImporter.Words("BistroExterior") == "Bistro Exterior"
                && ManaSceneImporter.Words("Bistro_Interior_Wine") == "Bistro Interior Wine", ManaSceneImporter.Words("BistroExterior"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // A mesh 10 m from its node's origin, used by a node turned a quarter about Y, with a child below it.
    private static void Pivots(string folder, Action<string, bool, string> check)
    {
        var gltf = Triangles(folder, "pivots", [new(10, 0, 0), new(11, 0, 0), new(10, 1, 0)], [0, 1, 2],
            "\"nodes\":[{\"mesh\":0,\"rotation\":[0,0.7071068,0,0.7071068],\"translation\":[1,2,3],\"children\":[1]},{\"translation\":[0,5,0]}]");
        var before = WorldBox(gltf);
        var childBefore = gltf.WorldMatrices()[1].Translation;
        var moved = ManaImportScene.RecentrePivots(gltf);
        var after = WorldBox(gltf);
        check("a mesh far from its origin is re-pivoted", moved == 1, $"{moved}");
        check("and stands exactly where it did", Vector3.Distance(before.Min, after.Min) < 1e-4f && Vector3.Distance(before.Max, after.Max) < 1e-4f,
            $"{before} -> {after}");
        check("its pivot now sits inside its geometry",
            Vector3.Distance(gltf.WorldMatrices()[0].Translation, (after.Min + after.Max) * 0.5f) < 1e-3f, gltf.WorldMatrices()[0].Translation.ToString());
        check("and its children do not move", Vector3.Distance(childBefore, gltf.WorldMatrices()[1].Translation) < 1e-4f,
            $"{childBefore} -> {gltf.WorldMatrices()[1].Translation}");
        var positions = gltf.ReadVec3(0);
        var min = ManaGltfDocument.Vector(gltf.Accessors[0]!["min"], Vector3.Zero);
        check("accessor bounds match the stored floats exactly", positions.Min(p => p.X) == min.X && positions.Min(p => p.Y) == min.Y, "");
    }

    // Two bulbs of one emissive mesh, far apart, and a third touching the first: two lights.
    private static void Glows(string folder, Action<string, bool, string> check)
    {
        Vector3[] points = [new(0, 0, 0), new(0.1f, 0, 0), new(0, 0.1f, 0), new(5, 0, 0), new(5.1f, 0, 0), new(5, 0.1f, 0), new(0.2f, 0, 0), new(0.3f, 0, 0), new(0.2f, 0.1f, 0)];
        var gltf = Triangles(folder, "glows", points, [0, 1, 2, 3, 4, 5, 6, 7, 8], "\"nodes\":[{\"mesh\":0}]", material: true);
        var glows = ManaImportScene.Glows(gltf, new Dictionary<int, Vector3> { [0] = new(1, 0.5f, 0.2f) });
        check("glowing pieces become lights, near ones joined", glows.Count == 2, $"{glows.Count}");
        check("each lit in its material's colour", glows.All(g => g.Colour == new Vector3(1, 0.5f, 0.2f)), "");
    }

    private static void Bc5Flip(Action<string, bool, string> check)
    {
        var random = new Random(3);
        var blocks = new byte[16 * 64];
        random.NextBytes(blocks);
        var header = new byte[128];
        System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(header, 0);
        BitConverter.GetBytes(124u).CopyTo(header, 4);
        BitConverter.GetBytes(0x1007u).CopyTo(header, 8);
        BitConverter.GetBytes(32u).CopyTo(header, 12);
        BitConverter.GetBytes(32u).CopyTo(header, 16);
        BitConverter.GetBytes(0x4u).CopyTo(header, 80);
        System.Text.Encoding.ASCII.GetBytes("ATI2").CopyTo(header, 84);
        var before = ManaDds.Read([.. header, .. blocks]).Decode();
        ManaImportMaterials.FlipBc5Green(blocks);
        var after = ManaDds.Read([.. header, .. blocks]).Decode();
        var worst = 0;
        var redSame = true;
        for (var i = 0; i < before.Pixels.Length; i += 4)
        {
            worst = Math.Max(worst, Math.Abs(255 - before.Pixels[i + 1] - after.Pixels[i + 1]));
            redSame &= before.Pixels[i] == after.Pixels[i];
        }
        check("a BC5 normal map's green flips inside its blocks", worst <= 1 && redSame, $"worst {worst}");
    }

    // An 8x2 HDR: a run of one value, then literal bytes.
    private static void Hdr(Action<string, bool, string> check)
    {
        var bytes = new List<byte>();
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 8\n"));
        for (var row = 0; row < 2; row++)
        {
            bytes.AddRange([2, 2, 0, 8]);
            bytes.AddRange([128 + 8, 128]);
            bytes.AddRange([128 + 8, 64]);
            bytes.AddRange([8, 0, 1, 2, 3, 4, 5, 6, 7]);
            bytes.AddRange([128 + 8, 129]);
        }
        var hdr = ManaHdr.Read(bytes.ToArray());
        check("a run-length HDR reads to linear floats", hdr.Width == 8 && hdr.Height == 2 && MathF.Abs(hdr.Rgb[0] - 1.0f) < 0.01f
            && MathF.Abs(hdr.Rgb[1] - 0.5f) < 0.01f && MathF.Abs(hdr.Rgb[3 * 7 + 2] - 7.0f / 128.0f) < 0.001f, $"{hdr.Rgb[0]} {hdr.Rgb[1]}");
    }

    private static void Manifest(string folder, Action<string, bool, string> check)
    {
        var checkout = Path.Combine(folder, "checkout");
        Directory.CreateDirectory(ManaDownloads.Folder(checkout));
        var zip = Path.Combine(ManaDownloads.Folder(checkout), "Pack.zip");
        File.WriteAllText(zip, "zip");
        check("a pack never imported needs importing", ManaDownloads.CurrentLevels(checkout, zip) == null, "");
        var import = ManaDownloads.ImportFolder(checkout, zip);
        Directory.CreateDirectory(import);
        File.WriteAllText(Path.Combine(import, "Pack.manalevel"), "{}");
        var manifest = ManaDownloads.Stamp(zip);
        manifest.Levels = ["Pack.manalevel"];
        ManaDownloads.WriteManifest(import, manifest);
        check("and is current once imported", ManaDownloads.CurrentLevels(checkout, zip)?.Count == 1, "");
        File.WriteAllText(zip, "a newer zip");
        check("until the zip changes", ManaDownloads.CurrentLevels(checkout, zip) == null, "");
    }

    // Falcor's scene file sets what FBX cannot carry: glass and wine that transmit light, and how much they absorb.
    private static void SceneFileMaterials(string folder, Action<string, bool, string> check)
    {
        var settings = ManaPySceneMaterials.Parse("""
            volume_absorption = {
                'red_wine': float3(117.13133, 251.91133, 294.33867),
            }
            glass = sceneBuilder.getMaterial("TransparentGlass")
            glass.roughness = 0
            glass.specularTransmission = 1
            glass.doubleSided = True  # two faces
            wine = sceneBuilder.getMaterial("Red_Wine")
            wine.specularTransmission = 1
            wine.volumeAbsorption = volume_absorption['red_wine']
            """);
        check("a scene file's material settings are read", settings.Count == 2 && settings["TransparentGlass"].Numbers["roughness"] == 0
            && settings["TransparentGlass"].Flags["doubleSided"] && settings["Red_Wine"].Colours["volumeAbsorption"].X > 117.0f,
            string.Join(", ", settings.Keys));

        var gltf = Triangles(folder, "glass", [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2], "\"nodes\":[{\"mesh\":0}]", material: true);
        gltf.Root["materials"] = new JsonArray(
            new JsonObject { ["name"] = "TransparentGlass", ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 } } },
            new JsonObject { ["name"] = "Red_Wine" });
        check("both are applied", ManaPySceneMaterials.Apply(gltf, settings) == 2, "");
        var glass = gltf.Materials[0]!;
        var wineColour = gltf.Materials[1]!["pbrMetallicRoughness"]!["baseColorFactor"]!.AsArray();
        check("glass that transmits is blended, faint, and loses the grey swatch over it",
            glass["alphaMode"]?.GetValue<string>() == "BLEND" && glass["pbrMetallicRoughness"]!["baseColorTexture"] == null
                && ManaGltfDocument.F(glass["pbrMetallicRoughness"]!["baseColorFactor"]![3]) < 0.3f && glass["doubleSided"]!.GetValue<bool>(), "");
        check("red wine is red, and more opaque than clear glass",
            ManaGltfDocument.F(wineColour[0]) > ManaGltfDocument.F(wineColour[2]) * 3 && ManaGltfDocument.F(wineColour[3]) > 0.5f,
            wineColour.ToJsonString());
    }

    private static ManaGltfDocument Triangles(string folder, string name, Vector3[] points, uint[] indices, string nodes, bool material = false)
    {
        var buffer = new List<byte>();
        foreach (var point in points)
        {
            buffer.AddRange(BitConverter.GetBytes(point.X));
            buffer.AddRange(BitConverter.GetBytes(point.Y));
            buffer.AddRange(BitConverter.GetBytes(point.Z));
        }
        var indexStart = buffer.Count;
        foreach (var index in indices)
        {
            buffer.AddRange(BitConverter.GetBytes(index));
        }
        File.WriteAllBytes(Path.Combine(folder, name + ".bin"), buffer.ToArray());
        var min = new Vector3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        var max = new Vector3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        var materialField = "";
        if (material)
        {
            materialField = ",\"material\":0";
        }
        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," + nodes + ","
            + $"\"buffers\":[{{\"uri\":\"{name}.bin\",\"byteLength\":{buffer.Count}}}],"
            + $"\"bufferViews\":[{{\"buffer\":0,\"byteOffset\":0,\"byteLength\":{indexStart}}},{{\"buffer\":0,\"byteOffset\":{indexStart},\"byteLength\":{indices.Length * 4}}}],"
            + $"\"accessors\":[{{\"bufferView\":0,\"componentType\":5126,\"count\":{points.Length},\"type\":\"VEC3\",\"min\":[{min.X},{min.Y},{min.Z}],\"max\":[{max.X},{max.Y},{max.Z}]}},"
            + $"{{\"bufferView\":1,\"componentType\":5125,\"count\":{indices.Length},\"type\":\"SCALAR\"}}],"
            + $"\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":0}},\"indices\":1{materialField}}}]}}]}}";
        var path = Path.Combine(folder, name + ".gltf");
        File.WriteAllText(path, json);
        return ManaGltfDocument.Load(path);
    }

    private static (Vector3 Min, Vector3 Max) WorldBox(ManaGltfDocument gltf) => ManaImportScene.Bounds(gltf);
}
