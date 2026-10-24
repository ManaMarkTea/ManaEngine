using Mana;
using Mana.Code.Materials;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// A node's material as a level saves it: only what differs from the material the node would get anyway, so
    /// an untouched shape writes nothing and levels saved before this read back unchanged. Texture maps use portable
    /// picture references, including entries inside material ZIPs.
    /// </summary>
    public sealed class ManaLevelMaterial
    {
        public string? SourceZip { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public IEnumerable<string> Pictures => new[] { AlbedoMap, NormalMap, RoughnessMap, MetallicMap, OcclusionMap, EmissionMap, OpacityMap }
            .OfType<string>().Where(path => path.Length > 0);

        public static IEnumerable<MeshRendererComponent> MeshRenderers(Mana.Code.Nodes.ManaSceneNode node)
        {
            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                yield return renderer;
            }
            foreach (var child in node.Children)
            {
                foreach (var renderer in MeshRenderers(child))
                {
                    yield return renderer;
                }
            }
        }
        public string? AlbedoMap { get; set; }
        public string? NormalMap { get; set; }
        public string? RoughnessMap { get; set; }
        public string? MetallicMap { get; set; }
        public string? OcclusionMap { get; set; }
        public string? EmissionMap { get; set; }
        public string? OpacityMap { get; set; }

        public Vector4? Albedo { get; set; }
        public Vector4? Emission { get; set; }
        public float? EmissionStrength { get; set; }
        public float? Metallic { get; set; }
        public float? Roughness { get; set; }
        public float? Occlusion { get; set; }

        /// <summary>How strongly the normal map applies, 0..1, as the inspector shows it.</summary>
        public float? NormalStrength { get; set; }

        public float? Opacity { get; set; }
        public ManaOpacityMode? OpacityMode { get; set; }
        public float? AlphaCutoff { get; set; }
        public Vector2? UvScale { get; set; }
        public Vector2? UvOffset { get; set; }
        public float? InsetDepth { get; set; }
        public bool? FlatShaded { get; set; }
        public bool? Unlit { get; set; }

        /// <summary>
        /// What of <paramref name="material"/> differs from a fresh one coloured <paramref name="baseColour"/> --
        /// the material a shape is built with -- or null when nothing does.
        /// </summary>
        public static ManaLevelMaterial? Capture(ManaMaterial material, ManaContext context, Vector4 baseColour)
        {
            var fresh = new ManaMaterial(context);
            fresh.Albedo.Value = baseColour;

            var captured = new ManaLevelMaterial
            {
                SourceZip = material.SourceZip,
                AlbedoMap = string.IsNullOrEmpty(material.Albedo.TextureValue?.OriginalFilename) ? null : material.Albedo.TextureValue?.OriginalFilename,
                NormalMap = string.IsNullOrEmpty(material.Normal.TextureValue?.OriginalFilename) ? null : material.Normal.TextureValue?.OriginalFilename,
                RoughnessMap = string.IsNullOrEmpty(material.Roughness.TextureValue?.OriginalFilename) ? null : material.Roughness.TextureValue?.OriginalFilename,
                MetallicMap = string.IsNullOrEmpty(material.Metallic.TextureValue?.OriginalFilename) ? null : material.Metallic.TextureValue?.OriginalFilename,
                OcclusionMap = string.IsNullOrEmpty(material.Occlusion.TextureValue?.OriginalFilename) ? null : material.Occlusion.TextureValue?.OriginalFilename,
                EmissionMap = string.IsNullOrEmpty(material.Emission.TextureValue?.OriginalFilename) ? null : material.Emission.TextureValue?.OriginalFilename,
                OpacityMap = string.IsNullOrEmpty(material.Opacity.TextureValue?.OriginalFilename) ? null : material.Opacity.TextureValue?.OriginalFilename,
                Albedo = Differs(material.Albedo.Value, fresh.Albedo.Value),
                Emission = Differs(material.Emission.Value, fresh.Emission.Value),
                EmissionStrength = Differs((float)material.EmissionStrength, (float)fresh.EmissionStrength),
                Metallic = Differs(material.Metallic.Value, fresh.Metallic.Value),
                Roughness = Differs(material.Roughness.Value, fresh.Roughness.Value),
                Occlusion = Differs(material.Occlusion.Value, fresh.Occlusion.Value),
                NormalStrength = Differs(1.0f - (float)material.NormalSelector, 1.0f - (float)fresh.NormalSelector),
                Opacity = Differs(material.Opacity.Value, fresh.Opacity.Value),
                OpacityMode = material.OpacityMode == fresh.OpacityMode ? null : material.OpacityMode,
                AlphaCutoff = Differs((float)material.AlphaCutoff, (float)fresh.AlphaCutoff),
                UvScale = material.UvScale == fresh.UvScale ? null : material.UvScale,
                UvOffset = material.UvOffset == fresh.UvOffset ? null : material.UvOffset,
                InsetDepth = Differs(material.InsetDepth, fresh.InsetDepth),
                FlatShaded = material.FlatShaded == fresh.FlatShaded ? null : material.FlatShaded,
                Unlit = material.Unlit == fresh.Unlit ? null : material.Unlit
            };

            return captured.IsEmpty ? null : captured;
        }

        /// <summary>Writes what this carries onto <paramref name="material"/>, leaving everything else as it is.</summary>
        public void ApplyTo(ManaMaterial material)
        {
            material.SourceZip = SourceZip;
            if (SourceZip != null)
            {
                // ZIP snapshots use fresh PBR defaults, rather than the imported model's old factors.
                var defaults = new ManaMaterial(material.Context);
                material.Albedo.Value = Vector4.One;
                material.Emission.Value = defaults.Emission.Value;
                material.Metallic.Value = defaults.Metallic.Value;
                material.Roughness.Value = defaults.Roughness.Value;
                material.Occlusion.Value = defaults.Occlusion.Value;
                material.Opacity.Value = defaults.Opacity.Value;
                material.NormalSelector = defaults.NormalSelector;
                material.EmissionStrength = defaults.EmissionStrength;
                material.OpacityMode = defaults.OpacityMode;
                material.AlphaCutoff = defaults.AlphaCutoff;
                material.UvScale = defaults.UvScale;
                material.UvOffset = defaults.UvOffset;
                material.InsetDepth = defaults.InsetDepth;
                material.FlatShaded = defaults.FlatShaded;
                material.Unlit = defaults.Unlit;
                material.Albedo.TextureValue = null;
                material.Normal.TextureValue = null;
                material.Roughness.TextureValue = null;
                material.Metallic.TextureValue = null;
                material.Occlusion.TextureValue = null;
                material.Emission.TextureValue = null;
                material.Opacity.TextureValue = null;
            }
            if (AlbedoMap is { } albedoMap)
            {
                material.Albedo.TextureValue = albedoMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = albedoMap };
            }
            if (NormalMap is { } normalMap)
            {
                material.Normal.TextureValue = normalMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = normalMap };
            }
            if (RoughnessMap is { } roughnessMap)
            {
                material.Roughness.TextureValue = roughnessMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = roughnessMap };
            }
            if (MetallicMap is { } metallicMap)
            {
                material.Metallic.TextureValue = metallicMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = metallicMap };
            }
            if (OcclusionMap is { } occlusionMap)
            {
                material.Occlusion.TextureValue = occlusionMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = occlusionMap };
            }
            if (EmissionMap is { } emissionMap)
            {
                material.Emission.TextureValue = emissionMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = emissionMap };
            }
            if (OpacityMap is { } opacityMap)
            {
                material.Opacity.TextureValue = opacityMap.Length == 0 ? null : new ManaTexture(material.Context) { OriginalFilename = opacityMap };
            }
            if (Albedo is { } albedo)
            {
                material.Albedo.Value = albedo;
            }

            if (Emission is { } emission)
            {
                material.Emission.Value = emission;
            }

            if (EmissionStrength is { } emissionStrength)
            {
                material.EmissionStrength = emissionStrength;
            }

            if (Metallic is { } metallic)
            {
                material.Metallic.Value = metallic;
            }

            if (Roughness is { } roughness)
            {
                material.Roughness.Value = roughness;
            }

            if (Occlusion is { } occlusion)
            {
                material.Occlusion.Value = occlusion;
            }

            if (NormalStrength is { } normalStrength)
            {
                material.NormalSelector = 1.0f - normalStrength;
            }

            if (Opacity is { } opacity)
            {
                material.Opacity.Value = opacity;
            }

            if (OpacityMode is { } opacityMode)
            {
                material.OpacityMode = opacityMode;
            }

            if (AlphaCutoff is { } alphaCutoff)
            {
                material.AlphaCutoff = alphaCutoff;
            }

            if (UvScale is { } uvScale)
            {
                material.UvScale = uvScale;
            }

            if (UvOffset is { } uvOffset)
            {
                material.UvOffset = uvOffset;
            }

            if (InsetDepth is { } insetDepth) material.InsetDepth = insetDepth;

            if (FlatShaded is { } flatShaded)
            {
                material.FlatShaded = flatShaded;
            }

            if (Unlit is { } unlit)
            {
                material.Unlit = unlit;
            }
        }

        private bool IsEmpty =>
            SourceZip == null &&
            AlbedoMap == null && NormalMap == null && RoughnessMap == null && MetallicMap == null && OcclusionMap == null && EmissionMap == null && OpacityMap == null
            && Albedo == null && Emission == null && EmissionStrength == null && Metallic == null && Roughness == null
            && Occlusion == null && NormalStrength == null && Opacity == null && OpacityMode == null && AlphaCutoff == null
            && InsetDepth == null && UvScale == null && UvOffset == null && FlatShaded == null && Unlit == null;

        // A float that came through a slider or a JSON round trip is never bit-identical; a thousandth is "the same".
        private static float? Differs(float value, float baseline) => MathF.Abs(value - baseline) < 0.0005f ? null : value;

        private static Vector4? Differs(Vector4 value, Vector4 baseline) =>
            Vector4.DistanceSquared(value, baseline) < 0.0005f * 0.0005f ? null : value;
    }
}
