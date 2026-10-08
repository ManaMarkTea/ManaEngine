using Mana.Code.Nodes;
using ManaCore.Graphics;
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
        public ManaTextureSingle Occlusion;

        /// <summary>
        /// How much of the surface is there at all. 1 is solid; a map here cuts holes without changing the
        /// geometry -- leaves, grates, chain link.
        /// </summary>
        public ManaTextureSingle Opacity;

        public Dictionary<string, ManaMaterialUniformValue> ShaderUniforms { get; set; }

        // Snapshot of the value each named uniform had the first time it was set. "Reset" in the
        // Inspector restores from here rather than removing the key outright — some render paths
        // (e.g. WebGPU's generic per-material uniform packing) assign buffer slots by sorting
        // ShaderUniforms' key set, so removing a key would silently shift every other uniform's slot.
        public Dictionary<string, ManaMaterialUniformValue> DefaultShaderUniforms { get; private set; }

        public double NormalSelector = 1.0;
        public double EmissionStrength = 0.0;

        /// <summary>Shade with per-triangle geometric normals instead of interpolated vertex ones.</summary>
        public bool FlatShaded = false;

        /// <summary>Show the colour as drawn, ignoring lights -- flat art like Kenney's 2D sprites.</summary>
        public bool Unlit = false;

        /// <summary>
        /// How many times a texture repeats across the surface's own UVs, per axis.
        /// <para>
        /// A material property rather than mesh data: the same brick texture wants a different repeat on a
        /// wall than on a doorframe, and neither should mean editing the mesh.
        /// </para>
        /// </summary>
        public Vector2 UvScale = Vector2.One;

        /// <summary>Shifts the texture across the surface. Useful for lining up a trim or a decal.</summary>
        public Vector2 UvOffset = Vector2.Zero;

        /// <summary>What the renderer should do with this material's alpha.</summary>
        public ManaOpacityMode OpacityMode = ManaOpacityMode.Auto;

        /// <summary>In Cutout, alpha below this is discarded and anything above is fully solid.</summary>
        public double AlphaCutoff = 0.5;

        /// <summary>
        /// <see cref="OpacityMode"/> with Auto resolved, from what the material is actually asking for.
        /// <para>
        /// A value below one is a fade -- every pixel equally solid -- and only blending can express that.
        /// Cutting out with a uniform alpha is a cliff: nothing happens until the value crosses the
        /// threshold and then the whole surface vanishes at once.
        /// </para>
        /// <para>
        /// A map with the value left at one is a mask, which is what cutting out is for: hard edges, no
        /// sorting, no depth-write cost. Foliage and grates want this, and blending them without ordering
        /// looks worse than not blending at all.
        /// </para>
        /// </summary>
        public ManaOpacityMode EffectiveOpacityMode
        {
            get
            {
                if (OpacityMode != ManaOpacityMode.Auto)
                {
                    return OpacityMode;
                }

                if (Opacity != null && Opacity.Value < 0.999f)
                {
                    return ManaOpacityMode.Blend;
                }

                return Opacity?.TextureValue != null ? ManaOpacityMode.Cutout : ManaOpacityMode.Opaque;
            }
        }

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
            Occlusion = new ManaTextureSingle(Context, 1.0);
            Opacity = new ManaTextureSingle(Context, 1.0);
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

        public void EnsureShaderMetadataDefaults()
        {
            var shaderName = Shader?.GetType().Name;
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return;
            }

            var schema = ManaWebGpuCustomUniformSchemaCache.GetSchema(shaderName);
            if (schema.Fields.Count == 0)
            {
                return;
            }

            foreach (var field in schema.Fields)
            {
                if (!TryMapFieldToUniformType(field, out var uniformType))
                {
                    continue;
                }

                if (!ShaderUniforms.ContainsKey(field.Name))
                {
                    ShaderUniforms[field.Name] = CreateDefaultUniformValue(uniformType, field);
                }

                if (!DefaultShaderUniforms.ContainsKey(field.Name))
                {
                    DefaultShaderUniforms[field.Name] = CreateDefaultUniformValue(uniformType, field);
                }
            }
        }

        private static bool TryMapFieldToUniformType(ManaCustomUniformFieldDefinition field, out UniformType type)
        {
            if (field.ElementKind == ManaCustomUniformElementKind.Bool)
            {
                type = UniformType.Bool;
                return true;
            }

            switch (field.ComponentCount)
            {
                case 1:
                    type = field.ElementKind == ManaCustomUniformElementKind.Float
                        ? UniformType.Float
                        : UniformType.Int;
                    return true;
                case 2:
                    if (field.ElementKind == ManaCustomUniformElementKind.Float)
                    {
                        type = UniformType.FloatVec2;
                        return true;
                    }
                    break;
                case 3:
                    if (field.ElementKind == ManaCustomUniformElementKind.Float)
                    {
                        type = UniformType.FloatVec3;
                        return true;
                    }
                    break;
                case 4:
                    if (field.ElementKind == ManaCustomUniformElementKind.Float)
                    {
                        type = UniformType.FloatVec4;
                        return true;
                    }
                    break;
            }

            type = UniformType.Float;
            return false;
        }

        private static ManaMaterialUniformValue CreateDefaultUniformValue(UniformType type, ManaCustomUniformFieldDefinition field)
        {
            var value = new ManaMaterialUniformValue
            {
                Type = type,
                BoolValue = true,
                FloatValue = 1.0f,
                Vector2Value = Vector2.One,
                Vector3Value = Vector3.One,
                Vector4Value = Vector4.One
            };

            if (field.DefaultScalar.HasValue)
            {
                switch (type)
                {
                    case UniformType.Float:
                        value.FloatValue = field.DefaultScalar.Value;
                        break;
                    case UniformType.Int:
                        value.IntValue = (int)MathF.Round(field.DefaultScalar.Value);
                        break;
                    case UniformType.Bool:
                        value.BoolValue = field.DefaultScalar.Value > 0.5f;
                        break;
                }
            }

            if (field.DefaultVector.HasValue)
            {
                var vector = field.DefaultVector.Value;
                switch (type)
                {
                    case UniformType.FloatVec2:
                        value.Vector2Value = new Vector2(vector.X, vector.Y);
                        break;
                    case UniformType.FloatVec3:
                        value.Vector3Value = new Vector3(vector.X, vector.Y, vector.Z);
                        break;
                    case UniformType.FloatVec4:
                        value.Vector4Value = vector;
                        break;
                }
            }

            return value;
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

            ApplyBlendState();

            Shader?.BindShader(this, objectMatrix, Context.World.MainCamera, owner);
        }

        /// <summary>
        /// Turns on alpha blending for a material that asked for it, and puts the state back afterwards.
        /// <para>
        /// Cutout needs none of this -- it discards in the shader and stays in the opaque pass, which is
        /// the whole reason to prefer it. Blend does: without it the shader's alpha reaches a blend
        /// function of one/zero and is simply thrown away.
        /// </para>
        /// <para>
        /// Depth writes are disabled with it, so a blended surface does not occlude what is behind it.
        /// Draw order is still whatever the scene graph produces, so several overlapping blended surfaces
        /// will not composite correctly -- that needs back-to-front sorting, which the renderer does not
        /// do yet on either backend.
        /// </para>
        /// </summary>
        private void ApplyBlendState()
        {
            var gl = Context.OpenGl;
            if (gl == null)
            {
                return;
            }

            _blendStateApplied = EffectiveOpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive
                || (Shader?.IsTransparent ?? false);

            if (!_blendStateApplied)
            {
                return;
            }

            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.DepthMask(false);
        }

        private bool _blendStateApplied;

        public void UnBind()
        {
            if (_blendStateApplied)
            {
                var gl = Context.OpenGl;
                if (gl != null)
                {
                    gl.DepthMask(true);
                    gl.Disable(EnableCap.Blend);
                }

                _blendStateApplied = false;
            }

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
            copy.FlatShaded = FlatShaded;
            copy.Unlit = Unlit;
            copy.Roughness = (ManaTextureSingle)copy.Roughness.Clone();
            copy.Occlusion = (ManaTextureSingle)copy.Occlusion.Clone();
            copy.Opacity = (ManaTextureSingle)copy.Opacity.Clone();
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
            copy.Occlusion.Load();
            copy.Opacity.Load();

            return copy;
        }
    }
}
