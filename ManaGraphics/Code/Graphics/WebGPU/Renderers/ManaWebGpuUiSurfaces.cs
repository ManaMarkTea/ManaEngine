using ManaUI;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// Gives each application-owned picture (<see cref="ManaUiSurface"/>) a texture of its own and keeps it current.
    /// Only a picture a batch draws from this frame is uploaded, and only when its revision moved -- so an idle canvas
    /// costs nothing.
    /// </summary>
    internal sealed unsafe class ManaWebGpuUiSurfaces : ManaWebGpuDeviceObject
    {
        private sealed class Entry
        {
            public Texture* Texture;
            public TextureView* View;
            public int Width;
            public int Height;
            public long Revision = -1;
        }

        // sRGB like the image atlas, so the PNG-style bytes a host hands over are decoded once, not twice.
        private const TextureFormat SurfaceFormat = TextureFormat.Rgba8UnormSrgb;

        private readonly ManaWebGpuUiCompositor _ui;
        private readonly Dictionary<int, Entry> _entries = new();

        public ManaWebGpuUiSurfaces(ManaWebGpuDevice gpu, ManaWebGpuUiCompositor ui)
            : base(gpu)
        {
            _ui = ui;
        }

        /// <summary>Call once a frame after the UI is recorded and before it is drawn.</summary>
        public void Sync(UiRoot root)
        {
            if (root.Surfaces.Count == 0 && _entries.Count == 0)
            {
                return;
            }

            foreach (var (id, surface) in root.Surfaces.ToArray())
            {
                if (surface.IsDisposed)
                {
                    Release(id);
                    root.Surfaces.Remove(id);
                    continue;
                }

                if (!surface.HasPixels || !root.DrawList.DrawsTexture(id))
                {
                    continue;
                }

                if (!_entries.TryGetValue(id, out var entry))
                {
                    entry = new Entry();
                    _entries[id] = entry;
                }

                if (entry.Texture == null || entry.Width != surface.Width || entry.Height != surface.Height)
                {
                    Recreate(id, entry, surface.Width, surface.Height);
                }

                if (entry.Revision != surface.Revision)
                {
                    Upload(entry, surface);
                    entry.Revision = surface.Revision;
                }
            }
        }

        private void Recreate(int id, Entry entry, int width, int height)
        {
            var oldTexture = entry.Texture;
            var oldView = entry.View;

            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                Format = SurfaceFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };
            entry.Texture = Api.DeviceCreateTexture(Device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = SurfaceFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            entry.View = Api.TextureCreateView(entry.Texture, &viewDescriptor);
            entry.Width = width;
            entry.Height = height;
            entry.Revision = -1;

            // The new view goes in before the old one is freed: the UI renderer holds a bind group on it.
            _ui.SetTexture(id, entry.View);
            if (oldView != null)
            {
                Api.TextureViewRelease(oldView);
            }
            if (oldTexture != null)
            {
                Api.TextureRelease(oldTexture);
            }
        }

        private void Upload(Entry entry, ManaUiSurface surface)
        {
            var destination = new ImageCopyTexture
            {
                Texture = entry.Texture,
                MipLevel = 0,
                Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
                Aspect = TextureAspect.All
            };
            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)(surface.Width * 4),
                RowsPerImage = (uint)surface.Height
            };
            var extent = new Extent3D { Width = (uint)surface.Width, Height = (uint)surface.Height, DepthOrArrayLayers = 1 };

            var pixels = surface.Pixels;
            fixed (byte* data = pixels)
            {
                Api.QueueWriteTexture(Queue, &destination, data, (nuint)pixels.Length, &layout, &extent);
            }
        }

        private void Release(int id)
        {
            if (!_entries.Remove(id, out var entry))
            {
                return;
            }

            _ui.SetTexture(id, null);
            Free(entry);
        }

        private void Free(Entry entry)
        {
            if (entry.View != null)
            {
                Api.TextureViewRelease(entry.View);
                entry.View = null;
            }
            if (entry.Texture != null)
            {
                Api.TextureRelease(entry.Texture);
                entry.Texture = null;
            }
        }

        public override void Dispose()
        {
            foreach (var (id, entry) in _entries)
            {
                _ui.SetTexture(id, null);
                Free(entry);
            }
            _entries.Clear();
        }
    }
}
