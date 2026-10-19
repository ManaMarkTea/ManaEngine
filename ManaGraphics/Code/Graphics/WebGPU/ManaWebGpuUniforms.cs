using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics
{
    // Written once per frame -- group 0, binding 0. Identical for every mesh drawn this frame.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ManaWebGpuFrameUniform
    {
        public Matrix4x4 ViewProjection;
        public Vector4 CameraPosition;
        public Vector4 IrradianceFactors;
        public Vector4 LightPositions0;
        public Vector4 LightPositions1;
        public Vector4 LightPositions2;
        public Vector4 LightPositions3;
        public Vector4 LightColors0;
        public Vector4 LightColors1;
        public Vector4 LightColors2;
        public Vector4 LightColors3;
        public float Time;
        public float WebGpuDebugMode;
        public float VertexNormalLength;
        public float _padding2;
        public Matrix4x4 CascadeViewProjection0;
        public Matrix4x4 CascadeViewProjection1;
        public Matrix4x4 CascadeViewProjection2;
        public Vector4 CascadeSplitFar;
        public Vector4 SunDirection;
        public Vector4 SunColor;
        public ManaWebGpuLocalLightArray LocalLightPositions;
        public ManaWebGpuLocalLightArray LocalLightColors;
        public Vector4 Wind;
        public Vector4 WindMotion;
    }

    [System.Runtime.CompilerServices.InlineArray(ManaRenderScene.MaxLocalLights)]
    internal struct ManaWebGpuLocalLightArray
    {
        private Vector4 _element;
    }

    // Written per mesh, only when the mesh's transform/material actually changed since last
    // frame (see ManaWebGpuSceneMesh.TryUpdateObjectUniform) -- group 1, binding 0.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ManaWebGpuObjectUniform
    {
        public Matrix4x4 Model;
        public Matrix4x4 NormalMatrix;
        public Vector4 MaterialAlbedo;
        public Vector4 MaterialFactors;
        public Vector4 MaterialEmission;
        public Vector4 OutlineColor;
        public Vector4 OutlineParameters;
        public float DebugModeOverride;
        public float FlatShading;
        public float NormalStrength;
        public float OpacityMode;

        /// <summary>Texture coordinate scale in XY, offset in ZW. Applies to every map.</summary>
        public Vector4 UvTransform;

        // Pure function, no GPU call -- lets callers dirty-check the result against the mesh's last
        // uploaded value before spending a QueueWriteBuffer on it (see
        // ManaWebGpuSceneMesh.TryUpdateObjectUniform in ManaWebGpuContext.Scene.cs).
        public static ManaWebGpuObjectUniform For(ManaRenderMeshInstance meshInstance)
        {
            var model = meshInstance.WorldTransform;
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }
            var normalMatrix = Matrix4x4.Transpose(inverseModel);

            return new ManaWebGpuObjectUniform
            {
                Model = model,
                NormalMatrix = normalMatrix,
                // The material's opacity folds into the albedo's alpha rather than travelling as its own
                // field: both answer "how solid is this", the shader already reads this channel, and
                // multiplying is what composing them means.
                MaterialAlbedo = new Vector4(
                    meshInstance.Albedo.X,
                    meshInstance.Albedo.Y,
                    meshInstance.Albedo.Z,
                    meshInstance.Albedo.W * Math.Clamp(meshInstance.Opacity, 0.0f, 1.0f)),
                MaterialFactors = new Vector4(
                    Math.Clamp(meshInstance.Metallic, 0.0f, 1.0f),
                    Math.Clamp(meshInstance.Roughness, 0.0f, 1.0f),
                    Math.Clamp(meshInstance.AmbientOcclusion, 0.0f, 1.0f),
                    // w carries the cutout threshold, which is only read in that mode.
                    Math.Clamp(meshInstance.AlphaCutoff, 0.0f, 1.0f)),
                UvTransform = meshInstance.UvTransform,
                OpacityMode = (float)meshInstance.OpacityMode,
                NormalStrength = Math.Clamp(meshInstance.NormalStrength, 0.0f, 1.0f),
                MaterialEmission = new Vector4(
                    meshInstance.Emission.X,
                    meshInstance.Emission.Y,
                    meshInstance.Emission.Z,
                    meshInstance.EmissionStrength),
                OutlineColor = meshInstance.OutlineColor,
                OutlineParameters = new Vector4(
                    Math.Clamp(meshInstance.OutlineWidth, 1.0f, 4.0f),
                    meshInstance.OutlineEnabled ? 1.0f : 0.0f,
                    // z: the material has an emission map of its own (pbr_irradiance.wgsl's emission).
                    string.IsNullOrEmpty(meshInstance.MeshData.EmissionTexturePath) ? 0.0f : 1.0f,
                    // w: unlit, drawn as its colour (pbr_irradiance.wgsl).
                    meshInstance.Unlit ? 1.0f : 0.0f),
                // -1 means "no per-object override, use the frame's global debug mode" -- valid debug
                // mode indexes are >= 0 (see ManaIrradianceSettings.WebGpuDebugMode).
                DebugModeOverride = meshInstance.PbrDebugMode ?? -1.0f,
                FlatShading = meshInstance.FlatShaded ? 1.0f : 0.0f
            };
        }
    }

    // Scene-capture-only, matches scene_capture.wgsl's unchanged (pre-split) TransformUniform
    // layout exactly. Not used by the main draw path -- see UpdateSceneCaptureUniform above.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ManaWebGpuSceneCaptureUniform
    {
        public Matrix4x4 ModelProjection;
        public Matrix4x4 Model;
        public Matrix4x4 NormalMatrix;
        public Vector4 CameraPosition;
        public Vector4 MaterialAlbedo;
        public Vector4 MaterialFactors;
        public Vector4 MaterialEmission;
        public Vector4 IrradianceFactors;
        public Vector4 LightPositions0;
        public Vector4 LightPositions1;
        public Vector4 LightPositions2;
        public Vector4 LightPositions3;
        public Vector4 LightColors0;
        public Vector4 LightColors1;
        public Vector4 LightColors2;
        public Vector4 LightColors3;
        public float Time;
        public float WebGpuDebugMode;
        public float _padding1;
        public float _padding2;
    }
}
