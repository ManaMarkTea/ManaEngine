using Mana.Code.Materials;
using Mana.Code.Nodes;

namespace Mana.Code.ManaShaders
{
    public class ManaShaderRayTrace : ManaShader
    {
        public ManaShaderRayTrace(ManaContext context) : base(context)
        {
        }

        // Rays and Bounces used to be editable from a settings window this shader pushed onto the world's
        // per-frame UI list. Nothing replaced it: ray tracing is experimental and off by default, so the
        // two fields are set in code until something in the ManaUI inspector wants them.

        public int RayCasts = 1;
        public int MaxBounces = 5;

        public bool SetSphereUniforms(int index, ManaSceneNode sphereNode)
        {
            if (sphereNode.Components[0] is not MeshRendererComponent renderer)
            {
                return false;
            }

            if (renderer.Mesh is not ManaSphere sphere)
            {
                return false;
            }

            var uniforms = CompiledShader.UniformMap;
            if (uniforms == null)
            {
                return false;
            }

            static void SetUniform(IDictionary<string, ManaShaderUniform> map, string key, Action<ManaShaderUniform> apply)
            {
                if (map.TryGetValue(key, out var uniform) && uniform != null)
                {
                    apply(uniform);
                }
            }

            var material = renderer.Material;

            SetUniform(uniforms, $@"spheres[{index}].position", uniform => uniform.SetValue(sphereNode.Position));
            SetUniform(uniforms, $@"spheres[{index}].material.color", uniform => uniform.SetValue(sphere.DefaultVertexColor));

            if (material.EmissionStrength == 0)
            {
                SetUniform(uniforms, $@"spheres[{index}].material.emission", uniform => uniform.SetValue(sphere.DefaultVertexColor));
            }
            else
            {
                var emissionColor = material.Emission?.Value ?? sphere.DefaultVertexColor;
                SetUniform(uniforms, $@"spheres[{index}].material.emission", uniform => uniform.SetValue(emissionColor));
            }

            SetUniform(uniforms, $@"spheres[{index}].material.emissionStrength", uniform => uniform.SetValue(material.EmissionStrength));
            SetUniform(uniforms, $@"spheres[{index}].material.smoothness", uniform => uniform.SetValue(material.Roughness.Value));
            SetUniform(uniforms, $@"spheres[{index}].material.specularColor", uniform => uniform.SetValue(Colors.White));
            SetUniform(uniforms, $@"spheres[{index}].material.specularColorScatterChance", uniform => uniform.SetValue(material.Occlusion.Value));
            SetUniform(uniforms, $@"spheres[{index}].radius", uniform => uniform.SetValue(sphereNode.Scale.X * 0.25f));
            return true;
        }
        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            base.BindShader(material, objectMatrix, camera);

            CompiledShader.UniformMap[$@"RayCasts"].SetValue(RayCasts);
            CompiledShader.UniformMap[$@"MaxBounces"].SetValue(MaxBounces);


            CompiledShader.UniformMap[$@"ViewPosition"].SetValue(camera.Owner.Position);
            CompiledShader.UniformMap[$@"ViewRight"].SetValue(camera.Owner.Right);
            CompiledShader.UniformMap[$@"ViewUp"].SetValue(camera.Owner.Up);
            CompiledShader.UniformMap[$@"ViewForward"].SetValue(camera.Owner.Forward);

            int index = 0;
            foreach (var scene in Context.World.Scenes)
            {
                Stack<ManaSceneNode> stack = new Stack<ManaSceneNode>();

                foreach (var child in scene.Children)
                {
                    stack.Push(child);
                }

                while (stack.Count > 0)
                {
                    var top = stack.Pop();

                    if (SetSphereUniforms(index, top))
                    {
                        index++;
                    }

                    foreach (var child in top.Children)
                    {
                        stack.Push(child);
                    }
                }
            }

            CompiledShader.UniformMap[$@"numSpheres"].SetValue(index);

        }

    }
}
