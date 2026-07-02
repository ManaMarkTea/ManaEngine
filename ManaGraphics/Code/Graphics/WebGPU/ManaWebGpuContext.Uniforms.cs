using System.Linq;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
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
        private const int CustomUniformSlotCount = 12;

        private static readonly Regex CustomUniformStructRegex = new(
            @"struct\s+CustomMaterialUniform\s*\{(?<body>.*?)\}\s*;",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex CustomUniformFieldRegex = new(
            @"^\s*(?<name>\w+)\s*:\s*vec4<f32>\s*,?\s*(?://\s*slot\s*(?<slot>\d+))?",
            RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Dictionary<string, IReadOnlyList<string?>> _customUniformSlotNames = new();
        private static readonly HashSet<string> _warnedUnmappedCustomUniforms = new();

        [StructLayout(LayoutKind.Sequential)]
        private struct ManaWebGpuCustomMaterialUniform
        {
            public Vector4 Slot0;
            public Vector4 Slot1;
            public Vector4 Slot2;
            public Vector4 Slot3;
            public Vector4 Slot4;
            public Vector4 Slot5;
            public Vector4 Slot6;
            public Vector4 Slot7;
            public Vector4 Slot8;
            public Vector4 Slot9;
            public Vector4 Slot10;
            public Vector4 Slot11;
        }

        private ManaWebGpuCustomMaterialUniform BuildCustomMaterialUniform(ManaRenderMeshInstance instance)
        {
            var uniform = new ManaWebGpuCustomMaterialUniform();
            if (instance.CustomShaderUniforms == null || instance.CustomShaderUniforms.Count == 0)
            {
                return uniform;
            }

            var slotNames = GetCustomUniformSlotNames(instance.ShaderName);
            for (var slotIndex = 0; slotIndex < slotNames.Count && slotIndex < CustomUniformSlotCount; slotIndex++)
            {
                var slotName = slotNames[slotIndex];
                if (slotName == null || !instance.CustomShaderUniforms.TryGetValue(slotName, out var value))
                {
                    continue;
                }

                SetCustomUniformSlot(ref uniform, slotIndex, value);
            }

            WarnAboutUnmappedCustomUniforms(instance, slotNames);
            return uniform;
        }

        private static void WarnAboutUnmappedCustomUniforms(ManaRenderMeshInstance instance, IReadOnlyList<string?> slotNames)
        {
            if (instance.CustomShaderUniforms == null)
            {
                return;
            }

            foreach (var name in instance.CustomShaderUniforms.Keys)
            {
                if (slotNames.Contains(name))
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

        // Reads the .wgsl file for shaderName and extracts its CustomMaterialUniform field names, in
        // declaration order, from trailing "// name" comments. Cached per shader name; a shader with
        // no such struct (or that fails to load) just gets an empty list.
        private IReadOnlyList<string?> GetCustomUniformSlotNames(string? shaderName)
        {
            shaderName ??= "ManaPbrShader";
            if (_customUniformSlotNames.TryGetValue(shaderName, out var cached))
            {
                return cached;
            }

            IReadOnlyList<string?> slotNames = Array.Empty<string?>();
            try
            {
                var shaderFile = ResolveShaderFileName(shaderName);
                var path = ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", shaderFile));
                var source = File.ReadAllText(path);
                slotNames = ParseCustomUniformSlotNames(shaderName, source);
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Failed to parse custom uniform slots for '{shaderName}': {ex.Message}");
            }

            _customUniformSlotNames[shaderName] = slotNames;
            return slotNames;
        }

        private static IReadOnlyList<string?> ParseCustomUniformSlotNames(string shaderName, string wgslSource)
        {
            var structMatch = CustomUniformStructRegex.Match(wgslSource);
            if (!structMatch.Success)
            {
                return Array.Empty<string?>();
            }

            var slotNames = new List<string?>();
            var index = 0;
            foreach (Match fieldMatch in CustomUniformFieldRegex.Matches(structMatch.Groups["body"].Value))
            {
                var name = fieldMatch.Groups["name"].Value;
                slotNames.Add(name);

                // The "// slot N" comment is just a human-readable annotation — declaration order is
                // the real, authoritative slot index. Flag it if someone lets the two drift apart.
                if (fieldMatch.Groups["slot"].Success &&
                    int.TryParse(fieldMatch.Groups["slot"].Value, out var declaredSlot) &&
                    declaredSlot != index)
                {
                    ManaConsole.WriteDiagnostic(
                        $"[WebGPU] '{shaderName}' CustomMaterialUniform field '{name}' is commented as " +
                        $"slot {declaredSlot} but is actually declaration position {index} — using {index}.");
                }

                index++;
            }

            return slotNames;
        }

        private static void SetCustomUniformSlot(ref ManaWebGpuCustomMaterialUniform uniform, int slotIndex, Vector4 value)
        {
            switch (slotIndex)
            {
                case 0: uniform.Slot0 = value; break;
                case 1: uniform.Slot1 = value; break;
                case 2: uniform.Slot2 = value; break;
                case 3: uniform.Slot3 = value; break;
                case 4: uniform.Slot4 = value; break;
                case 5: uniform.Slot5 = value; break;
                case 6: uniform.Slot6 = value; break;
                case 7: uniform.Slot7 = value; break;
                case 8: uniform.Slot8 = value; break;
                case 9: uniform.Slot9 = value; break;
                case 10: uniform.Slot10 = value; break;
                case 11: uniform.Slot11 = value; break;
            }
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
