using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics.Resources
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
        // Clustered placed lights (ManaLightClusters): grid xyz = tiles across, tiles down, depth slices (x 0 = none);
        // depth x = first slice's start, y = slices per log-unit, zw = viewport pixels; forward xyz = the camera's look.
        public Vector4 ClusterGrid;
        public Vector4 ClusterDepth;
        public Vector4 CameraForward;
        public Vector4 Wind;
        public Vector4 WindMotion;
        // The probe volume (ManaWebGpuProbeVolume): xyz its low corner, w 1 when baked; xyz the probes' extent
        // (count times spacing, which the harmonics textures span), w the spacing.
        public Vector4 ProbeVolumeMin;
        public Vector4 ProbeVolumeExtent;

        // Written once a frame, not per mesh: camera, time and lights are the same for every mesh, and baking them
        // into each mesh's own buffer meant the clock alone dirtied every resident mesh's uniform every frame.
        public static ManaWebGpuFrameUniform For(ManaRenderScene scene, ManaWebGpuShadowMap shadows, ManaLightClusters lights, float seconds)
        {
            var camera = scene.Camera;
            var irradianceSettings = scene.IrradianceSettings;
            var cascades = shadows.CascadeViewProjections;
            var splits = shadows.CascadeSplitFar;

            var uniform = new ManaWebGpuFrameUniform
            {
                ViewProjection = camera.View * camera.Projection,
                CameraPosition = new Vector4(camera.Position, 1.0f),
                IrradianceFactors = new Vector4(
                    Math.Clamp(irradianceSettings.Exposure, 0.0f, 8.0f),
                    // A computed sky is drawn into the reflection cubes already darkened; only the photo needs dimming.
                    Math.Clamp(irradianceSettings.IblIntensity * (scene.Sky.Mode == ManaSkyMode.Photo ? scene.Sky.Daylight : 1.0f), 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.LightIntensity, 0.0f, 64.0f),
                    // w = daylight, 0..1 (ManaSky): the unlit editor grid dims with it at night.
                    scene.Sky.Daylight),
                LightPositions0 = scene.LightPositions[0],
                LightPositions1 = scene.LightPositions[1],
                LightPositions2 = scene.LightPositions[2],
                LightPositions3 = scene.LightPositions[3],
                LightColors0 = scene.LightColors[0],
                LightColors1 = scene.LightColors[1],
                LightColors2 = scene.LightColors[2],
                LightColors3 = scene.LightColors[3],
                Time = seconds,
                WebGpuDebugMode = irradianceSettings.WebGpuDebugMode,
                VertexNormalLength = irradianceSettings.VertexNormalLength,
                _padding2 = 0.0f,
                CascadeViewProjection0 = cascades[0],
                CascadeViewProjection1 = cascades[1],
                CascadeViewProjection2 = cascades[2],
                CascadeSplitFar = new Vector4(splits[0], splits[1], splits[2], 0.0f),
                SunDirection = new Vector4(scene.SunDirection, scene.HasSun && scene.SunCastsShadows ? 1.0f : 0.0f),
                SunColor = scene.HasSun ? scene.SunColor : Vector4.Zero
            };

            var wind = scene.Wind;
            uniform.Wind = new Vector4(wind.Direction.X, wind.Direction.Y, wind.Speed, wind.Gustiness);
            uniform.WindMotion = new Vector4(wind.Drift.X, wind.Drift.Y, wind.Distance, wind.Gust);

            // w = light count; with none the shaders skip the cells, which the binning left as an older frame's.
            if (lights.Viewport.X >= 1.0f && lights.Viewport.Y >= 1.0f)
            {
                uniform.ClusterGrid = new Vector4(ManaLightClusters.TilesX, ManaLightClusters.TilesY, ManaLightClusters.Slices, lights.LightCount);
            }
            uniform.ClusterDepth = new Vector4(lights.Near, lights.SliceScale, lights.Viewport.X, lights.Viewport.Y);
            uniform.CameraForward = new Vector4(lights.Forward, 0.0f);

            return uniform;
        }
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
        public Vector4 Parallax;

        // Pure, so callers can dirty-check before uploading.
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
                // Opacity folds into albedo alpha.
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
                Parallax = new Vector4(float.IsFinite(meshInstance.InsetDepth) ? Math.Max(0, meshInstance.InsetDepth) : 0, 0, 0, 0),
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
    // layout exactly. Not used by the main draw path.
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

        // Captures keep the old combined per-face layout: they are rare, and scene_capture.wgsl stays as it was.
        public static ManaWebGpuSceneCaptureUniform For(ManaRenderMeshInstance meshInstance, ManaCameraData camera, ManaRenderScene scene, float seconds)
        {
            var model = meshInstance.WorldTransform;
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }
            var irradianceSettings = scene.IrradianceSettings;

            return new ManaWebGpuSceneCaptureUniform
            {
                ModelProjection = model * camera.View * camera.Projection,
                Model = model,
                NormalMatrix = Matrix4x4.Transpose(inverseModel),
                CameraPosition = new Vector4(camera.Position, 1.0f),
                MaterialAlbedo = meshInstance.Albedo,
                // Scene capture keeps its own pre-split layout; the material maps are a main-pass concern.
                MaterialFactors = new Vector4(
                    Math.Clamp(meshInstance.Metallic, 0.0f, 1.0f),
                    Math.Clamp(meshInstance.Roughness, 0.0f, 1.0f),
                    Math.Clamp(meshInstance.AmbientOcclusion, 0.0f, 1.0f),
                    1.0f),
                MaterialEmission = new Vector4(
                    meshInstance.Emission.X,
                    meshInstance.Emission.Y,
                    meshInstance.Emission.Z,
                    meshInstance.EmissionStrength),
                IrradianceFactors = new Vector4(
                    Math.Clamp(irradianceSettings.Exposure, 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.IblIntensity, 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.LightIntensity, 0.0f, 64.0f),
                    1.0f),
                LightPositions0 = scene.LightPositions[0],
                LightPositions1 = scene.LightPositions[1],
                LightPositions2 = scene.LightPositions[2],
                LightPositions3 = scene.LightPositions[3],
                LightColors0 = scene.LightColors[0],
                LightColors1 = scene.LightColors[1],
                LightColors2 = scene.LightColors[2],
                LightColors3 = scene.LightColors[3],
                Time = seconds,
                WebGpuDebugMode = meshInstance.PbrDebugMode ?? irradianceSettings.WebGpuDebugMode,
                _padding1 = 0.0f,
                _padding2 = 0.0f
            };
        }
    }
}
