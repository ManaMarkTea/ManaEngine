namespace ManaUI
{
    /// <summary>
    /// Records a frame of UI as a flat, ordered list of <see cref="ManaUiInstance"/>. Elements append to it
    /// during the render walk; a backend consumes it once and draws it.
    /// <para>
    /// Order is painter's order -- instances blend in submission sequence, which the rasteriser guarantees
    /// within a draw call, so a panel appended after a glyph correctly covers it.
    /// </para>
    /// The list is pooled and reused every frame; <see cref="Reset"/> keeps the backing arrays.
    /// </summary>
    public sealed class ManaUiDrawList
    {
        private ManaUiInstance[] _instances = new ManaUiInstance[1024];
        private int _instanceCount;

        private readonly List<ManaUiDrawBatch> _batches = new();
        private readonly Stack<UiRect> _clipStack = new();
        private readonly Stack<float> _opacityStack = new();

        private UiRect _clip = UiRect.Infinite;
        private float _opacity = 1.0f;
        private int _currentTextureId;
        private int _currentBatchStart;

        public ReadOnlySpan<ManaUiInstance> Instances => _instances.AsSpan(0, _instanceCount);
        public IReadOnlyList<ManaUiDrawBatch> Batches => _batches;
        public int InstanceCount => _instanceCount;

        /// <summary>The clip currently in force, in logical pixels. Elements can use it to cull.</summary>
        public UiRect CurrentClip => _clip;

        public void Reset()
        {
            _instanceCount = 0;
            _batches.Clear();
            _clipStack.Clear();
            _opacityStack.Clear();
            _clip = UiRect.Infinite;
            _opacity = 1.0f;
            _currentTextureId = 0;
            _currentBatchStart = 0;
            BackdropRequested = false;
        }

        /// <summary>Closes the trailing batch. Called by the UI root once the tree has finished rendering.</summary>
        public void Finish()
        {
            FlushBatch();
        }

        // ----- clipping ------------------------------------------------------------------------------

        /// <summary>
        /// Intersects the clip with <paramref name="rect"/>. Always pair with <see cref="PopClip"/>; nesting
        /// intersects rather than replaces, so a child can never draw outside its parent.
        /// </summary>
        public void PushClip(UiRect rect)
        {
            _clipStack.Push(_clip);
            _clip = _clip.Intersect(rect);
        }

        public void PopClip()
        {
            _clip = _clipStack.Count > 0 ? _clipStack.Pop() : UiRect.Infinite;
        }

        /// <summary>True when the clip has collapsed to nothing, meaning a subtree can be skipped entirely.</summary>
        public bool IsClippedOut => _clip.IsEmpty;

        // ----- opacity -------------------------------------------------------------------------------

        /// <summary>
        /// Multiplies into the current opacity, applied to every colour at record time. This is a cheap
        /// approximation of a real opacity group: overlapping children inside the subtree will show through
        /// each other rather than compositing as a unit.
        /// </summary>
        public void PushOpacity(float opacity)
        {
            _opacityStack.Push(_opacity);
            _opacity *= Math.Clamp(opacity, 0.0f, 1.0f);
        }

        public void PopOpacity()
        {
            _opacity = _opacityStack.Count > 0 ? _opacityStack.Pop() : 1.0f;
        }

        // ----- texture batching ----------------------------------------------------------------------

        /// <summary>
        /// Switches to a renderer-owned texture for subsequent draws, splitting the batch. Pass 0 to return
        /// to the shared atlases.
        /// </summary>
        public void SetTexture(int textureId)
        {
            if (textureId == _currentTextureId)
            {
                return;
            }

            FlushBatch();
            _currentTextureId = textureId;
        }

        // ----- primitives ----------------------------------------------------------------------------

        public void FillRect(UiRect rect, Vector4 color)
        {
            FillRect(rect, color, CornerRadius.Zero);
        }

        public void FillRect(UiRect rect, Vector4 color, CornerRadius corners)
        {
            if (color.W <= 0.0f)
            {
                return;
            }

            Add(rect, corners, color, UiColor.Transparent, 0.0f, Vector4.Zero, ManaUiInstanceFlags.None);
        }

        /// <summary>
        /// A vertical gradient fill, top colour to bottom. Still one instance -- the second colour rides
        /// on the same box rather than costing a second draw.
        /// </summary>
        public void FillRect(UiRect rect, Vector4 top, Vector4 bottom, CornerRadius corners)
        {
            if (top.W <= 0.0f && bottom.W <= 0.0f)
            {
                return;
            }

            Add(rect, corners, top, UiColor.Transparent, 0.0f, Vector4.Zero, ManaUiInstanceFlags.None, bottom);
        }

        /// <summary>
        /// The largest blur radius a backend can actually deliver.
        /// <para>
        /// The blur is a mip chain and the radius selects a level as log2 of it, so this is 2^(levels - 1).
        /// Asking for more does not fail -- it clamps to the last level, silently, which looks like the blur
        /// simply stopped getting stronger. Both backends cap their chain to match.
        /// </para>
        /// </summary>
        public const float MaxBackdropBlur = 32.0f;

        /// <summary>
        /// True when anything in this frame asked for a blurred backdrop.
        /// <para>
        /// The backend reads this to decide whether to snapshot and blur the frame at all. Blurring
        /// unconditionally would make every UI pay for a feature almost none of them use.
        /// </para>
        /// </summary>
        public bool BackdropRequested { get; private set; }

        /// <summary>
        /// A box filled with a blurred snapshot of what is behind the UI, tinted by <paramref name="tint"/>.
        /// <para>
        /// The tint's alpha is how much of it covers the blur: opaque hides it entirely, so a panel that
        /// wants the effect wants a translucent one.
        /// </para>
        /// </summary>
        public void FillBackdrop(UiRect rect, Vector4 tint, float blur, CornerRadius corners)
        {
            Add(rect, corners, tint, UiColor.Transparent, 0.0f, Vector4.Zero, ManaUiInstanceFlags.None,
                gradientTo: null, rotation: 0.0f, backdropBlur: blur);
        }

        /// <summary>Fill, gradient and inset border in one instance.</summary>
        public void DrawBox(
            UiRect rect,
            Vector4 top,
            Vector4 bottom,
            Vector4 border,
            float borderThickness,
            CornerRadius corners,
            float backdropBlur = 0.0f)
        {
            var hasFill = top.W > 0.0f || bottom.W > 0.0f;
            var hasBorder = borderThickness > 0.0f && border.W > 0.0f;

            // A blurred backdrop is worth drawing even under a fully transparent tint -- the blur is the
            // point, and the tint only says how much of it to cover.
            if (!hasFill && !hasBorder && backdropBlur <= 0.0f)
            {
                return;
            }

            Add(
                rect,
                corners,
                top,
                hasBorder ? border : UiColor.Transparent,
                hasBorder ? borderThickness : 0.0f,
                Vector4.Zero,
                ManaUiInstanceFlags.None,
                bottom,
                rotation: 0.0f,
                backdropBlur: backdropBlur);
        }

        /// <summary>Draws an inset border only, leaving the interior untouched.</summary>
        public void DrawBorder(UiRect rect, Vector4 color, float thickness, CornerRadius corners)
        {
            if (thickness <= 0.0f || color.W <= 0.0f)
            {
                return;
            }

            Add(rect, corners, UiColor.Transparent, color, thickness, Vector4.Zero, ManaUiInstanceFlags.None);
        }

        /// <summary>Fill and inset border in one instance -- the common case for panels, buttons and inputs.</summary>
        public void DrawBox(
            UiRect rect,
            Vector4 fill,
            Vector4 border,
            float borderThickness,
            CornerRadius corners,
            float backdropBlur = 0.0f)
        {
            var hasFill = fill.W > 0.0f;
            var hasBorder = borderThickness > 0.0f && border.W > 0.0f;

            // A blurred backdrop is worth drawing even under a fully transparent fill -- the blur is the
            // point, and the fill only says how much of it to cover.
            if (!hasFill && !hasBorder && backdropBlur <= 0.0f)
            {
                return;
            }

            Add(rect, corners, fill, border, hasBorder ? borderThickness : 0.0f, Vector4.Zero,
                ManaUiInstanceFlags.None, gradientTo: null, rotation: 0.0f, backdropBlur: backdropBlur);
        }

        /// <summary>
        /// An outer drop shadow of the given box. Drawn as its own instance, so submit it before the box it
        /// sits under.
        /// </summary>
        public void DrawShadow(UiRect rect, Vector4 color, float blur, CornerRadius corners)
        {
            if (blur <= 0.0f || color.W <= 0.0f)
            {
                return;
            }

            // Expanded so the blurred falloff is not cut off by the instance's own quad.
            var bounds = rect.Inflate(new Thickness(blur * 1.5f));
            Add(bounds, corners, color, UiColor.Transparent, blur, Vector4.Zero, ManaUiInstanceFlags.Shadow);

            // Shadow geometry needs the original box, which the shader reconstructs by deflating -- so the
            // inflation above and the shader's must agree. Encoded via Uv to avoid growing the struct.
            ref var instance = ref _instances[_instanceCount - 1];
            instance.Uv = rect.ToEdgeVector();
        }

        /// <summary>A single glyph quad. <paramref name="uv"/> is normalised into the glyph atlas.</summary>
        public void DrawGlyph(UiRect rect, Vector4 uv, Vector4 color)
        {
            if (color.W <= 0.0f)
            {
                return;
            }

            Add(rect, CornerRadius.Zero, color, UiColor.Transparent, 0.0f, uv, ManaUiInstanceFlags.GlyphAtlas);
        }

        public void DrawImage(UiRect rect, Vector4 uv, Vector4 tint, CornerRadius corners)
        {
            Add(rect, corners, tint, UiColor.Transparent, 0.0f, uv, ManaUiInstanceFlags.Texture);
        }

        /// <summary>
        /// A picture stretched as a nine-slice, tinted. <paramref name="scale"/> shrinks the corners (a 64 px
        /// picture under a 24 px control wants about 0.4); they are never wider than half the rectangle.
        /// </summary>
        public void DrawNineSlice(UiRect rect, UiSkinSlice slice, Vector4 tint, float scale = 1.0f)
        {
            if (tint.W <= 0.0f || rect.Width <= 0.0f || rect.Height <= 0.0f)
            {
                return;
            }

            var insets = slice.Insets;
            var left = MathF.Min(insets.Left * scale, rect.Width * 0.5f);
            var right = MathF.Min(insets.Right * scale, rect.Width * 0.5f);
            var top = MathF.Min(insets.Top * scale, rect.Height * 0.5f);
            var bottom = MathF.Min(insets.Bottom * scale, rect.Height * 0.5f);

            // Source pixel edges of the three columns and rows, as fractions of the picture.
            float[] u = { 0.0f, insets.Left / slice.Size.X, 1.0f - insets.Right / slice.Size.X, 1.0f };
            float[] v = { 0.0f, insets.Top / slice.Size.Y, 1.0f - insets.Bottom / slice.Size.Y, 1.0f };
            float[] x = { rect.X, rect.X + left, rect.Right - right, rect.Right };
            float[] y = { rect.Y, rect.Y + top, rect.Bottom - bottom, rect.Bottom };

            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    var width = x[column + 1] - x[column];
                    var height = y[row + 1] - y[row];
                    if (width <= 0.0f || height <= 0.0f)
                    {
                        continue;
                    }

                    var uv = new Vector4(
                        slice.Uv.X + (slice.Uv.Z - slice.Uv.X) * u[column],
                        slice.Uv.Y + (slice.Uv.W - slice.Uv.Y) * v[row],
                        slice.Uv.X + (slice.Uv.Z - slice.Uv.X) * u[column + 1],
                        slice.Uv.Y + (slice.Uv.W - slice.Uv.Y) * v[row + 1]);
                    DrawImage(new UiRect(x[column], y[row], width, height), uv, tint, CornerRadius.Zero);
                }
            }
        }

        /// <summary>
        /// A line at any angle, drawn as one rotated box. Rounded ends by default, which is what makes short
        /// thick segments read as strokes rather than as bricks.
        /// </summary>
        public void DrawLine(Vector2 from, Vector2 to, Vector4 color, float thickness, bool rounded = true)
        {
            var half = thickness * 0.5f;

            // Axis-aligned lines skip the rotation entirely. They are almost all of them -- dividers, rules,
            // underlines -- and an unrotated instance costs the shader nothing.
            if (MathF.Abs(from.Y - to.Y) < 0.5f)
            {
                var left = MathF.Min(from.X, to.X);
                var right = MathF.Max(from.X, to.X);
                FillRect(UiRect.FromEdges(left, from.Y - half, right, from.Y + half), color);
                return;
            }

            if (MathF.Abs(from.X - to.X) < 0.5f)
            {
                var top = MathF.Min(from.Y, to.Y);
                var bottom = MathF.Max(from.Y, to.Y);
                FillRect(UiRect.FromEdges(from.X - half, top, from.X + half, bottom), color);
                return;
            }

            var delta = to - from;
            var length = delta.Length();
            if (length <= 0.0f)
            {
                return;
            }

            var centre = (from + to) * 0.5f;
            var rect = UiRect.FromEdges(
                centre.X - length * 0.5f,
                centre.Y - half,
                centre.X + length * 0.5f,
                centre.Y + half);

            Rotated(rect, MathF.Atan2(delta.Y, delta.X), color,
                rounded ? new CornerRadius(half) : default);
        }

        /// <summary>A filled box turned about its own centre. The angle is in radians, clockwise.</summary>
        public void Rotated(UiRect rect, float rotation, Vector4 color, CornerRadius corners = default)
        {
            Add(rect, corners, color, default, 0.0f, Vector4.Zero, ManaUiInstanceFlags.None,
                gradientTo: null, rotation: rotation);
        }

        /// <summary>Bounds a rotated box actually covers, for clipping and hit testing.</summary>
        public static UiRect RotatedBounds(UiRect rect, float rotation)
        {
            var centre = rect.Center;
            var halfWidth = rect.Width * 0.5f;
            var halfHeight = rect.Height * 0.5f;

            var cos = MathF.Abs(MathF.Cos(rotation));
            var sin = MathF.Abs(MathF.Sin(rotation));

            var boundX = cos * halfWidth + sin * halfHeight;
            var boundY = sin * halfWidth + cos * halfHeight;

            return UiRect.FromEdges(centre.X - boundX, centre.Y - boundY, centre.X + boundX, centre.Y + boundY);
        }

        // ----- internals -----------------------------------------------------------------------------

        private void Add(
            UiRect rect,
            CornerRadius corners,
            Vector4 fill,
            Vector4 border,
            float borderWidth,
            Vector4 uv,
            ManaUiInstanceFlags flags,
            Vector4? gradientTo = null,
            float rotation = 0.0f,
            float backdropBlur = 0.0f)
        {
            // Cheap rejection before the instance is ever built. Shadows are exempt: their quad is inflated
            // past the clip on purpose and the falloff inside it is still visible. A rotated box is tested
            // against its own bounds rather than its rect, or a diagonal line would be culled by a clip it
            // actually crosses.
            var bounds = rotation == 0.0f ? rect : RotatedBounds(rect, rotation);

            if (_clip.IsEmpty || (!flags.HasFlag(ManaUiInstanceFlags.Shadow) && !bounds.IntersectsWith(_clip)))
            {
                return;
            }

            if (_instanceCount == _instances.Length)
            {
                Array.Resize(ref _instances, _instances.Length * 2);
            }

            var gradient = gradientTo;

            if (_opacity < 1.0f)
            {
                fill.W *= _opacity;
                border.W *= _opacity;

                if (gradient is { } fading)
                {
                    fading.W *= _opacity;
                    gradient = fading;
                }
            }

            if (gradient != null)
            {
                flags |= ManaUiInstanceFlags.Gradient;
            }

            if (backdropBlur > 0.0f)
            {
                flags |= ManaUiInstanceFlags.Backdrop;
                BackdropRequested = true;
            }

            _instances[_instanceCount++] = new ManaUiInstance
            {
                Rect = rect.ToEdgeVector(),
                Uv = uv,
                Corners = corners.ToVector4(),
                Clip = _clip.ToEdgeVector(),
                Rotation = rotation,
                BackdropBlur = backdropBlur,
                FillColor = UiColor.Pack(fill),
                BorderColor = UiColor.Pack(border),
                BorderWidth = borderWidth,
                Flags = (uint)flags,
                GradientColor = UiColor.Pack(gradient ?? fill)
            };
        }

        private void FlushBatch()
        {
            var count = _instanceCount - _currentBatchStart;
            if (count > 0)
            {
                _batches.Add(new ManaUiDrawBatch(_currentBatchStart, count, _currentTextureId));
            }

            _currentBatchStart = _instanceCount;
        }
    }
}
