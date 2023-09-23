using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public ManaSceneNode AddCube(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaCube(Context, color, size)));
            Scene.Children.Add(extraCube);
            return extraCube;
        }

        public ManaSceneNode AddSphere(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaSphere(Context, color, 0.5f)));
            extraCube.Scale = size;
            Scene.Children.Add(extraCube);
            return extraCube;
        }
        public ManaSceneNode AddPyramid(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaPyramid(Context, color, Vector3.One)));
            //extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaCube(this, color, size)));
            extraCube.Scale = size;
            Scene.Children.Add(extraCube);
            return extraCube;
        }

        public ManaSceneNode AddQuad(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaQuad(Context, color, size)));
            Scene.Children.Add(extraCube);
            return extraCube;
        }

    }
}
