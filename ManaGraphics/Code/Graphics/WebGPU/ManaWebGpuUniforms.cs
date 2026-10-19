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
