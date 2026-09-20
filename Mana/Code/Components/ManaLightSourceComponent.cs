using Mana.Code.Nodes;
using ManaCore.Graphics;

namespace Mana.Code
{
    [EditorComponent(AllowMultiple = true)]
    public class ManaLightSourceComponent : ManaComponent
    {
        public float Intensity { get; set; } = 1.0f;
        public Vector4 Color { get; set; } = Colors.White;
        public Vector3 Direction { get; set; } = -Vector3.UnitY;
        public bool Enabled { get; set; } = true;

        public ManaLightType LightType { get; set; } = ManaLightType.Point;

        /// <summary>Only meaningful when <see cref="LightType"/> is Directional -- casts cascaded shadow maps.</summary>
        public bool CastsShadows { get; set; }

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
