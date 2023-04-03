using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.Assimp;

namespace Mana
{
    public class ManaScene
    {
        public List<ManaSceneNode> Children { get; set; }

        public ManaWorld World { get; set; }


        public ManaScene(ManaWorld world) 
        {
            World = world;
            Children = new List<ManaSceneNode>();
        }

        public unsafe void LoadFromGltf(Scene* scene)
        {            
            var root = scene->MRootNode;
            for (int i = 0; i < root->MNumChildren; i++)
            {
                var child = root->MChildren[i];
                var childSceneNode = LoadSceneNode(child);
                Children.Add(childSceneNode);
            }            
        }

        private unsafe ManaSceneNode LoadSceneNode(Node* node)
        {
            var local = node->MTransformation;
            local = Matrix4x4.Transpose(local);
            var metaSceneNode = new ManaSceneNode(World, local);
            metaSceneNode.Name = node->MName;
            
            if (node->MNumMeshes != 0)
            {
                for (int i = 0; i < node->MNumMeshes; i++)
                {
                    int meshIndex = (int)node->MMeshes[i];
                    ManaMesh foundMesh = World.Meshes[meshIndex];
                    if (foundMesh != null)
                    {
                        metaSceneNode.Components.Add(new MeshRendererComponent(metaSceneNode, foundMesh) { });
                    }
                    else
                    {
                        Trace.TraceWarning($"Mesh not found {node->MName} had could not find {meshIndex}");
                    }
                }
            }

            for (int i = 0; i < node->MNumChildren; i++)
            {
                var child = node->MChildren[i];
                var childSceneNode = LoadSceneNode(child);
                Children.Add(childSceneNode);
            }

            return metaSceneNode;
        }

        public void Update()
        {
            foreach (var node in Children)
            {
                node.UpdateTransform();
            }
        }

        public void Draw()
        {
            foreach (var node in Children)
            {
                node.Draw();
            }
        }

    }
}
