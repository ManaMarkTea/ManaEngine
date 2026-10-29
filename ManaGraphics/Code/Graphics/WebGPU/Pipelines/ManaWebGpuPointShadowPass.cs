using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics.Renderers;
using ManaGraphics.Code.Graphics.Resources;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// Draws the opaque meshes depth-only into the six faces of each shadow-casting point light. A light's faces are
    /// redrawn only when it or a caster within its reach changes, so a still room costs nothing after its first frame.
    /// </summary>
    internal sealed unsafe class ManaWebGpuPointShadowPass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuPointShadowMap _map;
        private readonly ManaWebGpuDepthOnlyDrawer _drawer;
        private readonly int[] _drawnKeys = new int[ManaRenderScene.MaxPointShadows];
        private readonly Matrix4x4[] _views = new Matrix4x4[ManaWebGpuPointShadowMap.LayerCount];
        private readonly int[] _viewLayers = new int[ManaWebGpuPointShadowMap.LayerCount];
        private readonly Vector3[] _viewLights = new Vector3[ManaWebGpuPointShadowMap.LayerCount];

        public ManaWebGpuPointShadowPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, ManaWebGpuPointShadowMap map)
            : base(gpu)
        {
            _map = map;
            _drawer = new ManaWebGpuDepthOnlyDrawer(gpu, bindings, ManaWebGpuPointShadowMap.LayerCount, TextureFormat.Depth32float, bias: true, "point shadow");
        }

        public override string ProfileName => "GPU: Point Shadows";
        public override int PipelineCount => _drawer.PipelineCount;
        public override int ShaderModuleCount => _drawer.PipelineCount;

        public override void Prepare(ManaWebGpuFrame frame)
        {
            var lights = frame.Scene.PointShadows;
            var count = 0;
            for (var slot = 0; slot < lights.Count && slot < ManaRenderScene.MaxPointShadows; slot++)
            {
                var position = new Vector3(lights[slot].X, lights[slot].Y, lights[slot].Z);
                var range = lights[slot].W;
                var key = Key(frame, position, range);
                if (key == _drawnKeys[slot])
                {
                    continue;
                }
                _drawnKeys[slot] = key;
                for (var face = 0; face < ManaWebGpuPointShadowMap.FaceCount; face++)
                {
                    var layer = slot * ManaWebGpuPointShadowMap.FaceCount + face;
                    _map.FaceViewProjections[layer] = ManaWebGpuPointShadowMap.FaceViewProjection(position, range, face);
                    _views[count] = _map.FaceViewProjections[layer];
                    _viewLayers[count] = layer;
                    _viewLights[count] = position;
                    count++;
                }
            }
            if (count == 0)
            {
                return;
            }
            _map.UploadMatrices();
            _drawer.Draw(frame, _views.AsSpan(0, count), view => (nint)_map.LayerView(_viewLayers[view]), ManaWebGpuShadowMap.IsCaster,
                (view, bounds) => Encloses(bounds, _viewLights[view]));
        }

        // A light inside a caster's bounds is inside its own fixture -- a shade, a bulb, the glowing piece an import put
        // it at -- which would otherwise black out every face.
        private static bool Encloses((Vector3 Min, Vector3 Max) bounds, Vector3 light)
        {
            const float Margin = 0.02f;
            return light.X > bounds.Min.X - Margin && light.Y > bounds.Min.Y - Margin && light.Z > bounds.Min.Z - Margin
                && light.X < bounds.Max.X + Margin && light.Y < bounds.Max.Y + Margin && light.Z < bounds.Max.Z + Margin;
        }

        // The light, and every caster whose bounds reach into its range: geometry and where it stands.
        private static int Key(ManaWebGpuFrame frame, Vector3 position, float range)
        {
            var hash = new HashCode();
            hash.Add(position);
            hash.Add(range);
            foreach (var mesh in frame.Meshes)
            {
                var instance = mesh.Instance;
                if (!ManaWebGpuShadowMap.IsCaster(instance))
                {
                    continue;
                }
                var (min, max) = instance.MeshData.WorldBounds(instance.WorldTransform);
                if (Vector3.DistanceSquared(Vector3.Clamp(position, min, max), position) > range * range)
                {
                    continue;
                }
                if (Encloses((min, max), position))
                {
                    continue;
                }
                hash.Add(instance.MeshData.GeometryHash);
                hash.Add(instance.WorldTransform);
            }
            // 0 is "never drawn".
            return hash.ToHashCode() | 1;
        }

        public override void Dispose()
        {
            _drawer.Dispose();
        }
    }
}
