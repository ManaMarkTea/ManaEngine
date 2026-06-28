using System.Runtime.CompilerServices;

namespace Mana.Code.Nodes
{
    public class ManaSceneNode : ManaObject
    {
        private Vector3 _position;

        public ManaSceneNode? Parent { get; set; }
        public List<ManaSceneNode> Children { get; set; }

        public List<ManaComponent> Components { get; set; }

        public Vector3 Position 
        { 
            get => _position; 
            set
            {
                if (!_position.Equals(value))
                {
                    _position = value;                 
                }
            }
        }

        public Vector3 Rotation { get; set; }
        public Vector3 RotationDegrees { get { return Rotation.ToEulerDegrees(); } }
        public Vector3 Scale { get; set; }
        public bool IncludeInEnvironmentCapture { get; set; } = true;

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
            Parent = null;
            LocalTransform = localTransform;
            WorldTransform = localTransform;
            Children = new List<ManaSceneNode>();
            Components = new List<ManaComponent>();

            Matrix4x4.Decompose(localTransform, out Vector3 dScale, out Quaternion dRotation, out Vector3 dPosition);
            Rotation = dRotation.ToEuler();
            Position = dPosition;
            Scale = dScale;
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
            LocalTransform = Matrix4x4.Identity;
            LocalTransform *= Matrix4x4.CreateScale(Scale);
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

        public void CollectRenderMeshInstances(List<ManaCore.Graphics.ManaRenderMeshInstance> output)
        {
            CollectRenderMeshInstances(output, IncludeInEnvironmentCapture);
        }

        private void CollectRenderMeshInstances(List<ManaCore.Graphics.ManaRenderMeshInstance> output, bool includeInEnvironmentCapture)
        {
            foreach (var component in Components)
            {
                if (component is not MeshRendererComponent renderer || renderer.Mesh == null)
                {
                    continue;
                }

                try
                {
                    var meshData = renderer.Mesh.GetOrCreateCpuData();
                    output.Add(new ManaCore.Graphics.ManaRenderMeshInstance
                    {
                        InstanceKey = $"{RuntimeHelpers.GetHashCode(this):x8}:{RuntimeHelpers.GetHashCode(renderer):x8}",
                        MeshKey = renderer.Mesh.RenderKey,
                        MeshVersion = renderer.Mesh.CpuDataVersion,
                        Name = string.IsNullOrWhiteSpace(Name) ? renderer.Mesh.Name : Name,
                        MeshData = meshData,
                        WorldTransform = WorldTransform,
                        ShaderName = renderer.Material.Shader?.GetType().Name,
                        Albedo = renderer.Material.Albedo.Value,
                        Metallic = renderer.Material.Metallic.Value,
                        Roughness = renderer.Material.Roughness.Value,
                        AmbientOcclusion = renderer.Material.Ambient.Value,
                        IncludeInEnvironmentCapture = includeInEnvironmentCapture
                    });
                }
                catch (InvalidOperationException)
                {
                    // Legacy meshes that only exist as backend GPU buffers are skipped.
                }
            }

            foreach (var child in Children)
            {
                child.CollectRenderMeshInstances(output, includeInEnvironmentCapture && child.IncludeInEnvironmentCapture);
            }
        }

        public void RotateX(float degrees)
        {
            Rotation = new Vector3(Rotation.X + degrees.ToRadians(), Rotation.Y, Rotation.Z);
        }

        public void RotateY(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y + degrees.ToRadians(), Rotation.Z);
        }

        public void RotateZ(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, Rotation.Z + degrees.ToRadians());
        }

        public void SetLocalTransform(Matrix4x4 localTransform)
        {
            LocalTransform = localTransform;
        }

        public Matrix4x4 GetLocalTransform()
        {
            return LocalTransform;
        }

        public List<T> FindShapes<T>()
        {
            List<T> output = new List<T>();

            foreach (var component in Components)
            {
                if (component is MeshRendererComponent renderer)
                {
                    if (renderer.Mesh is T shape)
                    {
                        output.Add(shape);
                    }
                }
            }

            return output;
        }

        public List<T> GetComponents<T>()
        {
            return Components.OfType<T>().ToList();
        }
    }


    public class ManaMeshSceneNode : ManaSceneNode
    {
        public MeshRendererComponent MeshRenderer;
        public ManaMesh Mesh;

        public ManaMeshSceneNode(ManaContext context, ManaMesh mesh) : base(context, Matrix4x4.Identity)
        {
            Mesh = mesh;
            MeshRenderer = new MeshRendererComponent(this, mesh);
            Components.Add(MeshRenderer);
        }
    }
}
