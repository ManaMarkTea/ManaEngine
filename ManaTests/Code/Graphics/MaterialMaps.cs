using System.Text.RegularExpressions;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The PBR material maps, checked where they can be checked without a GPU: the contract between the C#
/// uniform struct and the WGSL that reads it, and the bindings the shader declares.
/// <para>
/// None of this proves a pixel is right. It does catch the failures that are otherwise silent until
/// something looks subtly wrong on screen -- a field added on one side of the uniform and not the other,
/// which shifts every field after it, or a map bound by the backend and never sampled.
/// </para>
/// </summary>
public static class MaterialMapChecks
{
    /// <summary>
    /// The OpenGL path held to the same contract as the WebGPU one. It is the fallback backend, so it is
    /// the one that quietly drifts: nothing here fails a build, and a missing uniform simply does nothing.
    /// </summary>
    private static void CheckOpenGl(Action<string, bool, string> Check)
    {
        var path = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Select(root => Path.Combine(root, "Assets", "Shaders", "LearnSpecularTextured", "pbr.frag"))
            .FirstOrDefault(File.Exists);

        if (path == null)
        {
            Check("the OpenGL PBR shader is beside the tests", false, AppContext.BaseDirectory);
            return;
        }

        var frag = File.ReadAllText(path);

        // The same packing bug the WebGPU shader had, and for the same reason: one texture holds all
        // three, so reading R everywhere gives the unused channel.
        Check("GL metallic reads blue", frag.Contains("texture(metallicMap, uv).b", StringComparison.Ordinal),
            "glTF packs metallic in B");
        Check("GL roughness reads green", frag.Contains("texture(roughnessMap, uv).g", StringComparison.Ordinal),
            "glTF packs roughness in G");
        Check("GL occlusion reads red", frag.Contains("texture(aoMap, uv).r", StringComparison.Ordinal),
            "glTF packs occlusion in R");

        // These were absent entirely, so the maps were the only source and every slider was inert.
        foreach (var uniform in new[] { "albedoColor", "materialFactors", "opacityMap", "opacityFactor", "opacityMode", "uvTransform" })
        {
            Check($"GL declares {uniform}",
                frag.Contains($" {uniform} =", StringComparison.Ordinal)
                    || frag.Contains($" {uniform};", StringComparison.Ordinal),
                uniform);
        }

        Check("GL applies the uv transform once",
            frag.Contains("vec2 uv = TexCoords * uvTransform.xy + uvTransform.zw;", StringComparison.Ordinal),
            "one coordinate for every map");

        // The tangent frame has to tile with the material or a repeated normal map lights the wrong way.
        Check("and its tangent frame follows the transformed coordinate",
            frag.Contains("st1 = dFdx(uv)", StringComparison.Ordinal), "derivatives of uv");

        Check("GL cuts out below the threshold",
            frag.Contains("alpha < materialFactors.w", StringComparison.Ordinal)
                && frag.Contains("discard", StringComparison.Ordinal), "cutout implemented");

        // Was hardcoded to 1.0, so nothing the shader computed about alpha could ever reach the blend.
        Check("and blend keeps its alpha",
            frag.Contains("mode == 3 ? alpha : 1.0", StringComparison.Ordinal), "no longer hardcoded");

        // The C# half has to actually send them.
        var shaderSource = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Select(root => Path.Combine(root, "..", "..", "..", "..", "Mana", "Code", "ManaShaders", "ManaPbrShader.cs"))
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);

        if (shaderSource == null)
        {
            return;
        }

        var binder = File.ReadAllText(shaderSource);
        foreach (var uniform in new[] { "albedoColor", "materialFactors", "opacityFactor", "opacityMode", "uvTransform" })
        {
            Check($"and the binder sets {uniform}",
                binder.Contains($"FindUniform(\"{uniform}\")", StringComparison.Ordinal), uniform);
        }

