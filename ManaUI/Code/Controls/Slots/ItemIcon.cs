namespace ManaUI
{
    /// <summary>The stock vector shapes an <see cref="ItemIcon"/> can draw when there is no picture.</summary>
    public enum ItemIconShape
    {
        None,
        Ore,
        Plate,
        Bar,
        Gear,
        Coil,
        Chip,
        Flask,
        Box,
        Brick,
        Dot,
        Arrow,
        Warning,
        Drop,
        Pipe
    }

    /// <summary>
    /// What goes in a slot: a picture from the UI atlas (a rendered thumbnail), or else one of a set of simple vector
    /// shapes in a colour, so a game has a recognisable icon for every item before it has any art.
    /// </summary>
    public class ItemIcon : UiElement
    {
        private ItemIconShape _shape;
        private Vector4 _color = Vector4.One;
        private Vector4 _uv;
        private bool _hasImage;

        public ItemIcon()
        {
            IsHitTestVisible = false;
        }

        public ItemIcon(ItemIconShape shape, Vector4 color)
            : this()
        {
            _shape = shape;
            _color = color;
        }

        public ItemIconShape Shape
        {
            get => _shape;
            set => SetVisualProperty(ref _shape, value);
        }

        public Vector4 Color
        {
            get => _color;
            set => SetVisualProperty(ref _color, value);
        }

        /// <summary>Shows an atlas region instead of the shape; <see cref="ClearImage"/> goes back to the shape.</summary>
        public void SetImage(Vector4 uv)
        {
            if (_hasImage && _uv == uv)
            {
                return;
            }
            _uv = uv;
            _hasImage = true;
            InvalidateVisual();
        }

        public void ClearImage()
        {
            _hasImage = false;
            InvalidateVisual();
        }

        public bool HasImage => _hasImage;

        protected override Vector2 MeasureOverride(Vector2 availableSize) =>
            new(float.IsFinite(availableSize.X) ? availableSize.X : 32, float.IsFinite(availableSize.Y) ? availableSize.Y : 32);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var side = MathF.Min(Bounds.Width, Bounds.Height);
            var box = new UiRect(Bounds.X + (Bounds.Width - side) / 2, Bounds.Y + (Bounds.Height - side) / 2, side, side);
            if (_hasImage)
            {
                drawList.DrawImage(box, _uv, Vector4.One, CornerRadius.Zero);
                return;
            }
            Draw(drawList, _shape, box, _color);
        }

        /// <summary>Draws a stock shape filling <paramref name="box"/>.</summary>
        public static void Draw(ManaUiDrawList d, ItemIconShape shape, UiRect box, Vector4 color)
        {
            var light = Shade(color, 1.35f);
            var dark = Shade(color, 0.6f);
            var s = box.Width;
            UiRect R(float x, float y, float w, float h) => new(box.X + x * s, box.Y + y * s, w * s, h * s);
            switch (shape)
            {
                case ItemIconShape.Ore:
                    // A heap of three lumps.
                    d.FillRect(R(0.08f, 0.42f, 0.5f, 0.42f), dark, new CornerRadius(s * 0.2f));
                    d.FillRect(R(0.42f, 0.38f, 0.5f, 0.46f), color, new CornerRadius(s * 0.22f));
                    d.FillRect(R(0.2f, 0.16f, 0.46f, 0.44f), color, new CornerRadius(s * 0.2f));
                    d.FillRect(R(0.28f, 0.22f, 0.14f, 0.1f), light, new CornerRadius(s * 0.05f));
                    break;
                case ItemIconShape.Plate:
                    d.FillRect(R(0.12f, 0.3f, 0.76f, 0.5f), dark, new CornerRadius(s * 0.06f));
                    d.FillRect(R(0.12f, 0.22f, 0.76f, 0.5f), color, new CornerRadius(s * 0.06f));
                    d.FillRect(R(0.18f, 0.27f, 0.64f, 0.06f), light, new CornerRadius(s * 0.03f));
                    break;
                case ItemIconShape.Bar:
                    d.FillRect(R(0.1f, 0.42f, 0.8f, 0.3f), dark, new CornerRadius(s * 0.08f));
                    d.FillRect(R(0.1f, 0.32f, 0.8f, 0.3f), color, new CornerRadius(s * 0.08f));
                    d.FillRect(R(0.16f, 0.36f, 0.68f, 0.06f), light, new CornerRadius(s * 0.03f));
                    break;
                case ItemIconShape.Brick:
                    d.FillRect(R(0.1f, 0.3f, 0.8f, 0.42f), dark, new CornerRadius(s * 0.04f));
                    d.FillRect(R(0.12f, 0.32f, 0.36f, 0.17f), color);
                    d.FillRect(R(0.52f, 0.32f, 0.36f, 0.17f), color);
                    d.FillRect(R(0.12f, 0.53f, 0.17f, 0.17f), color);
                    d.FillRect(R(0.33f, 0.53f, 0.34f, 0.17f), color);
                    d.FillRect(R(0.71f, 0.53f, 0.17f, 0.17f), color);
                    break;
                case ItemIconShape.Gear:
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * MathF.PI / 4;
                        var c = new Vector2(box.X + s * (0.5f + MathF.Cos(a) * 0.36f), box.Y + s * (0.5f + MathF.Sin(a) * 0.36f));
                        d.Rotated(new UiRect(c.X - s * 0.08f, c.Y - s * 0.08f, s * 0.16f, s * 0.16f), a, dark);
                    }
                    d.FillRect(R(0.16f, 0.16f, 0.68f, 0.68f), color, new CornerRadius(s * 0.34f));
                    d.FillRect(R(0.37f, 0.37f, 0.26f, 0.26f), dark, new CornerRadius(s * 0.13f));
                    break;
                case ItemIconShape.Coil:
                    for (var i = 0; i < 4; i++)
                    {
                        d.FillRect(R(0.14f + i * 0.04f, 0.18f + i * 0.13f, 0.72f - i * 0.08f, 0.14f), i % 2 == 0 ? color : light, new CornerRadius(s * 0.07f));
                    }
                    d.FillRect(R(0.44f, 0.08f, 0.12f, 0.14f), dark, new CornerRadius(s * 0.03f));
                    break;
                case ItemIconShape.Chip:
                    for (var i = 0; i < 3; i++)
                    {
                        d.FillRect(R(0.26f + i * 0.18f, 0.12f, 0.08f, 0.76f), Shade(color, 0.45f));
                        d.FillRect(R(0.12f, 0.26f + i * 0.18f, 0.76f, 0.08f), Shade(color, 0.45f));
                    }
                    d.FillRect(R(0.2f, 0.2f, 0.6f, 0.6f), color, new CornerRadius(s * 0.05f));
                    d.FillRect(R(0.32f, 0.32f, 0.36f, 0.36f), dark, new CornerRadius(s * 0.03f));
                    break;
                case ItemIconShape.Flask:
                    d.FillRect(R(0.4f, 0.1f, 0.2f, 0.3f), new Vector4(0.85f, 0.88f, 0.92f, 1), new CornerRadius(s * 0.03f));
                    d.FillRect(R(0.36f, 0.08f, 0.28f, 0.07f), new Vector4(0.6f, 0.62f, 0.66f, 1), new CornerRadius(s * 0.03f));
                    d.FillRect(R(0.18f, 0.34f, 0.64f, 0.56f), color, new CornerRadius(s * 0.26f));
                    d.FillRect(R(0.28f, 0.42f, 0.14f, 0.2f), light.WithAlpha(0.8f), new CornerRadius(s * 0.07f));
                    break;
                case ItemIconShape.Box:
                    d.FillRect(R(0.14f, 0.18f, 0.72f, 0.66f), dark, new CornerRadius(s * 0.05f));
                    d.FillRect(R(0.18f, 0.18f, 0.64f, 0.58f), color, new CornerRadius(s * 0.05f));
                    d.FillRect(R(0.18f, 0.18f, 0.64f, 0.14f), light, new CornerRadius(s * 0.05f));
                    break;
                case ItemIconShape.Arrow:
                    d.FillRect(R(0.14f, 0.42f, 0.46f, 0.16f), dark, new CornerRadius(s * 0.04f));
                    d.Rotated(R(0.42f, 0.34f, 0.32f, 0.08f), MathF.PI / 4, color, new CornerRadius(s * 0.04f));
                    d.Rotated(R(0.42f, 0.58f, 0.32f, 0.08f), -MathF.PI / 4, color, new CornerRadius(s * 0.04f));
                    d.FillRect(R(0.14f, 0.45f, 0.52f, 0.1f), color, new CornerRadius(s * 0.04f));
                    break;
                case ItemIconShape.Warning:
                    d.Rotated(R(0.2f, 0.2f, 0.6f, 0.6f), MathF.PI / 4, color, new CornerRadius(s * 0.06f));
                    d.FillRect(R(0.45f, 0.26f, 0.1f, 0.32f), dark, new CornerRadius(s * 0.04f));
                    d.FillRect(R(0.45f, 0.64f, 0.1f, 0.1f), dark, new CornerRadius(s * 0.05f));
                    break;
                case ItemIconShape.Dot:
                    d.FillRect(R(0.25f, 0.25f, 0.5f, 0.5f), color, new CornerRadius(s * 0.25f));
                    break;
                case ItemIconShape.Drop:
                    d.Rotated(R(0.31f, 0.27f, 0.38f, 0.38f), MathF.PI / 4, color, new CornerRadius(s * 0.04f));
                    d.FillRect(R(0.22f, 0.36f, 0.56f, 0.52f), color, new CornerRadius(s * 0.26f));
                    d.FillRect(R(0.32f, 0.52f, 0.1f, 0.2f), light.WithAlpha(0.8f), new CornerRadius(s * 0.05f));
                    break;
                case ItemIconShape.Pipe:
                    d.FillRect(R(0.08f, 0.38f, 0.84f, 0.24f), color, new CornerRadius(s * 0.04f));
                    d.FillRect(R(0.08f, 0.38f, 0.84f, 0.07f), light, new CornerRadius(s * 0.03f));
                    d.FillRect(R(0.16f, 0.3f, 0.09f, 0.4f), dark, new CornerRadius(s * 0.03f));
                    d.FillRect(R(0.75f, 0.3f, 0.09f, 0.4f), dark, new CornerRadius(s * 0.03f));
                    break;
            }
        }

        private static Vector4 Shade(Vector4 c, float f) => new(Math.Clamp(c.X * f, 0, 1), Math.Clamp(c.Y * f, 0, 1), Math.Clamp(c.Z * f, 0, 1), c.W);
    }
}
