using System.Linq;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Buffers.Binary;
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
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void UpdateDemoUniforms(ManaRenderMeshInstance meshInstance, WebGpuBuffer* uniformBuffer)
        {
            if (_activeRenderScene == null)
            {
                return;
            }

            UpdateDemoUniforms(meshInstance, uniformBuffer, _activeRenderScene.Camera);
        }

        private void UpdateDemoUniforms(ManaRenderMeshInstance meshInstance, WebGpuBuffer* uniformBuffer, ManaCameraData camera)
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
                _padding2 = 0.0f,
                OutlineColor = meshInstance.OutlineColor,
                OutlineParameters = new Vector4(
                    Math.Clamp(meshInstance.OutlineWidth, 1.0f, 4.0f),
                    meshInstance.OutlineEnabled ? 1.0f : 0.0f,
                    0.0f,
                    0.0f)
            };

            _api.QueueWriteBuffer(_queue, uniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuTransformUniform));
        }

        // Generic per-material uniform block, shared by every WGSL material shader (group 1). Any
        // shader can opt in just by declaring a matching struct + @group(1)@binding(0) in its own
        // .wgsl file — nothing here is specific to any one shader, so adding tunable parameters to a
        // new shader never requires touching this C# renderer code.
        //
        // Packing convention: each field of the shader's own "CustomMaterialUniform" struct is named
        // after the ManaMaterial.SetShaderUniform(name, value) it holds, e.g.
        // "sphereRadius : vec4<f32>, // slot 0" (the "// slot N" comment is just a human-readable
        // annotation — the real slot index is the field's declaration order, parsed straight out of
        // the .wgsl source by ParseCustomUniformSlotNames). So the mapping lives in exactly one
        // place — the shader file itself — instead of being hand-mirrored between C# and WGSL.
        private readonly Dictionary<string, ManaWebGpuCustomUniformSchema> _customUniformLayouts = new();
        private static readonly HashSet<string> _warnedUnmappedCustomUniforms = new();

        private void BuildCustomMaterialUniformData(
            ManaRenderMeshInstance instance,
            ManaWebGpuCustomUniformSchema layout,
            Span<byte> destination)
        {
            destination.Clear();
            if (instance.CustomShaderUniforms == null || instance.CustomShaderUniforms.Count == 0)
            {
                return;
            }

            foreach (var field in layout.Fields)
            {
                if (!instance.CustomShaderUniforms.TryGetValue(field.Name, out var value))
                {
                    continue;
                }

                WriteCustomUniformField(destination, field, value);
            }

            WarnAboutUnmappedCustomUniforms(instance, layout);
        }

        private static void WriteCustomUniformField(Span<byte> destination, ManaCustomUniformFieldDefinition field, Vector4 value)
        {
            for (var i = 0; i < field.ComponentCount; i++)
            {
                var componentOffset = field.Offset + (i * sizeof(float));
                var componentValue = GetVector4Component(value, i);
                var componentSpan = destination.Slice(componentOffset, sizeof(float));

                switch (field.ElementKind)
                {
                    case ManaCustomUniformElementKind.Float:
                        BinaryPrimitives.WriteSingleLittleEndian(componentSpan, componentValue);
                        break;
                    case ManaCustomUniformElementKind.SignedInt:
                        BinaryPrimitives.WriteInt32LittleEndian(componentSpan, (int)MathF.Round(componentValue));
                        break;
                    case ManaCustomUniformElementKind.UnsignedInt:
                        BinaryPrimitives.WriteUInt32LittleEndian(componentSpan, componentValue <= 0.0f ? 0u : (uint)MathF.Round(componentValue));
                        break;
                    case ManaCustomUniformElementKind.Bool:
                        BinaryPrimitives.WriteUInt32LittleEndian(componentSpan, componentValue > 0.5f ? 1u : 0u);
                        break;
                }
            }
        }

        private static float GetVector4Component(Vector4 value, int index)
        {
            return index switch
            {
                0 => value.X,
                1 => value.Y,
                2 => value.Z,
                3 => value.W,
                _ => 0.0f
            };
        }

        private static void WarnAboutUnmappedCustomUniforms(ManaRenderMeshInstance instance, ManaWebGpuCustomUniformSchema layout)
        {
            if (instance.CustomShaderUniforms == null)
            {
                return;
            }

            foreach (var name in instance.CustomShaderUniforms.Keys)
            {
                if (layout.FieldNames.Contains(name))
                {
                    continue;
                }

                var key = $"{instance.ShaderName}:{name}";
                if (_warnedUnmappedCustomUniforms.Add(key))
                {
                    ManaConsole.WriteDiagnostic(
                        $"[WebGPU] Material uniform '{name}' has no matching slot comment in the " +
                        $"'{instance.ShaderName}' .wgsl shader's CustomMaterialUniform struct — ignored on this backend.");
                }
            }
        }

        private ManaWebGpuCustomUniformSchema GetCustomUniformLayout(string? shaderName)
        {
            shaderName ??= "ManaPbrShader";
            if (_customUniformLayouts.TryGetValue(shaderName, out var cached))
            {
                return cached;
            }

            var layout = ManaWebGpuCustomUniformSchemaCache.GetSchema(shaderName);

            _customUniformLayouts[shaderName] = layout;
            return layout;
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
            public Vector4 OutlineColor;
            public Vector4 OutlineParameters;
        }
    }
}
