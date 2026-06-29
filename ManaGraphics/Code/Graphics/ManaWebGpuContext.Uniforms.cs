using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void UpdateDemoUniforms(ManaRenderMeshInstance meshInstance, Silk.NET.WebGPU.Buffer* uniformBuffer)
        {
            if (_activeRenderScene == null)
            {
                return;
            }

            UpdateDemoUniforms(meshInstance, uniformBuffer, _activeRenderScene.Camera);
        }

        private void UpdateDemoUniforms(ManaRenderMeshInstance meshInstance, Silk.NET.WebGPU.Buffer* uniformBuffer, ManaCameraData camera)
        {
            if (uniformBuffer == null || _activeRenderScene == null)
            {
                return;
            }

            var model = meshInstance.WorldTransform;
            var view = camera.View;
            var projection = GetAspectCorrectedProjection(camera);
            var modelProjection = model * view * projection;
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }
            var normalMatrix = Matrix4x4.Transpose(inverseModel);
            var elapsedSeconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
            var irradianceSettings = _activeRenderScene.IrradianceSettings;
            var lightDirection = irradianceSettings.LightDirection.LengthSquared() > 0.0001f
                ? Vector3.Normalize(irradianceSettings.LightDirection)
                : Vector3.Normalize(new Vector3(0.35f, 0.82f, 0.45f));
            
            var uniform = new ManaWebGpuTransformUniform
            {
                ModelProjection = modelProjection,
                Model = model,
                NormalMatrix = normalMatrix,
                CameraPosition = new Vector4(camera.Position, 1.0f),
                MaterialAlbedo = meshInstance.Albedo,
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

            _api.QueueWriteBuffer(_queue, uniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuTransformUniform));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ManaWebGpuTransformUniform
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
}
