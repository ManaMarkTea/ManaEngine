using Mana.Code;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaSceneNode : ManaObject
    {
        public ManaSceneNode Parent { get; set; }
        public List<ManaSceneNode> Children { get; set; }

        public List<ManaComponent> Components { get; set; }

        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Scale { get; set; }

        public override string ToString()
        {
            return $"{Name}";
        }

        public Vector3 Right
        {
            get
            {
                var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
                var right = Vector3.TransformNormal(Vector3.UnitX, rotationMatrix);                
                return right;
            }
        }

        public Vector3 Up
        {
            get
            {
                var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
                var up = Vector3.TransformNormal(Vector3.UnitY, rotationMatrix);
                return up;
            }
        }

        public Vector3 Forward
        {
            get
            {
                // OpenGL is Right handed coordinates... so -z is forward

                var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
                var up = Vector3.TransformNormal(-Vector3.UnitZ, rotationMatrix);
                return up;
            }
        }

        public Matrix4x4 LocalTransform { get; set; }
        public Matrix4x4 WorldTransform { get; set; }

        
        public ManaSceneNode(ManaContext context, Matrix4x4 localTransform) : base(context)
        {            
            this.Parent = null;
            this.LocalTransform = localTransform;
            this.WorldTransform = localTransform;
            Children = new List<ManaSceneNode>();
            Components = new List<ManaComponent>();

            Rotation = Quaternion.CreateFromRotationMatrix(localTransform).ToEuler();                       
            Position = localTransform.Translation;
            Scale = Vector3.One;
        }

        public void AddChild(ManaSceneNode child)
        {
            if (child.Parent != null)
            {
                child.Parent.RemoveChild(child);
            }
            Children.Add(child);
            child.Parent = this;
        }

        public void RemoveChild(ManaSceneNode child)
        {
            Children.Remove(child);
            child.Parent = null;
        }

        public void UpdateTransform()
        {
            LocalTransform = Matrix4x4.CreateScale(Scale);
            LocalTransform *= Matrix4x4.CreateFromQuaternion(Rotation.ToQuaternionFromEuler());
            LocalTransform *= Matrix4x4.CreateTranslation(Position);

            if (Parent != null)
            {
                //WorldTransform = Parent.WorldTransform * LocalTransform;
                WorldTransform = LocalTransform * Parent.WorldTransform;
            }
            else
            {
                WorldTransform = LocalTransform;
            }
            foreach (ManaSceneNode child in Children)
            {
                child.UpdateTransform();
            }

            foreach (ManaComponent component in Components)
            {
                component.UpdateComponent();
            }
        }

        public void Draw()
        {            
            foreach (ManaSceneNode child in Children)
            {
                child.Draw();
            }

            foreach (ManaComponent component in Components)
            {
                component.DrawComponent();
            }
        }

        public void SetLocalTransform(Matrix4x4 localTransform)
        {
            this.LocalTransform = localTransform;
        }

        public Matrix4x4 GetLocalTransform()
        {
            return LocalTransform;
        }
    }


    public class ManaMeshSceneNode : ManaSceneNode
    {
        public MeshRendererComponent MeshRenderer;
        public ManaMesh Mesh;

        public ManaMeshSceneNode(ManaContext context, ManaMesh mesh) : base (context, Matrix4x4.Identity)
        {            
            Mesh = mesh;
            MeshRenderer = new MeshRendererComponent(this, mesh);
            Components.Add(MeshRenderer);
        }
    }
}
