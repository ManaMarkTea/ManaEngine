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

        /// <summary>How much a point light's brightness wavers, 0 (steady) to 1 (down to nothing): a candle, a campfire.</summary>
        public float FlickerAmount { get; set; }

        /// <summary>How fast it wavers, in rough flickers a second; 0 means a lively 8.</summary>
        public float FlickerSpeed { get; set; }

        /// <summary>The brightness multiplier right now: slow swells with quick jitter on top, never above 1.</summary>
        public float FlickerFactor(double seconds)
        {
            if (FlickerAmount <= 0.0f)
            {
                return 1.0f;
            }

            // Three unrelated sines read as noise; the node's hash keeps neighbouring lamps out of step.
            var phase = (Owner.AuthoredId.GetHashCode() & 0xFFFF) * 0.001;
            var t = seconds * (FlickerSpeed > 0.0f ? FlickerSpeed : 8.0f) + phase;
            var wave = (Math.Sin(t) + 0.6 * Math.Sin(t * 2.37 + 1.3) + 0.35 * Math.Sin(t * 5.11 + 4.1)) / 1.95;
            return (float)Math.Clamp(1.0 - FlickerAmount * (0.5 - 0.5 * wave), 0.0, 1.0);
        }

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
