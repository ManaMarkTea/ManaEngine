using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>A depth texture and its view, made and released together; a failed construction holds nothing.</summary>
    internal sealed unsafe class ManaWebGpuDepthTarget : ManaWebGpuResource
    {
        private Texture* _texture;
        private TextureView* _view;

        public ManaWebGpuDepthTarget(ManaWebGpuDevice gpu, uint width, uint height, string name)
            : base(gpu)
        {
            var format = gpu.DepthFormat;
            Width = width;
            Height = height;

            var textureDescriptor = new TextureDescriptor
            {
                // CopySrc as well, so the composited frame can be snapshotted for the UI's blurred backdrop.
                // Nothing copies it unless a panel asks for blur; the usage flag alone costs nothing.
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _texture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (_texture == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU {name} texture.");
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.DepthOnly
            };

            _view = Api.TextureCreateView(_texture, &viewDescriptor);
            if (_view == null)
            {
                Api.TextureRelease(_texture);
                _texture = null;
                throw new InvalidOperationException($"Failed to create WebGPU {name} texture view.");
            }
        }

        public uint Width { get; }

        public uint Height { get; }

        public TextureView* View => _view;

        public override void Dispose()
        {
            if (_view != null)
            {
                Api.TextureViewRelease(_view);
                _view = null;
            }

            if (_texture != null)
            {
                Api.TextureRelease(_texture);
                _texture = null;
            }
        }
    }
}
