namespace ManaUI
{
    /// <summary>A laid-out line, as a slice of the source text plus its measured width.</summary>
    public readonly struct TextLine
    {
        public readonly int Start;
        public readonly int Length;
        public readonly float Width;

        /// <summary>True when the line was cut short and should render a trailing ellipsis.</summary>
        public readonly bool IsTrimmed;

        public TextLine(int start, int length, float width, bool isTrimmed)
        {
            Start = start;
            Length = length;
            Width = width;
            IsTrimmed = isTrimmed;
        }
    }

    /// <summary>
    /// Breaks a string into lines and draws them.
    /// <para>
    /// Held and reused by whatever owns the text, because measure, arrange and render all need the same
    /// answer within a frame -- <see cref="Update"/> recomputes only when an input actually changed, so
    /// calling it from all three passes is free after the first.
    /// </para>
    /// </summary>
    public sealed class TextLayout
    {
        private const string Ellipsis = "…";

        private readonly List<TextLine> _lines = new();

        private ManaFont? _font;
        private string _text = string.Empty;
        private float _maxWidth = float.PositiveInfinity;
        private TextWrapping _wrapping = TextWrapping.NoWrap;
        private TextTrimming _trimming = TextTrimming.None;
        private float _lineSpacing = 1.0f;

        private bool _dirty = true;
        private int _atlasGeneration = -1;

        public ManaFont? Font
        {
            get => _font;
            set => Set(ref _font, value);
        }

        public string Text
        {
            get => _text;
            set => Set(ref _text, value ?? string.Empty);
        }

        /// <summary>Wrapping/trimming width. Infinity means unconstrained.</summary>
        public float MaxWidth
        {
            get => _maxWidth;
            set => Set(ref _maxWidth, value);
        }

        public TextWrapping Wrapping
        {
            get => _wrapping;
            set => Set(ref _wrapping, value);
        }

        public TextTrimming Trimming
        {
            get => _trimming;
            set => Set(ref _trimming, value);
        }

        public TextAlignment Alignment { get; set; } = TextAlignment.Left;

        /// <summary>Multiplier on the font's natural line height.</summary>
        public float LineSpacing
        {
            get => _lineSpacing;
            set => Set(ref _lineSpacing, value);
        }

        /// <summary>Measured size after <see cref="Update"/>.</summary>
        public Vector2 Size { get; private set; }

        public IReadOnlyList<TextLine> Lines => _lines;

        public float LineHeight => _font == null ? 0.0f : _font.LineHeight * _lineSpacing;

        /// <summary>Recomputes the line break if any input changed. Cheap to call repeatedly.</summary>
        public void Update()
        {
            // Glyph advances come from the atlas-backed font; a reset invalidates the measurements too.
            if (_font != null && _atlasGeneration != _font.Atlas.Generation)
            {
                _atlasGeneration = _font.Atlas.Generation;
                _dirty = true;
            }

            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            _lines.Clear();

            if (_font == null)
            {
                Size = Vector2.Zero;
                return;
            }

            if (_text.Length == 0)
            {
                // An empty string still occupies one line's height, so an empty field keeps its box.
                Size = new Vector2(0.0f, LineHeight);
                return;
            }

            var maxLineWidth = 0.0f;
            var start = 0;

            while (start <= _text.Length)
            {
                var hardBreak = _text.IndexOf('\n', start);
                var segmentEnd = hardBreak < 0 ? _text.Length : hardBreak;

                // Trailing \r on CRLF input would otherwise render as a box glyph.
                var trimmedEnd = segmentEnd > start && _text[segmentEnd - 1] == '\r' ? segmentEnd - 1 : segmentEnd;

                BreakSegment(start, trimmedEnd, ref maxLineWidth);

                if (hardBreak < 0)
                {
                    break;
                }

                start = hardBreak + 1;

                // A trailing newline produces a final empty line, matching every text editor.
                if (start == _text.Length)
                {
                    _lines.Add(new TextLine(start, 0, 0.0f, false));
                    break;
                }
            }

            Size = new Vector2(maxLineWidth, _lines.Count * LineHeight);
        }

