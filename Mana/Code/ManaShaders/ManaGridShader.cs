using Mana.Code.Materials;
using Mana.Code.Nodes;
using System.Numerics;

namespace Mana.Code
{
    public class ManaGridShader : ManaShader
    {
        static ManaGridShader()
        {
            CacheShader("Assets/Shaders/Grids/grid.vert");
            CacheShader("Assets/Shaders/Grids/grid.frag");
        }

        public ManaGridShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["grid.vert"], ShaderSources["grid.frag"]);
            if (!context.DeferGpuUploads)
            {
                BindShader();
                UnbindShader();
            }
        }

        /// <summary>
        /// Pushes the material's flat colour channels (and UV remap) into the grid's own uniform names.
        /// WebGPU needs no equivalent -- grid.wgsl reads object.material_albedo/material_emission/
        /// uv_transform directly, since every material already uploads those every frame -- but the OpenGL
        /// path has no such generic per-object uniform block, so grid.frag's gridFloor/gridLine/uvTransform
        /// have to be set explicitly here, the same way ManaPbrShader sets albedoColor/uvTransform.
        /// </summary>
        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            base.BindShader(material, objectMatrix, camera, owner);
            FindUniform("gridFloor")?.SetValue(material.Albedo.Value);

            // EmissionStrength carries the line-thickness multiplier (see UiTheme.GridLineThickness), not
            // Emission.Value's own alpha -- WebGPU reads material_emission.a from EmissionStrength via the
            // generic per-object uniform, so packing thickness there too is what keeps both backends
            // reading the same value out of the same gridLine.a / material_emission.a convention.
            var emission = material.Emission.Value;
            FindUniform("gridLine")?.SetValue(new Vector4(emission.X, emission.Y, emission.Z, (float)material.EmissionStrength));
            FindUniform("uvTransform")?.SetValue(new Vector4(
                material.UvScale.X, material.UvScale.Y, material.UvOffset.X, material.UvOffset.Y));
        }
    }
}
