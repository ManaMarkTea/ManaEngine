using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Runs the colours of every Light and Glow on its node and beneath round the rainbow -- stage lighting, a party,
    /// a warning beacon. It sets the colour shown (<see cref="ManaLightSourceComponent.Animate"/>), never the saved one,
    /// so a level with it open in the editor stays unchanged. Driven by game time, so it plays in the editor as in play,
    /// and a recording steps it frame by frame.
    /// <para>
    /// <see cref="Phase"/> offsets one animator against another (a row of lamps chasing); <see cref="Spread"/> fans the
    /// lights beneath one animator round the wheel, so a single one on a ring of lamps makes a rainbow.
    /// </para>
    /// </summary>
    public sealed class ManaColorCycleComponent : ManaComponent
    {
        /// <summary>Times round the rainbow a second; negative runs it backwards, 0 holds a colour.</summary>
        public float Speed { get; set; } = 0.25f;

        /// <summary>Where on the wheel it starts, 0 to 1 (0 red, a third green, two thirds blue).</summary>
        public float Phase { get; set; }

        /// <summary>How far round the wheel each further light beneath is set from the one before, 0 to 1.</summary>
        public float Spread { get; set; }

        /// <summary>0 is white, 1 the full colour.</summary>
        public float Saturation { get; set; } = 1.0f;

        /// <summary>Multiplies the colour: above 1 a glow blooms more.</summary>
        public float Brightness { get; set; } = 1.0f;

        /// <summary>Brightness pulses a second; 0 holds steady.</summary>
        public float PulseSpeed { get; set; }

        /// <summary>How deep a pulse dips, 0 (none) to 1 (down to dark): a strobe is fast and deep.</summary>
        public float PulseDepth { get; set; } = 0.5f;

        private readonly List<ManaComponent> _targets = new();

        public ManaColorCycleComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Color Cycle";
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            var seconds = ManaTime.GlobalTime.GameTime.TotalSeconds;

            _targets.Clear();
            Collect(Owner);
            for (var i = 0; i < _targets.Count; i++)
            {
                var color = ColorAt(seconds, i);
                switch (_targets[i])
                {
                    case ManaLightSourceComponent light:
                        light.Animate(color);
                        break;
                    case ManaGlowComponent glow:
                        glow.Animate(color);
                        break;
                }
            }
        }

        /// <summary>The colour the <paramref name="index"/>th light beneath shows at a moment.</summary>
        public Vector4 ColorAt(double seconds, int index)
        {
            var hue = Phase + Speed * seconds + Spread * index;
            hue -= Math.Floor(hue);
            var value = 1.0;
            if (PulseSpeed > 0.0f)
            {
                var wave = 0.5 + 0.5 * Math.Cos(seconds * PulseSpeed * Math.Tau);
                value = 1.0 - Math.Clamp(PulseDepth, 0.0f, 1.0f) * (1.0 - wave);
            }
            var rgb = HueToRgb((float)hue, Math.Clamp(Saturation, 0.0f, 1.0f)) * (float)value * Brightness;
            return new Vector4(rgb, 1.0f);
        }

        /// <summary>A hue (0 to 1 round the wheel) at full value and the given saturation, as linear-ish RGB.</summary>
        public static Vector3 HueToRgb(float hue, float saturation)
        {
            var h = hue * 6.0f;
            var r = Math.Clamp(MathF.Abs(h - 3.0f) - 1.0f, 0.0f, 1.0f);
            var g = Math.Clamp(2.0f - MathF.Abs(h - 2.0f), 0.0f, 1.0f);
            var b = Math.Clamp(2.0f - MathF.Abs(h - 4.0f), 0.0f, 1.0f);
            var full = new Vector3(r, g, b);
            return Vector3.Lerp(Vector3.One, full, saturation);
        }

        // Its own node first, then down the tree in order: the order Spread fans them round the wheel.
        private void Collect(ManaSceneNode node)
        {
            foreach (var component in node.Components)
            {
                if (component is ManaLightSourceComponent { LightType: ManaCore.Graphics.ManaLightType.Point } or ManaGlowComponent)
                {
                    _targets.Add(component);
                }
            }
            foreach (var child in node.Children)
            {
                // A light beneath with an animator of its own follows that one instead.
                if (!child.Components.Any(component => component is ManaColorCycleComponent))
                {
                    Collect(child);
                }
            }
        }
    }
}
