using System.Text.RegularExpressions;
using System.Numerics;

namespace ManaCore.Graphics
{
    public enum ManaCustomUniformElementKind
    {
        Float,
        SignedInt,
        UnsignedInt,
        Bool
    }

    public readonly record struct ManaCustomUniformFieldDefinition(
        string Name,
        string TypeText,
        int Offset,
        int ComponentCount,
        ManaCustomUniformElementKind ElementKind,
        int DeclarationIndex,
        float? DefaultScalar,
        Vector4? DefaultVector,
        float? SoftMin,
        float? SoftMax);

    public sealed class ManaWebGpuCustomUniformSchema
    {
        public static readonly ManaWebGpuCustomUniformSchema Empty = new(Array.Empty<ManaCustomUniformFieldDefinition>(), 16);

        public ManaWebGpuCustomUniformSchema(IReadOnlyList<ManaCustomUniformFieldDefinition> fields, int bufferSize)
        {
            Fields = fields;
            BufferSize = Math.Max(bufferSize, 16);
            FieldNames = fields
                .Select(field => field.Name)
                .ToHashSet(StringComparer.Ordinal);
            FieldMap = fields.ToDictionary(field => field.Name, field => field, StringComparer.Ordinal);
        }

        public IReadOnlyList<ManaCustomUniformFieldDefinition> Fields { get; }

        public HashSet<string> FieldNames { get; }

        public IReadOnlyDictionary<string, ManaCustomUniformFieldDefinition> FieldMap { get; }

        public int BufferSize { get; }
    }

