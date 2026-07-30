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
