using Mana.Code.Nodes;

namespace Mana.Code
{
    public class ManaLightSourceComponent : ManaComponent
    {
        public float Intensity { get; set; } = 1.0f;
        public Vector4 Color { get; set; } = Colors.White;
        public Vector3 Direction { get; set; } = -Vector3.UnitY;
        public bool Enabled { get; set; } = true;

        public ManaLightSourceComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Point Light";
        }

        public ManaLightSourceComponent(ManaContext context)
            : this(new ManaSceneNode(context, Matrix4x4.Identity))
        {
        }
    }
}
