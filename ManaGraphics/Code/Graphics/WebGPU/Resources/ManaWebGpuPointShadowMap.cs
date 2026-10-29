using System.Numerics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    // Shadows for the point lights that ask for them (ManaRenderScene.PointShadows): six 90-degree faces a light, drawn
    // depth-only into one texture array (layer = slot * 6 + face), each face's view-projection in a storage buffer for
    // include/point_shadows.wgsl. Apart from ManaWebGpuPointShadowPass because the frame bind group needs it first.
    internal sealed unsafe class ManaWebGpuPointShadowMap : ManaWebGpuDeviceObject
    {
        public const int FaceCount = 6;
        public const int LayerCount = ManaRenderScene.MaxPointShadows * FaceCount;
        // Per face: 48 Depth32 faces of 512 are 50 MB, and a lamp's 90 degrees at 3 m comes to a centimetre a texel.
        public const int Resolution = 512;
        public const float Near = 0.05f;

        // +X -X +Y -Y +Z -Z, in the order the shader picks a face by the major axis.
        private static readonly (Vector3 Forward, Vector3 Up)[] Faces =
        [
            (Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitX, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitY, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY)
        ];

        private Texture* _texture;
        private readonly TextureView*[] _layerViews = new TextureView*[LayerCount];

        public ManaWebGpuPointShadowMap(ManaWebGpuDevice gpu)
            : base(gpu)
        {
            try
            {
                Create();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public TextureView* ArrayView { get; private set; }
        public WebGpuBuffer* Matrices { get; private set; }
        public static ulong MatricesSize => (ulong)(LayerCount * sizeof(Matrix4x4));
        public Matrix4x4[] FaceViewProjections { get; } = new Matrix4x4[LayerCount];

        public TextureView* LayerView(int layer) => _layerViews[layer];

        /// <summary>One face of a light's cube: looking down an axis with a 90-degree view out to the light's range.</summary>
        public static Matrix4x4 FaceViewProjection(Vector3 position, float range, int face)
        {
            var (forward, up) = Faces[face];
            var view = Matrix4x4.CreateLookAt(position, position + forward, up);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, Near, MathF.Max(range, Near * 2.0f));
            return view * projection;
        }

        public void UploadMatrices()
        {
            fixed (Matrix4x4* matrices = FaceViewProjections)
            {
                Api.QueueWriteBuffer(Queue, Matrices, 0, matrices, (nuint)MatricesSize);
            }
        }

        private void Create()
        {
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = Resolution, Height = Resolution, DepthOrArrayLayers = LayerCount },
                Format = TextureFormat.Depth32float,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _texture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (_texture == null)
            {
                throw new InvalidOperationException("Failed to create the point-light shadow map.");
            }

            var arrayDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Depth32float,
                Dimension = TextureViewDimension.Dimension2DArray,
                BaseArrayLayer = 0,
                ArrayLayerCount = LayerCount,
                MipLevelCount = 1,
                Aspect = TextureAspect.DepthOnly
            };
            ArrayView = Api.TextureCreateView(_texture, &arrayDescriptor);
            if (ArrayView == null)
            {
                throw new InvalidOperationException("Failed to create the point-light shadow map view.");
            }

            for (var layer = 0; layer < LayerCount; layer++)
            {
                var layerDescriptor = new TextureViewDescriptor
                {
                    Format = TextureFormat.Depth32float,
                    Dimension = TextureViewDimension.Dimension2D,
                    BaseArrayLayer = (uint)layer,
                    ArrayLayerCount = 1,
                    MipLevelCount = 1,
                    Aspect = TextureAspect.DepthOnly
                };
                _layerViews[layer] = Api.TextureCreateView(_texture, &layerDescriptor);
                if (_layerViews[layer] == null)
                {
                    throw new InvalidOperationException($"Failed to create point-light shadow face {layer}.");
                }
            }

            var bufferDescriptor = new BufferDescriptor { Usage = BufferUsage.Storage | BufferUsage.CopyDst, Size = MatricesSize };
            Matrices = Api.DeviceCreateBuffer(Device, &bufferDescriptor);
            if (Matrices == null)
            {
                throw new InvalidOperationException("Failed to create the point-light shadow matrices.");
            }
        }

        public override void Dispose()
        {
            for (var layer = 0; layer < LayerCount; layer++)
            {
                if (_layerViews[layer] != null)
                {
                    Api.TextureViewRelease(_layerViews[layer]);
                    _layerViews[layer] = null;
                }
            }
            if (ArrayView != null)
            {
                Api.TextureViewRelease(ArrayView);
                ArrayView = null;
            }
            if (_texture != null)
            {
                Api.TextureRelease(_texture);
                _texture = null;
            }
            if (Matrices != null)
            {
                Api.BufferRelease(Matrices);
                Matrices = null;
            }
        }
    }
}
