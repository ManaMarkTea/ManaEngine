using Mana;
using Mana.Code.Materials;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// A node's material as a level saves it: only what differs from the material the node would get anyway, so
    /// an untouched shape writes nothing and levels saved before this read back unchanged. Texture maps are not
    /// in it yet -- just the values the Material inspector edits.
    /// </summary>
    public sealed class ManaLevelMaterial
    {
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
                FlatShaded = material.FlatShaded == fresh.FlatShaded ? null : material.FlatShaded,
                Unlit = material.Unlit == fresh.Unlit ? null : material.Unlit
            };

            return captured.IsEmpty ? null : captured;
        }

        /// <summary>Writes what this carries onto <paramref name="material"/>, leaving everything else as it is.</summary>
        public void ApplyTo(ManaMaterial material)
        {
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
            Albedo == null && Emission == null && EmissionStrength == null && Metallic == null && Roughness == null
            && Occlusion == null && NormalStrength == null && Opacity == null && OpacityMode == null && AlphaCutoff == null
            && UvScale == null && UvOffset == null && FlatShaded == null && Unlit == null;

        // A float that came through a slider or a JSON round trip is never bit-identical; a thousandth is "the same".
        private static float? Differs(float value, float baseline) => MathF.Abs(value - baseline) < 0.0005f ? null : value;

        private static Vector4? Differs(Vector4 value, Vector4 baseline) =>
            Vector4.DistanceSquared(value, baseline) < 0.0005f * 0.0005f ? null : value;
    }
}
