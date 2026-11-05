using System.Text;

namespace ManaUI
{
    /// <summary>A coloured stretch of one line, as a <see cref="TextArea.LineColorizer"/> returns it.</summary>
    public readonly record struct TextSpanColor(int Start, int Length, Vector4 Color);

    /// <summary>
    /// A multi-line code/text editor: monospace, line numbers, caret and selection across lines, undo and redo,
    /// auto-indent (braces open and close tidily), Tab indenting and scrolling.
    /// <para>
    /// Text is held with <c>\n</c> line ends and no tab characters (tabs become spaces as they come in), so an
    /// index into the text and a column on screen always agree. It never wraps: long lines scroll sideways, which
    /// is what code wants. <see cref="ErrorLine"/> marks a line red; <see cref="LineColorizer"/> lets the host
    /// colour tokens without ManaUI knowing any language.
    /// </para>
    /// </summary>
    public class TextArea : Control
    {
        private const int MaxUndo = 300;

        private readonly TextLayout _layout = new();
        private readonly List<int> _lineStarts = new() { 0 };
        private readonly Stack<Snapshot> _undo = new();
        private readonly Stack<Snapshot> _redo = new();

        private string _text = string.Empty;
        private int _caret;
        private int _anchor;
        private float _desiredX = -1.0f;
        private float _scrollX;
        private float _scrollY;
        private bool _revealCaret;
        private bool _selecting;
        private string? _lastEditKind;
        private int _errorLine;
        private bool _showLineNumbers = true;
        private int _tabSize = 4;

        private readonly record struct Snapshot(string Text, int Caret, int Anchor);

        public TextArea()
        {
            Focusable = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            ClipToBounds = true;
        }

        /// <summary>Raised after every edit, including undo and redo.</summary>
        public event EventHandler<string>? TextChanged;

        public string Text
        {
            get => _text;
            set
            {
                var normalized = Normalize(value ?? string.Empty);
                if (_text == normalized)
                {
                    return;
                }

                // Setting the text from outside is a fresh start, not something Ctrl+Z should walk back into.
                _undo.Clear();
                _redo.Clear();
                _lastEditKind = null;
                SetTextCore(normalized);
                _caret = Math.Clamp(_caret, 0, _text.Length);
                _anchor = _caret;
                InvalidateVisual();
                TextChanged?.Invoke(this, _text);
            }
        }

        public bool IsReadOnly { get; set; }

        public int TabSize
        {
            get => _tabSize;
            set => _tabSize = Math.Clamp(value, 1, 16);
        }

        public bool ShowLineNumbers
        {
            get => _showLineNumbers;
            set => SetVisualProperty(ref _showLineNumbers, value);
        }

        /// <summary>1-based line to mark as holding an error; 0 for none.</summary>
        public int ErrorLine
        {
            get => _errorLine;
            set => SetVisualProperty(ref _errorLine, Math.Max(value, 0));
        }

        /// <summary>Optional: the coloured spans of one line (its text without the line end). Uncovered text uses the theme's colour.</summary>
        public Func<string, IReadOnlyList<TextSpanColor>>? LineColorizer { get; set; }

        public override bool WantsAllKeyboardInput => true;

        public int LineCount => _lineStarts.Count;

        public int CaretIndex
        {
            get => _caret;
            set
            {
                _caret = Math.Clamp(value, 0, _text.Length);
                _anchor = _caret;
                _desiredX = -1.0f;
                _revealCaret = true;
                InvalidateVisual();
            }
        }

        /// <summary>1-based, for a status line.</summary>
        public int CaretLine => LineOf(_caret) + 1;

        /// <summary>1-based, for a status line.</summary>
        public int CaretColumn => _caret - _lineStarts[LineOf(_caret)] + 1;

        public bool HasSelection => _anchor != _caret;
        public string SelectedText
        {
            get
            {
                if (!HasSelection)
                {
                    return string.Empty;
                }
                return _text.Substring(SelectionStart, SelectionLength);
            }
        }
        private int SelectionStart => Math.Min(_caret, _anchor);
        private int SelectionLength => Math.Abs(_caret - _anchor);

