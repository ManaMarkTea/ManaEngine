using Mana.Code.Levels;
using Mana.Code.Shapes;

namespace Mana.Code.Nodes
{
    // Mirrors ManaMeshSceneNode's pattern (a node that owns its MeshRendererComponent), plus a
    // ripple API on top. Ripple state is written into MeshRenderer.DynamicMaterial's per-instance
    // shader uniforms (rippleData0..rippleData11 in water.wgsl) - the WGSL shader does all the
    // wave/ripple math and normal recomputation on the GPU each frame, so nothing here needs to
    // run per-frame on the CPU.
    public class ManaWaterNode : ManaSceneNode
    {
        private const int RippleSlotCount = 12;

        public MeshRendererComponent MeshRenderer;
        public ManaWaterPlane Mesh;

        private int _nextRippleSlot;

        public ManaWaterNode(ManaContext context, Vector2 size, int resolution = 48) : base(context, Matrix4x4.Identity)
        {
            Name = "Water";
            Mesh = new ManaWaterPlane(context, Colors.White, size, resolution);
            MeshRenderer = new MeshRendererComponent(this, Mesh);
            Components.Add(MeshRenderer);
        }

        public const int MinimumResolution = 4;
        public const int MaximumResolution = 256;

        /// <summary>
        /// Rebuilds the surface at a new size and resolution, keeping the material -- and with it the water's
        /// look and any ripples in flight. Clamped: a zero-sized plane is invisible and a huge grid is a
        /// vertex buffer nobody asked for. The level source follows, so the change is what gets saved.
        /// </summary>
        public void Resize(Vector2 size, int resolution)
        {
            size = new Vector2(MathF.Max(0.5f, size.X), MathF.Max(0.5f, size.Y));
            resolution = Math.Clamp(resolution, MinimumResolution, MaximumResolution);

            if (Mesh.Size == size && Mesh.Resolution == resolution)
            {
                return;
            }

            Mesh = new ManaWaterPlane(Context, Colors.White, size, resolution);
            MeshRenderer.Mesh = Mesh;
            LevelSource = ManaLevelSource.ForWater(size, resolution);
        }

        // worldPosition is projected into this node's local XZ plane (ripples are computed in
        // local space in water.wgsl, matching how local_pos is used elsewhere e.g. liquid_ball.wgsl).
        public void SpawnRipple(Vector3 worldPosition, float strength = 1.0f)
        {
            if (!Matrix4x4.Invert(WorldTransform, out var inverseWorld))
            {
                return;
            }

            var localPosition = Vector3.Transform(worldPosition, inverseWorld);
            var slot = _nextRippleSlot;
            _nextRippleSlot = (_nextRippleSlot + 1) % RippleSlotCount;

            var startTime = (float)ManaTime.GlobalTime.GameTime.TotalSeconds;
            MeshRenderer.DynamicMaterial.SetShaderUniform(
                $"rippleData{slot}",
                new Vector4(localPosition.X, localPosition.Z, startTime, strength));
        }
    }
}
