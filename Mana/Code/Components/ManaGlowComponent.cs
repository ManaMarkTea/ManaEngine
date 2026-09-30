using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Makes everything beneath its node glow, the way a glowing object is built in Unity or Unreal: the meshes
    /// light themselves (emission -- above 1 it blooms), and a small light lights what is around them. A script,
    /// so it saves with a level and a prefab variant can add one -- a Glowing Coin is a Coin with a Glow.
    /// <para>
    /// It owns nothing it has to undo: the meshes read it as they are gathered for drawing, and the world
    /// gathers its light with the lamps', so taking the Glow off simply stops both.
    /// </para>
    /// </summary>
    public sealed class ManaGlowComponent : ManaComponent
    {
        /// <summary>The colour it glows. White shows the meshes as in full daylight; a colour tints them.</summary>
        public Vector4 Color { get; set; } = new(1.0f, 0.85f, 0.4f, 1.0f);

        /// <summary>How bright the glow is. 1 is fully lit; above 1 it blooms.</summary>
        public float Strength { get; set; } = 1.0f;

        /// <summary>How far its light reaches, in metres. 0 glows without lighting anything else.</summary>
        public float LightRange { get; set; } = 3.0f;

        /// <summary>How bright its light is, relative to a lamp's.</summary>
        public float LightIntensity { get; set; } = 0.5f;

        /// <summary>Where the light sits, from the node -- a coin's middle is a little above its feet.</summary>
        public Vector3 LightOffset { get; set; } = new(0.0f, 0.4f, 0.0f);

        /// <summary>Pulses per second; 0 holds steady.</summary>
        public float PulseSpeed { get; set; }

        public bool Enabled { get; set; } = true;

        public ManaGlowComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Glow";
        }

        /// <summary>The strength right now: steady, or breathing between two thirds and all of it.</summary>
        public float CurrentStrength()
        {
            if (PulseSpeed <= 0.0f)
            {
                return Strength;
            }

            var seconds = (float)(DateTime.Now - ManaTime.GlobalTime.StartTime).TotalSeconds;
            return Strength * (0.83f + 0.17f * MathF.Sin(seconds * PulseSpeed * MathF.Tau));
        }
    }
}
