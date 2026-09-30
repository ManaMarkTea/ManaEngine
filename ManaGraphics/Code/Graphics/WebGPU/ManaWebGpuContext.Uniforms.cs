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
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;
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
            if (_activeRenderScene == null || _frameUniformBuffer == null)
            {
                return;
            }

            var camera = _activeRenderScene.Camera;
            var irradianceSettings = _activeRenderScene.IrradianceSettings;
            var viewProjection = camera.View * GetAspectCorrectedProjection(camera);
            var elapsedSeconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds;

            var uniform = new ManaWebGpuFrameUniform
            {
                ViewProjection = viewProjection,
                CameraPosition = new Vector4(camera.Position, 1.0f),
                IrradianceFactors = new Vector4(
                    Math.Clamp(irradianceSettings.Exposure, 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.IblIntensity * _activeRenderScene.Sky.Daylight, 0.0f, 8.0f),
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
                CascadeViewProjection0 = _cascadeViewProjections[0],
                CascadeViewProjection1 = _cascadeViewProjections[1],
                CascadeViewProjection2 = _cascadeViewProjections[2],
                CascadeSplitFar = new Vector4(_cascadeSplitFar[0], _cascadeSplitFar[1], _cascadeSplitFar[2], 0.0f),
                SunDirection = new Vector4(
                    _activeRenderScene.SunDirection,
                    _activeRenderScene.HasSun && _activeRenderScene.SunCastsShadows ? 1.0f : 0.0f),
                SunColor = _activeRenderScene.HasSun
                    ? _activeRenderScene.SunColor
                    : Vector4.Zero
            };

            for (var i = 0; i < ManaRenderScene.MaxLocalLights; i++)
            {
                uniform.LocalLightPositions[i] = _activeRenderScene.LocalLightPositions[i];
                uniform.LocalLightColors[i] = _activeRenderScene.LocalLightColors[i];
            }

            _api.QueueWriteBuffer(_queue, _frameUniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuFrameUniform));
        }

        // Pure function, no GPU call -- lets callers dirty-check the result against the mesh's last
        // uploaded value before spending a QueueWriteBuffer on it (see
        // ManaWebGpuSceneMesh.TryUpdateObjectUniform in ManaWebGpuContext.Scene.cs).
        private static ManaWebGpuObjectUniform BuildObjectUniform(ManaRenderMeshInstance meshInstance)
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
                    0.0f),
                // -1 means "no per-object override, use the frame's global debug mode" -- valid debug
                // mode indexes are >= 0 (see ManaIrradianceSettings.WebGpuDebugMode).
                DebugModeOverride = meshInstance.PbrDebugMode ?? -1.0f,
                FlatShading = meshInstance.FlatShaded ? 1.0f : 0.0f
            };
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

        // Written once per frame -- group 0, binding 0. Identical for every mesh drawn this frame.
        [StructLayout(LayoutKind.Sequential)]
        private struct ManaWebGpuFrameUniform
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
            public LocalLightArray LocalLightPositions;
            public LocalLightArray LocalLightColors;
        }

        [System.Runtime.CompilerServices.InlineArray(ManaRenderScene.MaxLocalLights)]
        private struct LocalLightArray
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
        private struct ManaWebGpuSceneCaptureUniform
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
