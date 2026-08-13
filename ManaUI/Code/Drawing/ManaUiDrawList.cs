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

        /// <summary>Fill, gradient and inset border in one instance.</summary>
        public void DrawBox(
            UiRect rect,
            Vector4 top,
            Vector4 bottom,
            Vector4 border,
            float borderThickness,
            CornerRadius corners)
        {
            var hasFill = top.W > 0.0f || bottom.W > 0.0f;
            var hasBorder = borderThickness > 0.0f && border.W > 0.0f;

            if (!hasFill && !hasBorder)
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
                bottom);
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
        public void DrawBox(UiRect rect, Vector4 fill, Vector4 border, float borderThickness, CornerRadius corners)
        {
            var hasFill = fill.W > 0.0f;
            var hasBorder = borderThickness > 0.0f && border.W > 0.0f;
            if (!hasFill && !hasBorder)
            {
                return;
            }

            Add(rect, corners, fill, border, hasBorder ? borderThickness : 0.0f, Vector4.Zero, ManaUiInstanceFlags.None);
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
            float rotation = 0.0f)
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

            _instances[_instanceCount++] = new ManaUiInstance
            {
                Rect = rect.ToEdgeVector(),
                Uv = uv,
                Corners = corners.ToVector4(),
                Clip = _clip.ToEdgeVector(),
                Rotation = rotation,
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
