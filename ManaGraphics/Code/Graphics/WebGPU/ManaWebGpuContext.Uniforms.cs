using System.Linq;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Buffers.Binary;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        // Writes the per-frame global uniform buffer ONCE (not per mesh) -- camera, time, lights,
        // irradiance settings are identical for every mesh drawn this frame, so baking them into
        // every mesh's own buffer (the old ManaWebGpuTransformUniform monolith) meant a moving camera
        // -- or the constantly-changing Time field alone -- invalidated all ~2000 resident meshes'
        // uniforms every single frame, defeating any per-object dirty-check. See
        // ManaWebGpuObjectUniform below for the per-object half.
        private void UpdateFrameUniform()
        {
            if (_activeRenderScene == null || _bindings == null)
            {
                return;
            }

            var camera = _activeRenderScene.Camera;
            var irradianceSettings = _activeRenderScene.IrradianceSettings;
            var viewProjection = camera.View * GetAspectCorrectedProjection(camera);
            var elapsedSeconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
            // The bindings are made with the shadow map, so it exists whenever they do.
            var cascades = _shadowMap!.CascadeViewProjections;
            var splits = _shadowMap.CascadeSplitFar;

            var uniform = new ManaWebGpuFrameUniform
            {
                ViewProjection = viewProjection,
                CameraPosition = new Vector4(camera.Position, 1.0f),
                IrradianceFactors = new Vector4(
                    Math.Clamp(irradianceSettings.Exposure, 0.0f, 8.0f),
                    // A computed sky is drawn into the reflection cubes already darkened; only the photo needs dimming.
                    Math.Clamp(irradianceSettings.IblIntensity * (_activeRenderScene.Sky.Mode == ManaSkyMode.Photo ? _activeRenderScene.Sky.Daylight : 1.0f), 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.LightIntensity, 0.0f, 64.0f),
                    // w = daylight, 0..1 (ManaSky): the unlit editor grid dims with it at night.
                    _activeRenderScene.Sky.Daylight),
                LightPositions0 = _activeRenderScene.LightPositions[0],
                LightPositions1 = _activeRenderScene.LightPositions[1],
                LightPositions2 = _activeRenderScene.LightPositions[2],
                LightPositions3 = _activeRenderScene.LightPositions[3],
                LightColors0 = _activeRenderScene.LightColors[0],
                LightColors1 = _activeRenderScene.LightColors[1],
                LightColors2 = _activeRenderScene.LightColors[2],
                LightColors3 = _activeRenderScene.LightColors[3],
                Time = elapsedSeconds,
                WebGpuDebugMode = irradianceSettings.WebGpuDebugMode,
                VertexNormalLength = irradianceSettings.VertexNormalLength,
                _padding2 = 0.0f,
                CascadeViewProjection0 = cascades[0],
                CascadeViewProjection1 = cascades[1],
                CascadeViewProjection2 = cascades[2],
                CascadeSplitFar = new Vector4(splits[0], splits[1], splits[2], 0.0f),
                SunDirection = new Vector4(
                    _activeRenderScene.SunDirection,
                    _activeRenderScene.HasSun && _activeRenderScene.SunCastsShadows ? 1.0f : 0.0f),
                SunColor = _activeRenderScene.HasSun
                    ? _activeRenderScene.SunColor
                    : Vector4.Zero
            };

            var wind = _activeRenderScene.Wind;
            uniform.Wind = new Vector4(wind.Direction.X, wind.Direction.Y, wind.Speed, wind.Gustiness);
            uniform.WindMotion = new Vector4(wind.Drift.X, wind.Drift.Y, wind.Distance, wind.Gust);

            for (var i = 0; i < ManaRenderScene.MaxLocalLights; i++)
            {
                uniform.LocalLightPositions[i] = _activeRenderScene.LocalLightPositions[i];
                uniform.LocalLightColors[i] = _activeRenderScene.LocalLightColors[i];
            }

            _bindings.WriteFrame(_queue, uniform);
        }

        // Scene-capture (cubemap probe refresh, ManaWebGpuContext.Environment.cs) intentionally keeps
        // the OLD combined per-mesh-per-face layout instead of adopting the frame/object split above:
        // captures are infrequent (not every frame) so the redundant per-face rewrite isn't worth the
        // extra complexity of threading a second per-face "frame" buffer through the split, and
        // scene_capture.wgsl's struct is left untouched to match -- this is a verbatim copy of the
        // pre-split UpdateDemoUniforms(instance, buffer, camera) logic, just under its own name/struct.
        private void UpdateSceneCaptureUniform(ManaRenderMeshInstance meshInstance, WebGpuBuffer* uniformBuffer, ManaCameraData camera)
        {
            if (uniformBuffer == null || _activeRenderScene == null)
            {
                return;
            }

            var model = meshInstance.WorldTransform;
            var modelProjection = model * camera.View * GetAspectCorrectedProjection(camera);
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }
            var normalMatrix = Matrix4x4.Transpose(inverseModel);
            var elapsedSeconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
            var irradianceSettings = _activeRenderScene.IrradianceSettings;

            var uniform = new ManaWebGpuSceneCaptureUniform
            {
                ModelProjection = modelProjection,
                Model = model,
                NormalMatrix = normalMatrix,
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
                LightPositions0 = _activeRenderScene.LightPositions[0],
                LightPositions1 = _activeRenderScene.LightPositions[1],
                LightPositions2 = _activeRenderScene.LightPositions[2],
                LightPositions3 = _activeRenderScene.LightPositions[3],
                LightColors0 = _activeRenderScene.LightColors[0],
                LightColors1 = _activeRenderScene.LightColors[1],
                LightColors2 = _activeRenderScene.LightColors[2],
                LightColors3 = _activeRenderScene.LightColors[3],
                Time = elapsedSeconds,
                WebGpuDebugMode = meshInstance.PbrDebugMode ?? irradianceSettings.WebGpuDebugMode,
                _padding1 = 0.0f,
                _padding2 = 0.0f
            };

            _api.QueueWriteBuffer(_queue, uniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuSceneCaptureUniform));
        }
    }
}
