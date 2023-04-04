using Mana.Code.Shapes;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
    public class ManaShaderRayTrace : ManaShader
    {
        public ManaShaderRayTrace(ManaWorld world) : base(world)
        {
        }

        public bool SetSphereUniforms(int index, ManaSceneNode sphereNode)
        {
            var renderer = sphereNode.Components[0] as MeshRendererComponent;
            ManaSphere? sphere = renderer?.Mesh as ManaSphere;

            if (sphere == null)
            {
                return false;
            }

            this.UniformMap[$@"spheres[{index}].position"].SetValue(sphereNode.Position);
            this.UniformMap[$@"spheres[{index}].material.color"].SetValue(sphere.DefaultVertexColor);
            this.UniformMap[$@"spheres[{index}].material.emission"].SetValue(new Vector4(0.5f, 0.5f, 0.5f, 1));
            this.UniformMap[$@"spheres[{index}].material.emissionStrength"].SetValue(0.25f);
            this.UniformMap[$@"spheres[{index}].radius"].SetValue(sphereNode.Scale.X);
            return true;
        }
        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera)
        {
            base.BindShader(material, objectMatrix, camera);

            this.UniformMap[$@"ViewPosition"].SetValue(camera.Owner.Position);
            this.UniformMap[$@"ViewRight"].SetValue(camera.Owner.Right);
            this.UniformMap[$@"ViewUp"].SetValue(camera.Owner.Up);
            this.UniformMap[$@"ViewForward"].SetValue(camera.Owner.Forward);

            int index = 0;
            foreach (var scene in World.Scenes)
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

            this.UniformMap[$@"numSpheres"].SetValue(index);

        }

    }
}
