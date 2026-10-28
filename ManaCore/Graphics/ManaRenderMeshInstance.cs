using System.Collections.Generic;
using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed record ManaRenderMeshInstance
    {
        /// <summary>Optional render-only static batching scope; scene nodes remain independent.</summary>
        public string? StaticBatchKey { get; init; }

        public string? InstanceKey { get; init; }
        public string? MeshKey { get; init; }
        public ulong MeshVersion { get; init; }
        public required string Name { get; init; }
        public required ManaMeshCpuData MeshData { get; init; }
        public required Matrix4x4 WorldTransform { get; init; }
        public string? ShaderName { get; init; }
        public Vector4 Albedo { get; init; } = Vector4.One;
        public float Metallic { get; init; }
        public float Roughness { get; init; } = 1.0f;
        public bool FlatShaded { get; init; }
        public bool Unlit { get; init; }
        public float AmbientOcclusion { get; init; } = 1.0f;
        public Vector4 Emission { get; init; } = Vector4.Zero;
        public float EmissionStrength { get; init; }
        public float? PbrDebugMode { get; init; }
        public bool IncludeInEnvironmentCapture { get; init; } = true;
        public bool IsTransparent { get; init; }
        public bool DisableDepthWrite { get; init; }

        /// <summary>Drawn after everything else, ignoring depth, so it can never be hidden behind scene geometry. Editor handles.</summary>
        public bool AlwaysOnTop { get; init; }

        public bool OutlineEnabled { get; init; }
        public Vector4 OutlineColor { get; init; } = new(1.0f, 0.82f, 0.18f, 1.0f);
        public float OutlineWidth { get; init; } = 2.0f;

        /// <summary>Selected in the editor: its edges are drawn faintly over it.</summary>
        public bool SelectionWireframe { get; init; }

        /// <summary>Each side lit as the side facing the camera: a thin sheet seen from both sides.</summary>
        public bool TwoSidedLighting { get; init; }

        /// <summary>
        /// Texture coordinate scale in XY and offset in ZW, applied to every map on this material.
        /// <para>
        /// A material property rather than mesh data: the same brick texture wants a different repeat on a
        /// wall than on a doorframe, and neither should mean editing the mesh.
        /// </para>
        /// </summary>
        public Vector4 UvTransform { get; init; } = new(1.0f, 1.0f, 0.0f, 0.0f);

        /// <summary>Apparent recess in world metres; zero disables parallax.</summary>
        public float InsetDepth { get; init; }

        /// <summary>How strongly the normal map perturbs the surface. 0 ignores it entirely.</summary>
        public float NormalStrength { get; init; } = 1.0f;

        /// <summary>What the renderer does with this material's alpha. Never Auto -- resolved before here.</summary>
        public ManaOpacityMode OpacityMode { get; init; } = ManaOpacityMode.Opaque;

        /// <summary>Alpha below this is discarded in <see cref="ManaOpacityMode.Cutout"/>.</summary>
        public float AlphaCutoff { get; init; } = 0.5f;

        /// <summary>
        /// How solid the material is before any opacity map. Multiplies the albedo's own alpha, so the two
        /// compose rather than one overriding the other.
        /// </summary>
        public float Opacity { get; init; } = 1.0f;

        // Per-material named uniform overrides (from ManaMaterial.ShaderUniforms), normalized to
        // Vector4 (float in .X, vec3 in .XYZ) so this stays backend-agnostic. Consumed by shaders
        // that declare a matching custom uniform block (see ManaWebGpuContext.Uniforms.cs).
        public IReadOnlyDictionary<string, Vector4>? CustomShaderUniforms { get; init; }
    }
}
