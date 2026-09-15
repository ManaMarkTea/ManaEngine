using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mana.Code.Levels;
using Mana.Code.Nodes;

namespace Mana.Code.Shapes
{
    public class ManaShapeFactory : ManaObject
    {
        private ManaScene Scene { get; set; }

        public ManaShapeFactory(ManaContext context, ManaScene scene) : base(context) 
        { 
            Scene = scene;
        }


        public ManaSceneNode AddCube(Vector3 position, Vector4 color, float size)
        {
            return AddCube(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddSphere(Vector3 position, Vector4 color, float size)
        {
            return AddSphere(position, color, new Vector3(size, size, size));
        }

        public ManaSceneNode AddPyramid(Vector3 position, Vector4 color, float size)
        {
            return AddPyramid(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddWedge(Vector3 position, Vector4 color, float size)
        {
            return AddWedge(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddDisk(Vector3 position, Vector4 color, float size)
        {
            return AddDisk(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddTorus(Vector3 position, Vector4 color, float size)
        {
            return AddTorus(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddIcosahedron(Vector3 position, Vector4 color, float size)
        {
            return AddIcosahedron(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddIsoSphere(Vector3 position, Vector4 color, float size)
        {
            return AddIsoSphere(position, color, new Vector3(size, size, size));
        }

        public ManaSceneNode AddCube(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Name = "Cube";
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaCube(Context, color, size)));
            extraCube.LevelSource = ManaLevelSource.ForShape("Cube", color, size);
            Scene.Children.Add(extraCube);
            return extraCube;
        }

        public ManaSceneNode AddSphere(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Name = "Sphere";
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaSphere(Context, color, 0.5f)));
            extraCube.Scale = size;
            extraCube.LevelSource = ManaLevelSource.ForShape("Sphere", color, size);
            Scene.Children.Add(extraCube);
            return extraCube;
        }
        public ManaSceneNode AddPyramid(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Name = "Pyramid";
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaPyramid(Context, color, Vector3.One)));
            //extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaCube(this, color, size)));
            extraCube.Scale = size;
            extraCube.LevelSource = ManaLevelSource.ForShape("Pyramid", color, size);
            Scene.Children.Add(extraCube);
            return extraCube;
        }

        public ManaSceneNode AddWedge(Vector3 position, Vector4 color, Vector3 size)
        {
            var wedgeNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            wedgeNode.Name = "Wedge";
            wedgeNode.Components.Add(new MeshRendererComponent(wedgeNode, new ManaWedge(Context, color, size)));
            wedgeNode.LevelSource = ManaLevelSource.ForShape("Wedge", color, size);
            Scene.Children.Add(wedgeNode);
            return wedgeNode;
        }

        public ManaSceneNode AddDisk(Vector3 position, Vector4 color, Vector3 size)
        {
            var diskNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            diskNode.Name = "Disk";
            diskNode.Components.Add(new MeshRendererComponent(diskNode, new ManaDisk(Context, color, size)));
            diskNode.LevelSource = ManaLevelSource.ForShape("Disk", color, size);
            Scene.Children.Add(diskNode);
            return diskNode;
        }

        public ManaSceneNode AddTorus(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode torusNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            torusNode.Name = "Torus";
            torusNode.Components.Add(new MeshRendererComponent(torusNode, new ManaTorus(Context, color)));
            torusNode.Scale = size;
            torusNode.LevelSource = ManaLevelSource.ForShape("Torus", color, size);
            Scene.Children.Add(torusNode);
            return torusNode;
        }

        public ManaSceneNode AddIcosahedron(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode icosahedronNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            icosahedronNode.Name = "Icosahedron";
            icosahedronNode.Components.Add(new MeshRendererComponent(icosahedronNode, new ManaIcosahedron(Context, color)));
            icosahedronNode.Scale = size;
            icosahedronNode.LevelSource = ManaLevelSource.ForShape("Icosahedron", color, size);
            Scene.Children.Add(icosahedronNode);
            return icosahedronNode;
        }

        public ManaSceneNode AddIsoSphere(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode isoSphereNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            isoSphereNode.Name = "Icosphere";
            isoSphereNode.Components.Add(new MeshRendererComponent(isoSphereNode, new ManaIsoSphere(Context, color, 0.5f)));
            isoSphereNode.Scale = size;
            isoSphereNode.LevelSource = ManaLevelSource.ForShape("Icosphere", color, size);
            Scene.Children.Add(isoSphereNode);
            return isoSphereNode;
        }

        public ManaSceneNode AddQuad(Vector3 position, Vector4 color, Vector3 size, string name = "Quad")
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Name = name;
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaQuad(Context, color, size)));
            extraCube.LevelSource = ManaLevelSource.ForShape("Quad", color, size);
            Scene.Children.Add(extraCube);
            return extraCube;
        }


        public ManaSceneNode AddQuad(Vector3 position, Vector4 color, ManaQuadPlanes plane, float axisStart, float axisEnd, float extrusionStart, float extrusionEnd, string name = "Quad")
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Name = name;
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaQuad(Context, color, plane, axisStart, axisEnd, extrusionStart, extrusionEnd)));
            Scene.Children.Add(extraCube);
            return extraCube;
        }

        public ManaSceneNode AddGridCoveFillet(
            Vector3 sweepAxis,
            Vector3 crossAxisA,
            Vector3 crossAxisB,
            float sweepLength,
            float radius,
            float floorLift = 0.0f,
            Vector3 position = default,
            string name = "Grid Cove")
        {
            var fillet = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            fillet.Name = name;
            fillet.Components.Add(new MeshRendererComponent(
                fillet, new ManaGridCoveFillet(Context, sweepAxis, crossAxisA, crossAxisB, sweepLength, radius, floorLift)));
            Scene.Children.Add(fillet);
            return fillet;
        }

        public ManaSceneNode AddVoxels(Vector3 position)
        {
            ManaSceneNode voxelNode = new ManaVoxelsNode(Context, Matrix4x4.CreateTranslation(position));            
            voxelNode.Name = "Voxel Scene";
            voxelNode.LevelSource = ManaLevelSource.Voxels;
            Scene.Children.Add(voxelNode);
            return voxelNode;
        }
    }
}
