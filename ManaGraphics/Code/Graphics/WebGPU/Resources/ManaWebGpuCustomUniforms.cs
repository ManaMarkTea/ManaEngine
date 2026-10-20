using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;

namespace ManaGraphics.Code.Graphics.Resources
{
    internal static class ManaWebGpuCustomUniforms
    {
        // Per-material custom uniforms, for any shader that declares a CustomMaterialUniform struct. Fields are packed in
        // declaration order, parsed from the .wgsl, and named after ManaMaterial.SetShaderUniform keys, so the shader file
        // is the only place the mapping lives.
        private static readonly ConcurrentDictionary<string, ManaWebGpuCustomUniformSchema> Layouts = new();
        private static readonly ConcurrentDictionary<string, bool> WarnedUnmapped = new();

        public static void Fill(
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
                if (WarnedUnmapped.TryAdd(key, true))
                {
                    ManaConsole.WriteDiagnostic(
                        $"[WebGPU] Material uniform '{name}' has no matching slot comment in the " +
                        $"'{instance.ShaderName}' .wgsl shader's CustomMaterialUniform struct — ignored on this backend.");
                }
            }
        }

        public static ManaWebGpuCustomUniformSchema Layout(string? shaderName)
        {
            return Layouts.GetOrAdd(shaderName ?? "ManaPbrShader", ManaWebGpuCustomUniformSchemaCache.GetSchema);
        }
    }
}
