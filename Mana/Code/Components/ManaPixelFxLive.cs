using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaPixelFx.Core;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A ported pixelfx effect drawn as the game runs: the CPU paints its two buffers (pixelfx's colour and light),
    /// they go up as two textures as they are -- the buffers are already RGBA bytes -- and the GPU puts them
    /// together: colour blended over the scene, light added on top in HDR, where the hottest of it blooms.
    /// </summary>
    internal sealed class ManaPixelFxLive
    {
        private readonly ManaDrawingNode _drawing;
        private readonly PixelFrame _frame;
        private readonly ManaDynamicTexture _color;
        private readonly ManaDynamicTexture _light;
        private readonly MeshRendererComponent _colorRenderer;
        private readonly MeshRendererComponent _lightRenderer;
        private readonly Dictionary<string, object> _options;
        private PixelFxInstance _instance;
        private long _drawnStep = long.MinValue;
        private bool _blank;

        /// <param name="anchor">Where the node sits on the effect's canvas, in pixels from its top left.</param>
        /// <param name="drawn">Everything one run draws, in canvas pixels: the quads cover only that.</param>
        public ManaPixelFxLive(ManaSceneNode owner, PixelEffect effect, Dictionary<string, object> options, float pixelsPerUnit, (double X, double Y) anchor, PixelBounds drawn, bool flip, Vector4 tint)
        {
            _drawing = new ManaDrawingNode(owner, "Pixel FX");
            Effect = effect;
            _options = options;
            var (w, h) = effect.Meta.Size;
            var (ox, oy) = effect.Meta.Origin;
            _frame = new PixelFrame(w, h);
            _options["x"] = (double)ox;
            _options["y"] = (double)oy;
            _instance = new PixelFxInstance(effect, _options);
            _color = new ManaDynamicTexture(w, h);
            _light = new ManaDynamicTexture(w, h);

            if (drawn.IsEmpty)
            {
                drawn = new PixelBounds(0, 0, w - 1, h - 1);
            }
            var ppu = MathF.Max(pixelsPerUnit, 0.01f);
            var size = new Vector2(drawn.Width / ppu, drawn.Height / ppu);
            // A sprite's pivot runs 0..1 across and up from its bottom left; flipped, the art mirrors about the anchor.
            var pivot = new Vector2((float)((anchor.X - drawn.X0) / drawn.Width), (float)((drawn.Y1 + 1 - anchor.Y) / drawn.Height));
            if (flip)
            {
                pivot.X = 1.0f - pivot.X;
            }
            _colorRenderer = Quad(new ManaSpriteMesh(owner.Context, _color, $"Pixel FX {effect.Meta.Id}", size, pivot, tint, true), ManaOpacityMode.Blend);
            // A millimetre nearer the camera, so the light sorts after the colour it lands on.
            _lightRenderer = Quad(new ManaSpriteMesh(owner.Context, _light, $"Pixel FX {effect.Meta.Id} light", size, pivot, tint, true, 0.001f), ManaOpacityMode.Additive);
            Crop(_colorRenderer, drawn, w, h, flip);
            Crop(_lightRenderer, drawn, w, h, flip);
        }

        public PixelEffect Effect { get; }

        /// <summary>Shows the effect <paramref name="seconds"/> in, or nothing when null; redrawn only when its own clock ticks.</summary>
        public void Show(double? seconds)
        {
            if (seconds is not { } t)
            {
                if (!_blank)
                {
                    _frame.Clear();
                    Upload();
                    _blank = true;
                    _drawnStep = long.MinValue;
                }
                return;
            }

            // A one-shot playing again starts a fresh instance, so a simulated one starts from nothing.
            if (t < _instance.T)
            {
                _instance = new PixelFxInstance(Effect, _options);
            }
            _instance.Update(t - _instance.T);
            var step = (long)Math.Floor(t * Effect.Meta.Fps);
            if (step == _drawnStep && !_blank)
            {
                return;
            }
            _drawnStep = step;
            _blank = false;
            _frame.Clear();
            _instance.Draw(_frame);
            // Light cut off at the canvas edge shows as a box: eased out over the last pixels, as pixelfx's sheets are.
            if (!PixelFxRun.IsScreen(Effect.Meta))
            {
                _frame.EdgeFade();
            }
            Upload();
        }

        /// <summary>Takes the two quads off the node.</summary>
        public void Remove() => _drawing.Clear();

        private MeshRendererComponent Quad(ManaSpriteMesh mesh, ManaOpacityMode mode)
        {
            mesh.Materials[0].Unlit = true;
            mesh.Materials[0].OpacityMode = mode;
            return _drawing.Add(mesh);
        }

        // The quad shows only the drawn part of the canvas, whose rows go up bottom first (see Upload).
        private static void Crop(MeshRendererComponent renderer, PixelBounds drawn, int w, int h, bool flip)
        {
            var scale = new Vector2((float)drawn.Width / w, (float)drawn.Height / h);
            var offset = new Vector2((float)drawn.X0 / w, (float)(h - 1 - drawn.Y1) / h);
            if (flip)
            {
                scale.X = -scale.X;
                offset.X = (float)(drawn.X1 + 1) / w;
            }
            renderer.Material.UvScale = scale;
            renderer.Material.UvOffset = offset;
        }

        // Rows go up bottom first, as textures are uploaded.
        private void Upload()
        {
            Copy(_frame.Color, _color);
            Copy(_frame.Light, _light);
        }

        private void Copy(uint[] buffer, ManaDynamicTexture texture)
        {
            var w = _frame.W;
            var h = _frame.H;
            var bytes = MemoryMarshal.AsBytes(buffer.AsSpan());
            lock (texture.Sync)
            {
                for (var y = 0; y < h; y++)
                {
                    bytes.Slice(y * w * 4, w * 4).CopyTo(texture.Pixels.AsSpan((h - 1 - y) * w * 4, w * 4));
                }
            }
            texture.Publish();
        }
    }
}