        public void SelectAll()
        {
            _anchor = 0;
            _caret = _text.Length;
            _revealCaret = true;
            InvalidateVisual();
        }

        /// <summary>Puts the caret at the start of a 1-based line and scrolls to it.</summary>
        public void GoToLine(int line)
        {
            CaretIndex = _lineStarts[Math.Clamp(line - 1, 0, _lineStarts.Count - 1)];
        }

        protected override Thickness ContentInset => Padding.IsZero ? Theme.ControlPadding : Padding;

        // ----- text model ------------------------------------------------------------------------------

        private string Normalize(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Contains('\t'))
            {
                text = text.Replace("\t", new string(' ', _tabSize));
            }
            return text;
        }

        private void SetTextCore(string text)
        {
            _text = text;
            _lineStarts.Clear();
            _lineStarts.Add(0);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    _lineStarts.Add(i + 1);
                }
            }
        }

        private int LineOf(int index)
        {
            var found = _lineStarts.BinarySearch(index);
            if (found >= 0)
            {
                return found;
            }
            return ~found - 1;
        }

        private int LineEnd(int line)
        {
            if (line + 1 < _lineStarts.Count)
            {
                return _lineStarts[line + 1] - 1;
            }
            return _text.Length;
        }

        private string LineText(int line)
        {
            var start = _lineStarts[line];
            return _text.Substring(start, LineEnd(line) - start);
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        // ----- edits -----------------------------------------------------------------------------------

        private void PushUndo(string? kind)
        {
            // A run of typing (or of deleting) is one undo step.
            if (kind == null || kind != _lastEditKind)
            {
                _undo.Push(new Snapshot(_text, _caret, _anchor));
                if (_undo.Count > MaxUndo)
                {
                    var keep = _undo.ToArray().Take(MaxUndo).Reverse().ToArray();
                    _undo.Clear();
                    foreach (var snapshot in keep)
                    {
                        _undo.Push(snapshot);
                    }
                }
            }
            _lastEditKind = kind;
            _redo.Clear();
        }

        /// <summary>Replaces a range and puts the caret at <paramref name="caretAfter"/>, as one edit.</summary>
        private void Edit(int start, int length, string insert, int caretAfter, string? kind = null)
        {
            if (IsReadOnly)
            {
                return;
            }

            PushUndo(kind);
            var builder = new StringBuilder(_text.Length - length + insert.Length);
            builder.Append(_text, 0, start).Append(insert).Append(_text, start + length, _text.Length - start - length);
            SetTextCore(builder.ToString());
            _caret = Math.Clamp(caretAfter, 0, _text.Length);
            _anchor = _caret;
            _desiredX = -1.0f;
            _revealCaret = true;
            InvalidateVisual();
            TextChanged?.Invoke(this, _text);
        }

        /// <summary>Types text over the selection.</summary>
        public void InsertText(string text, string? kind = null)
        {
            text = Normalize(text);
            Edit(SelectionStart, SelectionLength, text, SelectionStart + text.Length, kind);
        }

        private void DeleteSelection()
        {
            Edit(SelectionStart, SelectionLength, string.Empty, SelectionStart);
        }

        private void RestoreFrom(Stack<Snapshot> source, Stack<Snapshot> opposite)
        {
            if (source.Count == 0 || IsReadOnly)
            {
                return;
            }

            opposite.Push(new Snapshot(_text, _caret, _anchor));
            var snapshot = source.Pop();
            SetTextCore(snapshot.Text);
            _caret = Math.Clamp(snapshot.Caret, 0, _text.Length);
            _anchor = Math.Clamp(snapshot.Anchor, 0, _text.Length);
            _lastEditKind = null;
            _revealCaret = true;
            InvalidateVisual();
            TextChanged?.Invoke(this, _text);
        }

        private string LeadingWhitespace(int line)
        {
            var text = LineText(line);
            var count = 0;
            while (count < text.Length && text[count] == ' ')
            {
                count++;
            }
            return text.Substring(0, count);
        }

        private void InsertNewline()
        {
            var line = LineOf(SelectionStart);
            var indent = LeadingWhitespace(line);
            var before = _text.Substring(0, SelectionStart).TrimEnd(' ');
            var after = _text.Substring(SelectionStart + SelectionLength);

            // After an opening brace go one level deeper; if its closing brace is right there, give it its own line.
            if (before.EndsWith('{'))
            {
                var inner = indent + new string(' ', _tabSize);
                if (after.StartsWith('}'))
                {
                    var insert = "\n" + inner + "\n" + indent;
                    Edit(SelectionStart, SelectionLength, insert, SelectionStart + 1 + inner.Length);
                    return;
                }
                indent = inner;
            }

            Edit(SelectionStart, SelectionLength, "\n" + indent, SelectionStart + 1 + indent.Length);
        }

        private void InsertTab()
        {
            var line = LineOf(SelectionStart);
            var column = SelectionStart - _lineStarts[line];
            var spaces = _tabSize - column % _tabSize;
            InsertText(new string(' ', spaces));
        }

        /// <summary>Indents or dedents every line the selection touches.</summary>
        private void ShiftLines(bool dedent)
        {
            var first = LineOf(SelectionStart);
            var last = LineOf(SelectionStart + SelectionLength);
            if (SelectionLength > 0 && SelectionStart + SelectionLength == _lineStarts[last] && last > first)
            {
                last--;
            }

            var builder = new StringBuilder();
            var anchor = _anchor;
            var caret = _caret;
            var rangeStart = _lineStarts[first];
            var rangeEnd = LineEnd(last);
            var delta = 0;
            for (var line = first; line <= last; line++)
            {
                var text = LineText(line);
                var lineStart = _lineStarts[line];
                if (dedent)
                {
                    var remove = 0;
                    while (remove < _tabSize && remove < text.Length && text[remove] == ' ')
                    {
                        remove++;
                    }
                    text = text.Substring(remove);
                    Shift(ref anchor, lineStart + delta, -remove);
                    Shift(ref caret, lineStart + delta, -remove);
                    delta -= remove;
                }
                else if (text.Length > 0)
                {
                    text = new string(' ', _tabSize) + text;
                    Shift(ref anchor, lineStart + delta, _tabSize);
                    Shift(ref caret, lineStart + delta, _tabSize);
                    delta += _tabSize;
                }

                if (line > first)
                {
                    builder.Append('\n');
                }
                builder.Append(text);
            }

            Edit(rangeStart, rangeEnd - rangeStart, builder.ToString(), caret);
            _anchor = Math.Clamp(anchor, 0, _text.Length);
        }

        // Moves an offset that sits at or after a line's start by the change made to that line.
        private static void Shift(ref int offset, int lineStart, int by)
        {
            if (offset >= lineStart)
            {
                offset = Math.Max(lineStart, offset + by);
            }
        }

        // ----- movement --------------------------------------------------------------------------------

        private void MoveCaret(int index, bool extend, bool keepDesiredX = false)
        {
            _caret = Math.Clamp(index, 0, _text.Length);
            if (!extend)
            {
                _anchor = _caret;
            }
            if (!keepDesiredX)
            {
                _desiredX = -1.0f;
            }
            _revealCaret = true;
            InvalidateVisual();
        }

        private int WordLeft(int index)
        {
            while (index > 0 && !IsWordChar(_text[index - 1]) && _text[index - 1] != '\n')
            {
                index--;
            }
            while (index > 0 && IsWordChar(_text[index - 1]))
            {
                index--;
            }
            if (index > 0 && index == _caret && _text[index - 1] == '\n')
            {
                index--;
            }
            return index;
        }

        private int WordRight(int index)
        {
            while (index < _text.Length && !IsWordChar(_text[index]) && _text[index] != '\n')
            {
                index++;
            }
            while (index < _text.Length && IsWordChar(_text[index]))
            {
                index++;
            }
            if (index < _text.Length && index == _caret && _text[index] == '\n')
            {
                index++;
            }
            return index;
        }

        private void MoveVertically(int lines, bool extend)
        {
            var font = ResolveFont();
            var line = LineOf(_caret);
            var target = line + lines;

            if (target < 0)
            {
                MoveCaret(0, extend);
                return;
            }
            if (target >= _lineStarts.Count)
            {
                MoveCaret(_text.Length, extend);
                return;
            }

            if (font == null)
            {
                return;
            }

            // Remembered, so passing over a short line and on to a long one returns to the original column.
            if (_desiredX < 0.0f)
            {
                _desiredX = XOf(font, line, _caret - _lineStarts[line]);
            }
            MoveCaret(_lineStarts[target] + ColumnAt(font, target, _desiredX), extend, keepDesiredX: true);
        }

        private void SmartHome(bool extend)
        {
            var line = LineOf(_caret);
            var start = _lineStarts[line];
            var firstText = start + LeadingWhitespace(line).Length;

            // First press goes to the first character, the next to the true start.
            if (_caret == firstText || firstText >= LineEnd(line))
            {
                MoveCaret(start, extend);
            }
            else
            {
                MoveCaret(firstText, extend);
            }
        }

        // ----- metrics ---------------------------------------------------------------------------------

        private ManaFont? ResolveFont()
        {
            var root = Root;
            if (root == null)
            {
                return null;
            }

            try
            {
                return root.Fonts.Get(root.Theme.FontFamilyMono, root.Theme.FontSize, root.Scale);
            }
            catch (Exception)
            {
                // A machine without the mono face still gets an editor, just not an aligned one.
                return root.Fonts.Get(root.Theme.FontFamily, root.Theme.FontSize, root.Scale);
            }
        }

        private float XOf(ManaFont font, int line, int column)
        {
            var text = LineText(line);
            return font.MeasureWidth(text.AsSpan(0, Math.Clamp(column, 0, text.Length)));
        }

        // The column whose boundary is nearest x; past a glyph's midpoint the caret belongs after it.
        private int ColumnAt(ManaFont font, int line, float x)
        {
            var text = LineText(line);
            var width = 0.0f;
            for (var i = 0; i < text.Length; i++)
            {
                var advance = font.GetGlyph(text[i]).Advance;
                if (x < width + advance * 0.5f)
                {
                    return i;
                }
                width += advance;
            }
            return text.Length;
        }

        private float GutterWidth(ManaFont font)
        {
            if (!_showLineNumbers)
            {
                return 0.0f;
            }

            var digits = Math.Max(2, _lineStarts.Count.ToString().Length);
            return digits * font.GetGlyph('0').Advance + 20.0f;
        }

        private UiRect TextRect(ManaFont font)
        {
            var inner = Bounds.Deflate(ContentInset);
            var gutter = GutterWidth(font);
            return new UiRect(Bounds.X + gutter + 4.0f, inner.Y, MathF.Max(0.0f, Bounds.Right - Bounds.X - gutter - 4.0f - ContentInset.Right), inner.Height);
        }

        // ----- layout and rendering --------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = 320.0f;
            var height = 160.0f;
            if (!float.IsInfinity(availableSize.X))
            {
                width = availableSize.X;
            }
            if (!float.IsInfinity(availableSize.Y))
            {
                height = availableSize.Y;
            }
            return new Vector2(width, height);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var font = ResolveFont();
            var enabled = IsEffectivelyEnabled;

            drawList.DrawBox(
                Bounds,
                ApplyStateOverlay(enabled ? theme.SurfaceAlt : theme.Surface),
                IsFocused ? theme.Accent : theme.Border,
                theme.BorderThickness,
                theme.CornerRadius);

            if (font == null)
            {
                return;
            }

            var lineHeight = font.LineHeight;
            var area = TextRect(font);
            var gutter = GutterWidth(font);
            ClampScroll(font, area, lineHeight);

            var firstLine = Math.Max(0, (int)MathF.Floor(_scrollY / lineHeight));
            var lastLine = Math.Min(_lineStarts.Count - 1, (int)MathF.Floor((_scrollY + area.Height) / lineHeight));
            var caretLine = LineOf(_caret);

            if (_showLineNumbers)
            {
                drawList.FillRect(new UiRect(Bounds.X + 1.0f, Bounds.Y + 1.0f, gutter, Bounds.Height - 2.0f), theme.Surface);
            }

            for (var line = firstLine; line <= lastLine; line++)
            {
                var y = area.Y + line * lineHeight - _scrollY;
                var isError = line + 1 == _errorLine;
                var rowRect = new UiRect(Bounds.X + 1.0f, y, Bounds.Width - 2.0f, lineHeight);

                if (isError)
                {
                    drawList.FillRect(rowRect, theme.Danger.WithAlpha(0.20f));
                }
                else if (IsFocused && !HasSelection && line == caretLine)
                {
                    drawList.FillRect(rowRect, theme.Accent.WithAlpha(0.08f));
                }

                if (_showLineNumbers)
                {
                    DrawLineNumber(drawList, font, line, y, gutter, isError);
                }
            }

            drawList.PushClip(area);
            var originX = area.X - _scrollX;

            if (HasSelection && IsFocused)
            {
                DrawSelection(drawList, font, firstLine, lastLine, area, lineHeight, originX);
            }

            for (var line = firstLine; line <= lastLine; line++)
            {
                DrawLineText(drawList, font, line, originX, area.Y + line * lineHeight - _scrollY, lineHeight, enabled);
            }

            if (IsFocused && !HasSelection)
            {
                var caretX = originX + XOf(font, caretLine, _caret - _lineStarts[caretLine]);
                var caretY = area.Y + caretLine * lineHeight - _scrollY;
                drawList.FillRect(new UiRect(font.SnapToPixel(caretX), caretY, 1.0f, lineHeight), theme.Text);
            }

            drawList.PopClip();
        }

        private void DrawLineNumber(ManaUiDrawList drawList, ManaFont font, int line, float y, float gutter, bool isError)
        {
            var label = (line + 1).ToString();
            var color = Theme.TextDisabled;
            if (isError)
            {
                color = Theme.Danger;
            }
            else if (line == LineOf(_caret) && IsFocused)
            {
                color = Theme.TextMuted;
            }

            _layout.Font = font;
            _layout.Text = label;
            var width = font.MeasureWidth(label);
            _layout.Render(drawList, new UiRect(Bounds.X + gutter - 10.0f - width, y, width + 2.0f, font.LineHeight), color);
        }

        private void DrawSelection(ManaUiDrawList drawList, ManaFont font, int firstLine, int lastLine, UiRect area, float lineHeight, float originX)
        {
            var start = SelectionStart;
            var end = SelectionStart + SelectionLength;
            var newlineWidth = font.GetGlyph(' ').Advance;

            for (var line = firstLine; line <= lastLine; line++)
            {
                var lineStart = _lineStarts[line];
                var lineEnd = LineEnd(line);
                if (end <= lineStart || start > lineEnd)
                {
                    continue;
                }

                var from = Math.Max(start, lineStart);
                var to = Math.Min(end, lineEnd);
                var x0 = XOf(font, line, from - lineStart);
                var x1 = XOf(font, line, to - lineStart);

                // A selection running on past the line end shows the newline as a space's width.
                if (end > lineEnd)
                {
                    x1 += newlineWidth;
                }
                if (x1 <= x0)
                {
                    continue;
                }

                drawList.FillRect(
                    new UiRect(originX + x0, area.Y + line * lineHeight - _scrollY, x1 - x0, lineHeight),
                    Theme.Selected);
            }
        }

        private void DrawLineText(ManaUiDrawList drawList, ManaFont font, int line, float originX, float y, float lineHeight, bool enabled)
        {
            var text = LineText(line);
            if (text.Length == 0)
            {
                return;
            }

            var baseColor = Theme.Text;
            if (!enabled)
            {
                baseColor = Theme.TextDisabled;
            }

            _layout.Font = font;
            var spans = LineColorizer?.Invoke(text);
            if (spans == null || spans.Count == 0)
            {
                DrawRun(drawList, font, text, 0, text.Length, originX, y, lineHeight, baseColor);
                return;
            }

            var position = 0;
            foreach (var span in spans.OrderBy(s => s.Start))
            {
                var start = Math.Clamp(span.Start, position, text.Length);
                var end = Math.Clamp(span.Start + span.Length, start, text.Length);
                if (start > position)
                {
                    DrawRun(drawList, font, text, position, start, originX, y, lineHeight, baseColor);
                }
                if (end > start)
                {
                    DrawRun(drawList, font, text, start, end, originX, y, lineHeight, span.Color);
                }
                position = Math.Max(position, end);
            }

            if (position < text.Length)
            {
                DrawRun(drawList, font, text, position, text.Length, originX, y, lineHeight, baseColor);
            }
        }

        private void DrawRun(ManaUiDrawList drawList, ManaFont font, string line, int start, int end, float originX, float y, float lineHeight, Vector4 color)
        {
            var x = originX + font.MeasureWidth(line.AsSpan(0, start));
            _layout.Font = font;
            _layout.Text = line.Substring(start, end - start);
            _layout.Render(drawList, new UiRect(x, y, float.MaxValue / 4.0f, lineHeight), color);
        }

        private float LongestLineWidth(ManaFont font)
        {
            var longest = 0;
            var longestLine = 0;
            for (var line = 0; line < _lineStarts.Count; line++)
            {
                var length = LineEnd(line) - _lineStarts[line];
                if (length > longest)
                {
                    longest = length;
                    longestLine = line;
                }
            }
            return XOf(font, longestLine, longest);
        }

        private void ClampScroll(ManaFont font, UiRect area, float lineHeight)
        {
            if (_revealCaret)
            {
                _revealCaret = false;
                var line = LineOf(_caret);
                var top = line * lineHeight;
                if (top < _scrollY)
                {
                    _scrollY = top;
                }
                else if (top + lineHeight > _scrollY + area.Height)
                {
                    _scrollY = top + lineHeight - area.Height;
                }

                var caretX = XOf(font, line, _caret - _lineStarts[line]);
                var margin = font.GetGlyph('0').Advance * 2.0f;
                if (caretX - _scrollX > area.Width - margin)
                {
                    _scrollX = caretX - area.Width + margin;
                }
                else if (caretX - _scrollX < 0.0f)
                {
                    _scrollX = MathF.Max(0.0f, caretX - margin);
                }
            }

            // A little room past the last line, as editors leave, so the end is not glued to the edge.
            var maxY = MathF.Max(0.0f, _lineStarts.Count * lineHeight - area.Height * 0.5f);
            var maxX = MathF.Max(0.0f, LongestLineWidth(font) - area.Width + 24.0f);
            _scrollY = Math.Clamp(_scrollY, 0.0f, maxY);
            _scrollX = Math.Clamp(_scrollX, 0.0f, maxX);
        }

        // ----- pointer ---------------------------------------------------------------------------------

        private int IndexAt(Vector2 position)
        {
            var font = ResolveFont();
            if (font == null)
            {
                return 0;
            }

            var area = TextRect(font);
            var line = Math.Clamp((int)MathF.Floor((position.Y - area.Y + _scrollY) / font.LineHeight), 0, _lineStarts.Count - 1);
            return _lineStarts[line] + ColumnAt(font, line, position.X - area.X + _scrollX);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            Focus();
            var index = IndexAt(e.Position);

            if (e.ClickCount >= 3)
            {
                var line = LineOf(index);
                _anchor = _lineStarts[line];
                _caret = Math.Min(LineEnd(line) + 1, _text.Length);
            }
            else if (e.ClickCount == 2)
            {
                var start = index;
                var end = index;
                while (start > 0 && IsWordChar(_text[start - 1]))
                {
                    start--;
                }
                while (end < _text.Length && IsWordChar(_text[end]))
                {
                    end++;
                }
                _anchor = start;
                _caret = end;
            }
            else
            {
                _caret = index;
                if (!e.Modifiers.HasFlag(UiModifiers.Shift))
                {
                    _anchor = _caret;
                }
                _selecting = true;
                CapturePointer();
            }

            _desiredX = -1.0f;
            _revealCaret = true;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_selecting)
            {
                return;
            }

            _caret = IndexAt(e.Position);
            _desiredX = -1.0f;
            _revealCaret = true;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_selecting)
            {
                _selecting = false;
                ReleasePointerCapture();
                e.Handled = true;
            }
        }

        protected override void OnPointerWheel(UiPointerWheelEventArgs e)
        {
            var font = ResolveFont();
            if (font == null)
            {
                return;
            }

            // Three lines a notch; Shift turns the wheel sideways.
            if (e.Modifiers.HasFlag(UiModifiers.Shift))
            {
                _scrollX -= e.Delta.Y * font.LineHeight * 3.0f;
            }
            else
            {
                _scrollY -= e.Delta.Y * font.LineHeight * 3.0f;
                _scrollX -= e.Delta.X * font.LineHeight * 3.0f;
            }
            InvalidateVisual();
            e.Handled = true;
        }

        // ----- keyboard --------------------------------------------------------------------------------

        protected override void OnTextInput(UiTextInputEventArgs e)
        {
            if (IsReadOnly || !IsEffectivelyEnabled)
            {
                return;
            }

            foreach (var character in e.Text)
            {
                if (character < ' ' && character != '\t')
                {
                    continue;
                }

                if (character == '}' && !HasSelection && TryDedentBeforeBrace())
                {
                    continue;
                }

                // A selection is replaced as its own step; the characters after it join one typing run.
                var kind = "type";
                if (HasSelection)
                {
                    kind = null;
                }
                InsertText(character.ToString(), kind);
            }
            e.Handled = true;
        }

        // Typing } on a line holding only indent pulls it back one level, so it lines up with its opener.
        private bool TryDedentBeforeBrace()
        {
            var line = LineOf(_caret);
            var start = _lineStarts[line];
            var before = _text.Substring(start, _caret - start);
            if (before.Length < _tabSize || before.Any(c => c != ' '))
            {
                return false;
            }

            var remove = before.Length % _tabSize;
            if (remove == 0)
            {
                remove = _tabSize;
            }
            Edit(_caret - remove, remove, "}", _caret - remove + 1);
            return true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (!IsEffectivelyEnabled)
            {
                return;
            }

            var shift = e.Modifiers.HasFlag(UiModifiers.Shift);
            var control = e.Modifiers.HasFlag(UiModifiers.Control) || e.Modifiers.HasFlag(UiModifiers.Meta);
            var line = LineOf(_caret);
            var pageLines = 10;
            if (ResolveFont() is { } font)
            {
                pageLines = Math.Max(1, (int)(TextRect(font).Height / font.LineHeight) - 1);
            }

            switch (e.Key)
            {
                case UiKey.Left:
                    if (HasSelection && !shift && !control)
                    {
                        MoveCaret(SelectionStart, false);
                    }
                    else if (control)
                    {
                        MoveCaret(WordLeft(_caret), shift);
                    }
                    else
                    {
                        MoveCaret(_caret - 1, shift);
                    }
                    break;

                case UiKey.Right:
                    if (HasSelection && !shift && !control)
                    {
                        MoveCaret(SelectionStart + SelectionLength, false);
                    }
                    else if (control)
                    {
                        MoveCaret(WordRight(_caret), shift);
                    }
                    else
                    {
                        MoveCaret(_caret + 1, shift);
                    }
                    break;

                case UiKey.Up:
                    MoveVertically(-1, shift);
                    break;
                case UiKey.Down:
                    MoveVertically(1, shift);
                    break;
                case UiKey.PageUp:
                    MoveVertically(-pageLines, shift);
                    break;
                case UiKey.PageDown:
                    MoveVertically(pageLines, shift);
                    break;

                case UiKey.Home when control:
                    MoveCaret(0, shift);
                    break;
                case UiKey.End when control:
                    MoveCaret(_text.Length, shift);
                    break;
                case UiKey.Home:
                    SmartHome(shift);
                    break;
                case UiKey.End:
                    MoveCaret(LineEnd(line), shift);
                    break;

                case UiKey.A when control:
                    SelectAll();
                    break;
                case UiKey.C when control:
                    if (HasSelection)
                    {
                        Clipboard.Text = SelectedText;
                    }
                    break;
                case UiKey.X when control && !IsReadOnly:
                    if (HasSelection)
                    {
                        Clipboard.Text = SelectedText;
                        DeleteSelection();
                    }
                    break;
                case UiKey.V when control && !IsReadOnly:
                    var pasted = Clipboard.Text;
                    if (!string.IsNullOrEmpty(pasted))
                    {
                        InsertText(pasted);
                    }
                    break;

                case UiKey.Z when control && shift:
                    RestoreFrom(_redo, _undo);
                    break;
                case UiKey.Z when control:
                    RestoreFrom(_undo, _redo);
                    break;
                case UiKey.Y when control:
                    RestoreFrom(_redo, _undo);
                    break;

                case UiKey.Enter when !IsReadOnly:
                    InsertNewline();
                    break;

                case UiKey.Tab when !IsReadOnly:
                    if (shift)
                    {
                        ShiftLines(dedent: true);
                    }
                    else if (HasSelection && LineOf(SelectionStart) != LineOf(SelectionStart + SelectionLength))
                    {
                        ShiftLines(dedent: false);
                    }
                    else
                    {
                        InsertTab();
                    }
                    break;

                case UiKey.Backspace when !IsReadOnly:
                    Backspace(control);
                    break;
                case UiKey.Delete when !IsReadOnly:
                    if (HasSelection)
                    {
                        DeleteSelection();
                    }
                    else if (_caret < _text.Length)
                    {
                        var end = _caret + 1;
                        if (control)
                        {
                            end = WordRight(_caret);
                        }
                        Edit(_caret, end - _caret, string.Empty, _caret, "delete");
                    }
                    break;

                case UiKey.Escape:
                    Root?.FocusManager.Focus(null);
                    break;

                default:
                    return;
            }

            InvalidateVisual();
            e.Handled = true;
        }

        private void Backspace(bool control)
        {
            if (HasSelection)
            {
                DeleteSelection();
                return;
            }
            if (_caret == 0)
            {
                return;
            }

            var line = LineOf(_caret);
            var start = _lineStarts[line];
            var before = _text.Substring(start, _caret - start);

            // Only indent before the caret: take away a whole level at once, back to the previous tab stop.
            if (!control && before.Length > 0 && before.All(c => c == ' '))
            {
                var remove = before.Length % _tabSize;
                if (remove == 0)
                {
                    remove = _tabSize;
                }
                Edit(_caret - remove, remove, string.Empty, _caret - remove, "delete");
                return;
            }

            var from = _caret - 1;
            if (control)
            {
                from = WordLeft(_caret);
            }
            Edit(from, _caret - from, string.Empty, from, "delete");
        }

        protected override void OnLostFocus()
        {
            _selecting = false;
            _lastEditKind = null;
            InvalidateVisual();
        }

        protected override void OnGotFocus()
        {
            InvalidateVisual();
        }

        /// <summary>The clipboard copy, cut and paste go through; shared with <see cref="TextBox"/> so a host sets it once.</summary>
        public static IUiClipboard Clipboard
        {
            get => TextBox.Clipboard;
            set => TextBox.Clipboard = value;
        }
    }
}
