using Mana.Code.Nodes;
using Silk.NET.OpenGL;

namespace Mana.Code.Materials
{
    public class ManaMaterial : ManaObject, ICloneable
    {
        private static Dictionary<ManaContext, ManaMaterial> DefaultMaterial = new Dictionary<ManaContext, ManaMaterial>();

        public static ManaMaterial GetDefaultMaterial(ManaContext context)
        {
            if (!DefaultMaterial.ContainsKey(context))
            {
                DefaultMaterial.Add(context, new ManaMaterial(context)
                {
                    Name = "Default PBR Material"
                });
            }

            return DefaultMaterial[context];
        }

        public ManaShader? Shader { get; set; }

        public ManaTextureVector4 Albedo;
        public ManaTextureVector4 Normal;
        public ManaTextureVector4 Emission;
        public ManaTextureSingle Metallic;
        public ManaTextureSingle Roughness;
        public ManaTextureSingle Ambient;
        public Dictionary<string, ManaMaterialUniformValue> ShaderUniforms { get; set; }

        // Snapshot of the value each named uniform had the first time it was set. "Reset" in the
        // Inspector restores from here rather than removing the key outright — some render paths
        // (e.g. WebGPU's generic per-material uniform packing) assign buffer slots by sorting
        // ShaderUniforms' key set, so removing a key would silently shift every other uniform's slot.
        public Dictionary<string, ManaMaterialUniformValue> DefaultShaderUniforms { get; private set; }

        public double NormalSelector = 1.0;
        public double EmissionStrength = 0.0;

        public ManaMaterial(ManaContext context) : base(context)
        {
            Name = "PBR Material";
            Channels = new Dictionary<string, ManaMaterialChannel>();
            ShaderUniforms = new Dictionary<string, ManaMaterialUniformValue>();
            DefaultShaderUniforms = new Dictionary<string, ManaMaterialUniformValue>();
            if (context.OpenGl != null && !context.DeferGpuUploads)
            {
                var shader = new ManaPbrShader(context);
                Shader = shader;
            }

            Random r = new Random();            

            Albedo = new ManaTextureVector4(Context, Colors.Orange);
            Normal = new ManaTextureVector4(Context, new Vector4((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble(), 1.0f));
            Emission = new ManaTextureVector4(Context, Colors.Cyan);
            Metallic = new ManaTextureSingle(Context, 0);
            Roughness = new ManaTextureSingle(Context, 1);
            Ambient = new ManaTextureSingle(Context, 1.0);
        }

        public Dictionary<string, ManaMaterialChannel> Channels { get; set; }

        public void SetShaderUniform(string name, float value)
        {
            SetShaderUniformValue(name, new ManaMaterialUniformValue
            {
                Type = UniformType.Float,
                FloatValue = value
            });
        }

        public void SetShaderUniform(string name, int value)
        {
            SetShaderUniformValue(name, new ManaMaterialUniformValue
            {
                Type = UniformType.Int,
                IntValue = value
            });
        }

        public void SetShaderUniform(string name, bool value)
        {
            SetShaderUniformValue(name, new ManaMaterialUniformValue
            {
                Type = UniformType.Bool,
                BoolValue = value
            });
        }

        public void SetShaderUniform(string name, Vector2 value)
        {
            SetShaderUniformValue(name, new ManaMaterialUniformValue
            {
                Type = UniformType.FloatVec2,
                Vector2Value = value
            });
        }

        public void SetShaderUniform(string name, Vector3 value)
        {
            SetShaderUniformValue(name, new ManaMaterialUniformValue
            {
                Type = UniformType.FloatVec3,
                Vector3Value = value
            });
        }

        public void SetShaderUniform(string name, Vector4 value)
        {
            SetShaderUniformValue(name, new ManaMaterialUniformValue
            {
                Type = UniformType.FloatVec4,
                Vector4Value = value
            });
        }

        // The first call for a given name establishes its "default" (what Inspector resets restore).
        // Later calls (e.g. re-loading, or the Inspector editor itself) only update the live value.
        private void SetShaderUniformValue(string name, ManaMaterialUniformValue value)
        {
            ShaderUniforms[name] = value;
            if (!DefaultShaderUniforms.ContainsKey(name))
            {
                DefaultShaderUniforms[name] = value.Clone();
            }
        }


        public void Bind(Matrix4x4 objectMatrix, ManaSceneNode? owner = null)
        {
            int i = 0;

            foreach (var texture in Channels)
            {
                if (texture.Value.Texture != null)
                {
                   texture.Value.Texture.Bind(i++);
                }
            }

            Shader?.BindShader(this, objectMatrix, Context.World.MainCamera, owner);
        }

        public void UnBind()
        {
            int i = 0;
            foreach (var texture in Channels)
            {
                if (texture.Value.Texture != null)
                {
                    texture.Value.Texture.UnBind(i++);
                }
            }

            Shader?.UnbindShader();
        }

        public object Clone()
        {
            var copy = MemberwiseClone() as ManaMaterial;
            if (copy == null)
            {
                return this;
            }

            copy.Albedo = (ManaTextureVector4)copy.Albedo.Clone();
            copy.Normal = (ManaTextureVector4)copy.Normal.Clone();
            copy.Emission = (ManaTextureVector4)copy.Emission.Clone();
            copy.Metallic = (ManaTextureSingle)copy.Metallic.Clone();
            copy.Roughness = (ManaTextureSingle)copy.Roughness.Clone();
            copy.Ambient = (ManaTextureSingle)copy.Ambient.Clone();
            copy.ShaderUniforms = copy.ShaderUniforms.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Clone());
            copy.DefaultShaderUniforms = copy.DefaultShaderUniforms.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Clone());

            copy.Albedo.Load();
            copy.Normal.Load();
            copy.Emission.Load();
            copy.Metallic.Load();
            copy.Roughness.Load();
            copy.Ambient.Load();

            return copy;
        }
    }
}
