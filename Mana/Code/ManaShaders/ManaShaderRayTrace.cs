using ImGuiNET;
using Mana.Code.Nodes;

namespace Mana.Code.ManaShaders
{
    public class ManaShaderRayTrace : ManaShader
    {
        public ManaShaderRayTrace(ManaContext context) : base(context)
        {
            context.World.PostDrawFutures.Add(() => OnGui());
        }

        void OnGui()
        {
            ImGui.Begin("RayTracer");

            ImGui.InputInt("Rays", ref RayCasts);
            ImGui.InputInt("Bounces", ref MaxBounces);

            ImGui.End();

            Context.World.PostDrawFutures.Add(() => OnGui());
        }

        public int RayCasts = 1;
        public int MaxBounces = 5;

        public bool SetSphereUniforms(int index, ManaSceneNode sphereNode)
        {
            var renderer = sphereNode.Components[0] as MeshRendererComponent;
            if (renderer == null)
            {
                return false;
            }

            ManaSphere? sphere = renderer?.Mesh as ManaSphere;
            if (sphere == null)
            {
                return false;
            }

            CompiledShader.UniformMap[$@"spheres[{index}].position"].SetValue(sphereNode.Position);
            CompiledShader.UniformMap[$@"spheres[{index}].material.color"].SetValue(sphere.DefaultVertexColor);

            if (renderer.Material.EmissionStrength == 0)
            {
                CompiledShader.UniformMap[$@"spheres[{index}].material.emission"].SetValue(sphere.DefaultVertexColor);
            }
            else
            {
                CompiledShader.UniformMap[$@"spheres[{index}].material.emission"].SetValue(renderer.Material.Emission.Value);
            }

            CompiledShader.UniformMap[$@"spheres[{index}].material.emissionStrength"].SetValue(renderer.Material.EmissionStrength);
            CompiledShader.UniformMap[$@"spheres[{index}].material.smoothness"].SetValue(renderer.Material.Roughness.Value);
            CompiledShader.UniformMap[$@"spheres[{index}].material.specularColor"].SetValue(Colors.White);
            CompiledShader.UniformMap[$@"spheres[{index}].material.specularColorScatterChance"].SetValue(renderer.Material.Ambient.Value);
            CompiledShader.UniformMap[$@"spheres[{index}].radius"].SetValue(sphereNode.Scale.X * 0.25f);
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