    public static class ManaWebGpuCustomUniformSchemaCache
    {
        private static readonly Regex CustomUniformStructRegex = new(
            @"struct\s+CustomMaterialUniform\s*\{(?<body>.*?)\}\s*;",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex CustomUniformFieldRegex = new(
            @"^\s*(?<name>\w+)\s*:\s*(?<type>[^,\r\n/]+)\s*,?\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PropertyAnnotationRegex = new(
            @"^\s*//\s*PROPERTY\s*\((?<args>.*)\)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PropertyMinRegex = new(
            @"\bmin\s*=\s*(?<value>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PropertyMaxRegex = new(
            @"\bmax\s*=\s*(?<value>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PropertyDefaultRegex = new(
            @"\bdefault\s*=\s*(?<value>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PropertyDefaultArrayRegex = new(
            @"\bdefault\s*=\s*\[(?<values>[^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, ManaWebGpuCustomUniformSchema> Schemas = new(StringComparer.Ordinal);

        private static readonly object SchemaLock = new();

        public static ManaWebGpuCustomUniformSchema GetSchema(string? shaderName)
        {
            shaderName ??= "ManaPbrShader";
            lock (SchemaLock)
            {
                if (Schemas.TryGetValue(shaderName, out var cached))
                {
                    return cached;
                }

                var schema = LoadSchema(shaderName);
                Schemas[shaderName] = schema;
                return schema;
            }
        }

        private static ManaWebGpuCustomUniformSchema LoadSchema(string shaderName)
        {
            if (!TryResolveShaderFileName(shaderName, out var shaderFileName))
            {
                return ManaWebGpuCustomUniformSchema.Empty;
            }

            var shaderPath = ResolveWebGpuShaderPath(shaderFileName);
            if (shaderPath == null)
            {
                return ManaWebGpuCustomUniformSchema.Empty;
            }

            try
            {
                var source = File.ReadAllText(shaderPath);
                return ParseSchema(source);
            }
            catch
            {
                return ManaWebGpuCustomUniformSchema.Empty;
            }
        }

        private static ManaWebGpuCustomUniformSchema ParseSchema(string wgslSource)
        {
            var structMatch = CustomUniformStructRegex.Match(wgslSource);
            if (!structMatch.Success)
            {
                return ManaWebGpuCustomUniformSchema.Empty;
            }

            var fields = new List<ManaCustomUniformFieldDefinition>();
            var declarationIndex = 0;
            var offset = 0;
            var structAlignment = 1;
            string? pendingPropertyArgs = null;

            foreach (var line in structMatch.Groups["body"].Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var propertyMatch = PropertyAnnotationRegex.Match(line);
                if (propertyMatch.Success)
                {
                    pendingPropertyArgs = propertyMatch.Groups["args"].Value;
                    continue;
                }

                var fieldMatch = CustomUniformFieldRegex.Match(line);
                if (!fieldMatch.Success)
                {
                    continue;
                }

                var name = fieldMatch.Groups["name"].Value;
                var typeText = fieldMatch.Groups["type"].Value.Trim();
                if (!TryParseFieldType(typeText, out var componentCount, out var elementKind, out var alignment, out var size))
                {
                    declarationIndex++;
                    continue;
                }

                offset = Align(offset, alignment);
                TryParseDefaultScalar(pendingPropertyArgs, out var defaultScalar);
                TryParseDefaultVector(pendingPropertyArgs, out var defaultVector);
                TryParseSoftRange(pendingPropertyArgs, out var softMin, out var softMax);
                pendingPropertyArgs = null;

                fields.Add(new ManaCustomUniformFieldDefinition(
                    name,
                    typeText,
                    offset,
                    componentCount,
                    elementKind,
                    declarationIndex,
                    defaultScalar,
                    defaultVector,
                    softMin,
                    softMax));
                offset += size;
                structAlignment = Math.Max(structAlignment, alignment);
                declarationIndex++;
            }

            if (fields.Count == 0)
            {
                return ManaWebGpuCustomUniformSchema.Empty;
            }

            var bufferSize = Align(offset, structAlignment);
            return new ManaWebGpuCustomUniformSchema(fields, bufferSize);
        }

        private static bool TryParseFieldType(
            string typeText,
            out int componentCount,
            out ManaCustomUniformElementKind elementKind,
            out int alignment,
            out int size)
        {
            componentCount = 0;
            elementKind = ManaCustomUniformElementKind.Float;
            alignment = 0;
            size = 0;

            if (TryParseScalarType(typeText, out elementKind))
            {
                componentCount = 1;
                alignment = sizeof(float);
                size = sizeof(float);
                return true;
            }

            var vectorMatch = Regex.Match(typeText, @"^vec(?<components>[2-4])\s*<\s*(?<element>f32|i32|u32|bool)\s*>$", RegexOptions.IgnoreCase);
            if (!vectorMatch.Success)
            {
                return false;
            }

            componentCount = int.Parse(vectorMatch.Groups["components"].Value);
            if (!TryParseScalarType(vectorMatch.Groups["element"].Value, out elementKind))
            {
                return false;
            }

            size = componentCount * sizeof(float);
            alignment = componentCount switch
            {
                2 => 2 * sizeof(float),
                3 => 4 * sizeof(float),
                4 => 4 * sizeof(float),
                _ => sizeof(float)
            };

            return true;
        }

        private static bool TryParseScalarType(string typeText, out ManaCustomUniformElementKind elementKind)
        {
            switch (typeText.Trim())
            {
                case "f32":
                    elementKind = ManaCustomUniformElementKind.Float;
                    return true;
                case "i32":
                    elementKind = ManaCustomUniformElementKind.SignedInt;
                    return true;
                case "u32":
                    elementKind = ManaCustomUniformElementKind.UnsignedInt;
                    return true;
                case "bool":
                    elementKind = ManaCustomUniformElementKind.Bool;
                    return true;
                default:
                    elementKind = ManaCustomUniformElementKind.Float;
                    return false;
            }
        }

        private static int Align(int value, int alignment)
        {
            if (alignment <= 1)
            {
                return value;
            }

            var remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }

        private static void TryParseSoftRange(string? propertyArgs, out float? softMin, out float? softMax)
        {
            softMin = null;
            softMax = null;

            if (string.IsNullOrWhiteSpace(propertyArgs))
            {
                return;
            }

            var minMatch = PropertyMinRegex.Match(propertyArgs);
            if (minMatch.Success &&
                float.TryParse(minMatch.Groups["value"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMin))
            {
                softMin = parsedMin;
            }

            var maxMatch = PropertyMaxRegex.Match(propertyArgs);
            if (maxMatch.Success &&
                float.TryParse(maxMatch.Groups["value"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMax))
            {
                softMax = parsedMax;
            }
        }

        private static void TryParseDefaultScalar(string? propertyArgs, out float? defaultScalar)
        {
            defaultScalar = null;
            if (string.IsNullOrWhiteSpace(propertyArgs))
            {
                return;
            }

            var defaultMatch = PropertyDefaultRegex.Match(propertyArgs);
            if (defaultMatch.Success &&
                float.TryParse(defaultMatch.Groups["value"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedDefault))
            {
                defaultScalar = parsedDefault;
            }
        }

        private static void TryParseDefaultVector(string? propertyArgs, out Vector4? defaultVector)
        {
            defaultVector = null;
            if (string.IsNullOrWhiteSpace(propertyArgs))
            {
                return;
            }

            var arrayMatch = PropertyDefaultArrayRegex.Match(propertyArgs);
            if (!arrayMatch.Success)
            {
                return;
            }

            var parts = arrayMatch.Groups["values"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return;
            }

            Span<float> values = stackalloc float[4];
            for (var i = 0; i < Math.Min(parts.Length, 4); i++)
            {
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                {
                    return;
                }
            }

            defaultVector = new Vector4(values[0], values[1], values[2], values[3]);
        }

        private static bool TryResolveShaderFileName(string shaderName, out string shaderFileName)
        {
            shaderFileName = shaderName switch
            {
                "ManaLightningBallShader" => "lightning_ball.wgsl",
                "ManaLiquidBallShader" => "liquid_ball.wgsl",
                "ManaLiquidShader" => "liquid.wgsl",
                "ManaGridShader" => "grid.wgsl",
                "ManaEnvironmentIrradianceShader" => "environment_irradiance.wgsl",
                "ManaPbrShader" => "pbr_irradiance.wgsl",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(shaderFileName);
        }

        private static string? ResolveWebGpuShaderPath(string shaderFileName)
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, "Assets", "Shaders", "WebGpu", shaderFileName),
                Path.Combine(Environment.CurrentDirectory, "Mana", "Assets", "Shaders", "WebGpu", shaderFileName),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", "Assets", "Shaders", "WebGpu", shaderFileName),
                Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "WebGpu", shaderFileName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", "Assets", "Shaders", "WebGpu", shaderFileName)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }
    }
}
