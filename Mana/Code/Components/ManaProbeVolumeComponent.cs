using System.ComponentModel.DataAnnotations;
using ManaCore.Graphics;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A box of baked light probes centred on its node: inside it, ambient light is what actually reaches each spot --
    /// sky that walls do not hide, sunlight bounced off the street, a lamp's glow off the walls -- rather than the open
    /// sky everywhere. The renderer voxelizes the scenery in the box and bakes the probes on the GPU; the bake reruns
    /// when the lights or the sun change, the voxelizing only when the scenery does. Rotation is ignored: the box lies
    /// along the world axes. One per world (the first found).
    /// </summary>
    public sealed class ManaProbeVolumeComponent : ManaComponent
    {
        /// <summary>Off, the scene falls back to the open sky's light everywhere.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The box, in metres along X, Y and Z.</summary>
        public Vector3 Size { get; set; } = new(32.0f, 12.0f, 32.0f);

        /// <summary>Metres between probes: smaller follows light closer, at the square of the memory.</summary>
        [Range(0.5, 16)]
        public float ProbeSpacing { get; set; } = 2.0f;

        /// <summary>The voxel the scenery is cut into for the probes' rays: thinner walls than this may let light through.</summary>
        [Range(0.25, 8)]
        public float VoxelSize { get; set; } = 1.0f;

        /// <summary>Times light bounces: 1 is sky and sun bounced once; each more brightens deep shade a little.</summary>
        [Range(1, 6)]
        public int Bounces { get; set; } = 3;

        public ManaProbeVolumeComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Probe Volume";
        }

        /// <summary>The volume as the renderer takes it, placed by the node.</summary>
        public ManaProbeVolumeSettings Settings()
        {
            var size = Vector3.Max(Size, new Vector3(0.5f));
            var centre = Owner.WorldTransform.Translation;
            return new ManaProbeVolumeSettings(centre - size * 0.5f, size, ProbeSpacing, VoxelSize, Math.Clamp(Bounces, 1, 6));
        }
    }
}