        Check("and binds the opacity map",
            binder.Contains("material.Opacity.Bind(9)", StringComparison.Ordinal), "slot 9");
    }

    /// <summary>
    /// The FlightHelmet, loaded for real. An integration check rather than a unit one: what a glTF says
    /// and what Assimp reports it as are two different things, and the mapping between them is where this
    /// went wrong -- alpha mode never read, and the occlusion map arriving as a lightmap that nothing
    /// looked at.
    /// </summary>
    private static void CheckLoadedGltf(Action<string, bool, string> Check)
    {
        var path = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Select(root => Path.Combine(root, "Assets", "Models", "FlightHelmet", "glTF", "FlightHelmet.gltf"))
            .FirstOrDefault(File.Exists);

        if (path == null)
        {
            // Reported rather than assumed: a checkout without the sample models is not a failure.
            Check("the FlightHelmet is available to load", false, "sample model not found");
            return;
        }

        var world = new Mana.ManaWorld();
        var context = new Mana.Code.ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var loader = new Mana.Code.Shapes.Loaders.ManaGltfLoader(context);

        try
        {
            loader.LoadFile(path);
        }
        catch (Exception ex)
        {
            Check("the FlightHelmet loads", false, ex.Message);
            return;
        }

        Check("the FlightHelmet loads", loader.Materials.Count > 0, $"{loader.Materials.Count} materials");

        var lenses = loader.Materials.FirstOrDefault(m => m.Name.Contains("Lens", StringComparison.OrdinalIgnoreCase));
        Check("its lenses have their own material", lenses != null,
            string.Join(", ", loader.Materials.Select(m => m.Name)));

        if (lenses == null)
        {
            return;
        }

        // glTF carries transparency on the material: the alpha is in the base colour texture, and
        // alphaMode says what to do with it. Unread, the lenses rendered as solid plastic.
        Check("alphaMode BLEND becomes a blending material",
            lenses.OpacityMode == ManaOpacityMode.Blend, lenses.OpacityMode.ToString());

        Check("and its cutoff comes across too",
            Math.Abs(lenses.AlphaCutoff - 0.5) < 0.001, lenses.AlphaCutoff.ToString("F2"));

        // Assimp reports glTF's occlusionTexture as a lightmap. Nothing read that, so ambient occlusion
        // was 1 everywhere no matter what the model shipped.
        Check("the occlusion map is picked up", lenses.Occlusion?.TextureValue != null,
            lenses.Occlusion?.TextureValue?.OriginalFilename ?? "none");

        // One packed texture serves all three, which is why the shader reads a different channel for each.
        var occlusion = lenses.Occlusion?.TextureValue?.OriginalFilename ?? string.Empty;
        Check("and it is the packed ORM texture",
            occlusion.Contains("occlusionRoughnessMetallic", StringComparison.OrdinalIgnoreCase), occlusion);

        Check("metallic and roughness read that same texture",
            lenses.Metallic?.TextureValue != null && lenses.Roughness?.TextureValue != null,
            $"{lenses.Metallic?.TextureValue?.OriginalFilename}");

        // A normal map, with the selector set so it is actually used.
        Check("the normal map is assigned", lenses.Normal?.TextureValue != null,
            lenses.Normal?.TextureValue?.OriginalFilename ?? "none");

        Check("and its selector says to use it", lenses.NormalSelector < 0.001,
            $"selector {lenses.NormalSelector} (0 means use the map)");

        // An opaque material in the same file must not have been swept along with it.
        var metal = loader.Materials.FirstOrDefault(m => m.Name.Contains("Metal", StringComparison.OrdinalIgnoreCase));
        Check("an opaque material in the same file stays opaque",
            metal == null || metal.EffectiveOpacityMode == ManaOpacityMode.Opaque,
            metal?.EffectiveOpacityMode.ToString() ?? "no metal material");
    }

    /// <summary>Bindings 0..7 are the object uniform, two samplers, albedo and the environment set.</summary>
    private const int FirstMaterialMapBinding = 8;

    private static readonly string[] MapBindings =
    {
        "normal_texture",
        "emission_texture",
        "metallic_texture",
        "roughness_texture",
        "occlusion_texture",
        "opacity_texture"
    };

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        CheckLoadedGltf(Check);
        CheckOpenGl(Check);

        // Both roots: `dotnet run` leaves the working directory at the repository, while the built output
        // is where the shaders are actually copied. Searching one alone finds nothing in the other.
        var shaderDirectory = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Where(root => !string.IsNullOrEmpty(root))
            .Select(root => Path.Combine(root, "Assets", "Shaders", "WebGpu"))
            .FirstOrDefault(directory => File.Exists(Path.Combine(directory, "pbr_irradiance.wgsl")));

        if (shaderDirectory == null)
        {
            Check("the WebGPU shaders are beside the tests", false, AppContext.BaseDirectory);
            return;
        }

        var pbrPath = Path.Combine(shaderDirectory, "pbr_irradiance.wgsl");
        var uniformsPath = Path.Combine(shaderDirectory, "include", "uniforms.wgsl");

        if (!File.Exists(uniformsPath))
        {
            Check("the shared uniform include is beside them", false, uniformsPath);
            return;
        }

        var pbr = File.ReadAllText(pbrPath);
        var uniforms = File.ReadAllText(uniformsPath);

        // ---------------------------------------------------------------- bindings
        for (var i = 0; i < MapBindings.Length; i++)
        {
            var binding = FirstMaterialMapBinding + i;
            var declaration = $"@group(1) @binding({binding}) var {MapBindings[i]} : texture_2d<f32>;";

            Check($"{MapBindings[i]} is bound at {binding}",
                pbr.Contains(declaration, StringComparison.Ordinal), declaration);

            // Bound and never read is the quiet failure: the backend uploads the texture, the bind group
            // validates, and the map simply has no effect -- which is exactly what "I assigned a normal
            // map and nothing happened" looks like.
            Check($"and {MapBindings[i]} is actually sampled",
                pbr.Contains($"textureSample({MapBindings[i]}, color_sampler", StringComparison.Ordinal),
                "sampled in the fragment shader");
        }

        // Every map shares one transform, so a tiled brick stays registered across albedo and normal.
        var sampleCount = Regex.Matches(pbr, @"textureSample\((?<t>\w+_texture), color_sampler, uv\)").Count;
        Check("every material map samples the transformed coordinate", sampleCount >= MapBindings.Length + 1,
            $"{sampleCount} maps sampling 'uv'");

        Check("and the transform is applied once, from the uniform",
            pbr.Contains("input.uv0 * object.uv_transform.xy + object.uv_transform.zw", StringComparison.Ordinal),
            "uv built from uv_transform");

        // ---------------------------------------------------------------- the uniform contract
        // WGSL derives offsets from declared order, so a field added on one side and not the other shifts
        // everything after it. The struct is shared by every shader, so that would be silent and total.
        var expected = new (string Name, int Size, int Alignment)[]
        {
            ("model", 64, 16),
            ("normal_matrix", 64, 16),
            ("material_albedo", 16, 16),
            ("material_factors", 16, 16),
            ("material_emission", 16, 16),
            ("outline_color", 16, 16),
            ("outline_parameters", 16, 16),
            ("debug_mode_override", 4, 4),
            ("flat_shading", 4, 4),
            ("normal_strength", 4, 4),
            ("opacity_mode", 4, 4),
            ("uv_transform", 16, 16)
        };

        var body = Regex.Match(uniforms, @"struct\s+ObjectUniform\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        Check("the shared ObjectUniform is declared", body.Success);

        if (!body.Success)
        {
            return;
        }

        var fields = Regex.Matches(body.Groups["body"].Value, @"^\s*(?<name>\w+)\s*:\s*(?<type>[^,\r\n]+),",
                RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToList();

        Check("its fields are the ones the C# struct writes",
            fields.SequenceEqual(expected.Select(f => f.Name)),
            string.Join(", ", fields));

        // Offsets computed the way WGSL does, so this catches a field of the wrong size as well as one in
        // the wrong place. 240 bytes is what ManaWebGpuObjectUniform measures.
        var offset = 0;
        var misaligned = new List<string>();

        foreach (var (name, size, alignment) in expected)
        {
            if (offset % alignment != 0)
            {
                misaligned.Add($"{name}@{offset}");
            }

            offset += size;
        }

        Check("no field lands on a bad offset", misaligned.Count == 0, string.Join(", ", misaligned));
        Check("and the struct is the size the buffer is allocated for", offset == 240, $"{offset} bytes");
        Check("which is a multiple of 16, as a uniform buffer must be", offset % 16 == 0, $"{offset} bytes");

        // ---------------------------------------------------------------- normal mapping
        // No mesh here carries tangents, so the frame is rebuilt from derivatives. Degenerate UVs have no
        // frame at all, and returning a NaN there would blow a hole in the surface.
        Check("the tangent frame is derived rather than assumed",
            pbr.Contains("fn mana_perturb_normal", StringComparison.Ordinal), "no tangent attribute needed");

        Check("and degenerate UVs fall back instead of producing NaN",
            pbr.Contains("abs(determinant) < 1e-12", StringComparison.Ordinal), "guarded");

        // ---------------------------------------------------------------- packed channels
        // glTF puts occlusion in R, roughness in G and metallic in B, all in one texture, and Assimp hands
        // both channels the same file. Reading R gave the packed map's unused channel -- usually 1 -- so
        // every loaded model came out fully metallic and fully rough. A standalone greyscale map has the
        // same value in every channel, so reading the packed one is right for both.
        Check("metallic reads the blue channel",
            pbr.Contains("textureSample(metallic_texture, color_sampler, uv).b", StringComparison.Ordinal),
            "glTF packs metallic in B");

        Check("roughness reads green",
            pbr.Contains("textureSample(roughness_texture, color_sampler, uv).g", StringComparison.Ordinal),
            "glTF packs roughness in G");

        Check("and occlusion reads red",
            pbr.Contains("textureSample(occlusion_texture, color_sampler, uv).r", StringComparison.Ordinal),
            "glTF packs occlusion in R");

        Check("normal strength comes from the material",
            pbr.Contains("object.normal_strength", StringComparison.Ordinal), "wired to Normal mix");

        // ---------------------------------------------------------------- opacity
        // An opacity map that only lowers alpha does nothing: the opaque blend state is One/Zero, so the
        // alpha is computed and thrown away. The mode is what turns it into pixels.
        Check("cutout discards below the threshold",
            pbr.Contains("discard", StringComparison.Ordinal)
                && pbr.Contains("alpha < object.material_factors.w", StringComparison.Ordinal),
            "cutout implemented");

        Check("and the discard sits after every derivative",
            pbr.IndexOf("discard", StringComparison.Ordinal) > pbr.LastIndexOf("dpdx(", StringComparison.Ordinal),
            "derivatives after a discard are undefined");

        Check("blend keeps its alpha and the other modes do not",
            pbr.Contains("select(1.0, alpha, opacity_mode == 3u)", StringComparison.Ordinal),
            "opaque forces solid");
    }
}