        /// <summary>Breaks one hard-delimited segment into wrapped lines.</summary>
        private void BreakSegment(int start, int end, ref float maxLineWidth)
        {
            var font = _font!;
            var text = _text.AsSpan();

            if (_wrapping == TextWrapping.NoWrap)
            {
                var line = BuildLine(start, end - start);
                maxLineWidth = MathF.Max(maxLineWidth, line.Width);
                _lines.Add(line);
                return;
            }

            var lineStart = start;

            while (lineStart < end)
            {
                var width = 0.0f;
                var index = lineStart;
                var lastBreak = -1;
                var previous = 0;

                while (index < end)
                {
                    var codepoint = char.ConvertToUtf32(_text, index);
                    var charLength = char.IsHighSurrogate(text[index]) && index + 1 < end ? 2 : 1;

                    if (previous != 0)
                    {
                        width += font.GetKerning(previous, codepoint);
                    }

                    var advance = font.GetGlyph(codepoint).Advance;

                    // Overflow: fall back to the last space, or split mid-word if the word alone is too long.
                    if (width + advance > _maxWidth && index > lineStart)
                    {
                        break;
                    }

                    width += advance;
                    previous = codepoint;

                    if (char.IsWhiteSpace(text[index]))
                    {
                        lastBreak = index;
                    }

                    index += charLength;
                }

                if (index >= end)
                {
                    var line = BuildLine(lineStart, end - lineStart);
                    maxLineWidth = MathF.Max(maxLineWidth, line.Width);
                    _lines.Add(line);
                    return;
                }

                var breakAt = lastBreak > lineStart ? lastBreak : index;
                var completed = BuildLine(lineStart, breakAt - lineStart);
                maxLineWidth = MathF.Max(maxLineWidth, completed.Width);
                _lines.Add(completed);

                // Whitespace at a wrap point is consumed rather than starting the next line.
                lineStart = breakAt;
                while (lineStart < end && char.IsWhiteSpace(text[lineStart]) && text[lineStart] != '\n')
                {
                    lineStart++;
                }
            }
        }

        /// <summary>Measures a slice, applying ellipsis trimming when it overflows and trimming is on.</summary>
        private TextLine BuildLine(int start, int length)
        {
            var font = _font!;
            length = Math.Max(0, length);
            var width = font.MeasureWidth(_text.AsSpan(start, length));

            if (_trimming != TextTrimming.CharacterEllipsis || width <= _maxWidth || float.IsInfinity(_maxWidth))
            {
                return new TextLine(start, length, width, false);
            }

            var ellipsisWidth = font.MeasureWidth(Ellipsis);
            var budget = _maxWidth - ellipsisWidth;
            var kept = 0;
            var keptWidth = 0.0f;
            var previous = 0;

            while (kept < length)
            {
                var codepoint = char.ConvertToUtf32(_text, start + kept);
                var charLength = char.IsHighSurrogate(_text[start + kept]) && kept + 1 < length ? 2 : 1;

                var advance = font.GetGlyph(codepoint).Advance
                    + (previous != 0 ? font.GetKerning(previous, codepoint) : 0.0f);

                if (keptWidth + advance > budget)
                {
                    break;
                }

                keptWidth += advance;
                previous = codepoint;
                kept += charLength;
            }

            return new TextLine(start, kept, keptWidth + ellipsisWidth, true);
        }

        // ----- rendering -----------------------------------------------------------------------------

