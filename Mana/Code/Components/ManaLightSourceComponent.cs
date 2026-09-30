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

        /// <summary>How far a point light reaches, in metres; it fades smoothly to nothing there.</summary>
        public float Range { get; set; } = 8.0f;

        // The irradiance renderer's far-off fill lights, which keep the four grid slots rather than a lamp's.
        internal bool IsGridLight { get; set; }

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