        /// <summary>Draws the text inside <paramref name="bounds"/>, aligned per Alignment and the given vertical rule.</summary>
        public void Render(
            ManaUiDrawList drawList,
            UiRect bounds,
            Vector4 color,
            VerticalAlignment verticalAlignment = VerticalAlignment.Top)
        {
            Update();

            if (_font == null || _lines.Count == 0 || color.W <= 0.0f)
            {
                return;
            }

            var lineHeight = LineHeight;
            var totalHeight = _lines.Count * lineHeight;

            var y = verticalAlignment switch
            {
                VerticalAlignment.Center => bounds.Y + (bounds.Height - totalHeight) * 0.5f,
                VerticalAlignment.Bottom => bounds.Bottom - totalHeight,
                _ => bounds.Y
            };

            // Device-pixel-aligned baselines: a fractional offset visibly blurs bitmap glyphs at UI sizes.
            y = _font.SnapToPixel(y);

            foreach (var line in _lines)
            {
                var x = Alignment switch
                {
                    TextAlignment.Center => bounds.X + (bounds.Width - line.Width) * 0.5f,
                    TextAlignment.Right => bounds.Right - line.Width,
                    _ => bounds.X
                };

                RenderLine(drawList, line, _font.SnapToPixel(x), y + _font.Ascent, color);
                y += lineHeight;
            }
        }

        private void RenderLine(ManaUiDrawList drawList, TextLine line, float penX, float baselineY, Vector4 color)
        {
            var font = _font!;
            var previous = 0;

            foreach (var codepoint in new CodepointEnumerator(_text.AsSpan(line.Start, line.Length)))
            {
                if (previous != 0)
                {
                    penX += font.GetKerning(previous, codepoint);
                }

                var glyph = font.GetGlyph(codepoint);
                if (glyph.HasPixels)
                {
                    var quad = new UiRect(
                        font.SnapToPixel(penX + glyph.Offset.X),
                        font.SnapToPixel(baselineY + glyph.Offset.Y),
                        glyph.Size.X,
                        glyph.Size.Y);

                    drawList.DrawGlyph(quad, glyph.AtlasUv, color);
                }

                penX += glyph.Advance;
                previous = codepoint;
            }

            if (!line.IsTrimmed)
            {
                return;
            }

            foreach (var codepoint in new CodepointEnumerator(Ellipsis))
            {
                var glyph = font.GetGlyph(codepoint);
                if (glyph.HasPixels)
                {
                    drawList.DrawGlyph(
                        new UiRect(
                            font.SnapToPixel(penX + glyph.Offset.X),
                            font.SnapToPixel(baselineY + glyph.Offset.Y),
                            glyph.Size.X,
                            glyph.Size.Y),
                        glyph.AtlasUv,
                        color);
                }

                penX += glyph.Advance;
            }
        }

        // ----- caret / hit testing (used by editable text) ---------------------------------------------

        /// <summary>Character index nearest <paramref name="point"/>, expressed relative to the text origin.</summary>
        public int GetIndexAt(Vector2 point)
        {
            Update();

            if (_font == null || _lines.Count == 0)
            {
                return 0;
            }

            var lineIndex = Math.Clamp((int)(point.Y / LineHeight), 0, _lines.Count - 1);
            var line = _lines[lineIndex];

            var x = 0.0f;
            var previous = 0;
            var index = line.Start;

            foreach (var codepoint in new CodepointEnumerator(_text.AsSpan(line.Start, line.Length)))
            {
                if (previous != 0)
                {
                    x += _font.GetKerning(previous, codepoint);
                }

                var advance = _font.GetGlyph(codepoint).Advance;

                // Past the midpoint of a glyph the caret belongs after it, which is what makes clicking
                // near a character boundary land where the user expects.
                if (point.X < x + advance * 0.5f)
                {
                    return index;
                }

                x += advance;
                previous = codepoint;
                index += codepoint > 0xFFFF ? 2 : 1;
            }

            return line.Start + line.Length;
        }

        /// <summary>Caret origin (top of the line) for a character index, relative to the text origin.</summary>
        public Vector2 GetCaretPosition(int index)
        {
            Update();

            if (_font == null || _lines.Count == 0)
            {
                return Vector2.Zero;
            }

            index = Math.Clamp(index, 0, _text.Length);

            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                if (index > line.Start + line.Length && i < _lines.Count - 1)
                {
                    continue;
                }

                var offset = Math.Clamp(index - line.Start, 0, line.Length);
                return new Vector2(_font.MeasureWidth(_text.AsSpan(line.Start, offset)), i * LineHeight);
            }

            return Vector2.Zero;
        }

        private void Set<T>(ref T field, T value)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                _dirty = true;
            }
        }
    }
}
