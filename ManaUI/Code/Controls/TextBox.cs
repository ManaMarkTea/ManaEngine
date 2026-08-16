using System.Text;

namespace ManaUI
{
    /// <summary>
    /// A single-line editable text field with a caret, selection and clipboard support.
    /// <para>
    /// Reports <see cref="UiElement.WantsAllKeyboardInput"/> so the host suppresses editor shortcuts while
    /// it has focus -- otherwise typing "v" into a field would also switch tools -- and so Tab reaches the
    /// control rather than moving focus away mid-edit.
    /// </para>
    /// </summary>
    public class TextBox : Control
    {
        private readonly TextLayout _layout = new();
        private readonly StringBuilder _builder = new();

        private string _text = string.Empty;
        private string _placeholder = string.Empty;
        private int _caret;
        private int _selectionAnchor;
        private float _scrollOffset;
        private bool _selecting;

        public TextBox()
        {
            Focusable = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;
            ClipToBounds = true;
        }

        public event EventHandler<string>? TextChanged;

        /// <summary>Raised on Enter, and on losing focus after an edit -- the "value is settled" moment.</summary>
        public event EventHandler<string>? TextCommitted;

        public string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;
                if (_text == value)
                {
                    return;
                }

                _text = value;
                _caret = Math.Clamp(_caret, 0, _text.Length);
                _selectionAnchor = _caret;
                InvalidateMeasure();
                TextChanged?.Invoke(this, _text);
            }
        }

        /// <summary>Shown in muted text when the field is empty.</summary>
        public string Placeholder
        {
            get => _placeholder;
            set => SetVisualProperty(ref _placeholder, value ?? string.Empty);
        }

        public int MaxLength { get; set; } = int.MaxValue;

        public bool IsReadOnly { get; set; }

        /// <summary>
        /// Floor for the field's height, defaulting to the theme's control height so fields line up with
        /// the buttons beside them. Lower it for a dense form, where matching every other control matters
        /// less than fitting more rows on screen.
        /// </summary>
        public float MinimumFieldHeight { get; set; } = float.NaN;

        private float ResolvedMinimumFieldHeight =>
            LayoutHelper.IsAuto(MinimumFieldHeight) ? Theme.ControlHeight : MinimumFieldHeight;

        /// <summary>Optional filter; return false to reject a character. Used for numeric-only fields.</summary>
        public Func<char, bool>? CharacterFilter { get; set; }

        public override bool WantsAllKeyboardInput => true;

        public int CaretIndex
        {
            get => _caret;
            set
            {
                _caret = Math.Clamp(value, 0, _text.Length);
                _selectionAnchor = _caret;
                InvalidateVisual();
            }
        }

        public bool HasSelection => _selectionAnchor != _caret;
        private int SelectionStart => Math.Min(_caret, _selectionAnchor);
        private int SelectionLength => Math.Abs(_caret - _selectionAnchor);

        public string SelectedText => HasSelection ? _text.Substring(SelectionStart, SelectionLength) : string.Empty;

        public void SelectAll()
        {
            _selectionAnchor = 0;
            _caret = _text.Length;
            InvalidateVisual();
        }

        protected override Thickness ContentInset => Padding.IsZero ? Theme.ControlPadding : Padding;

        // ----- layout ---------------------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var font = ResolveFont();
            if (font == null)
            {
                return new Vector2(0.0f, Theme.ControlHeight);
            }

            var inset = ContentInset;
            var width = float.IsInfinity(availableSize.X) ? 160.0f : availableSize.X;

            return new Vector2(
                width,
                MathF.Max(ResolvedMinimumFieldHeight, font.LineHeight + inset.Vertical));
        }

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var font = ResolveFont();
            var enabled = IsEffectivelyEnabled;
            var corners = theme.CornerRadius;

            // Hover overlay, same as every other control -- an input used to be the one thing on screen
            // that never reacted to the pointer at all, resting or over it looked identical.
            var fill = ApplyStateOverlay(enabled ? theme.SurfaceAlt : theme.Surface);

            drawList.DrawBox(
                Bounds,
                fill,
                IsFocused ? theme.Accent : theme.Border,
                theme.BorderThickness,
                corners);

            if (font == null)
            {
                return;
            }

            var content = Bounds.Deflate(ContentInset);
            EnsureCaretVisible(font, content.Width);

            // Everything inside the field is clipped to the content box, so a long value scrolls under the
            // padding instead of spilling over the border.
            drawList.PushClip(content);

            var origin = new Vector2(content.X - _scrollOffset, content.Y);
            var baselineTop = content.Y + (content.Height - font.LineHeight) * 0.5f;
            origin.Y = baselineTop;

            if (_text.Length == 0 && _placeholder.Length > 0)
            {
                _layout.Font = font;
                _layout.Text = _placeholder;
                _layout.Render(drawList, new UiRect(origin, content.Size), theme.TextDisabled);
                drawList.PopClip();
                return;
            }

            if (HasSelection && IsFocused)
            {
                var start = MeasureTo(font, SelectionStart);
                var end = MeasureTo(font, SelectionStart + SelectionLength);

                drawList.FillRect(
                    new UiRect(origin.X + start, content.Y + 2.0f, end - start, content.Height - 4.0f),
                    theme.Selected);
            }

            _layout.Font = font;
            _layout.Text = _text;
            _layout.Render(drawList, new UiRect(origin, new Vector2(float.MaxValue / 4.0f, content.Height)),
                enabled ? theme.Text : theme.TextDisabled);

            if (IsFocused && !HasSelection)
            {
                var caretX = origin.X + MeasureTo(font, _caret);
                drawList.FillRect(
                    new UiRect(font.SnapToPixel(caretX), content.Y + 2.0f, 1.0f, content.Height - 4.0f),
                    theme.Text);
            }

            drawList.PopClip();
        }

        /// <summary>Scrolls the visible window so the caret stays inside it while typing.</summary>
        private void EnsureCaretVisible(ManaFont font, float viewportWidth)
        {
            var caretX = MeasureTo(font, _caret);
            var textWidth = font.MeasureWidth(_text);

            if (caretX - _scrollOffset > viewportWidth)
            {
                _scrollOffset = caretX - viewportWidth;
            }
            else if (caretX - _scrollOffset < 0.0f)
            {
                _scrollOffset = caretX;
            }

            // Never leave blank space on the right when the text would fill it.
            _scrollOffset = Math.Clamp(_scrollOffset, 0.0f, MathF.Max(0.0f, textWidth - viewportWidth));
        }

        private float MeasureTo(ManaFont font, int index)
        {
            return font.MeasureWidth(_text.AsSpan(0, Math.Clamp(index, 0, _text.Length)));
        }

        private ManaFont? ResolveFont()
        {
            var root = Root;
            return root?.Fonts.Get(root.Theme.FontFamily, root.Theme.FontSize, root.Scale);
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            Focus();

            if (e.ClickCount >= 2)
            {
                SelectAll();
                e.Handled = true;
                return;
            }

            _caret = IndexAt(e.Position.X);
            _selectionAnchor = _caret;
            _selecting = true;

            CapturePointer();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_selecting)
            {
                return;
            }

            // Anchor stays put and the caret follows, which is what makes drag-select extend rather than
            // restart the selection.
            _caret = IndexAt(e.Position.X);
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

        private int IndexAt(float x)
        {
            var font = ResolveFont();
            if (font == null)
            {
                return 0;
            }

            var content = Bounds.Deflate(ContentInset);
            var local = x - content.X + _scrollOffset;

            var width = 0.0f;
            for (var i = 0; i < _text.Length; i++)
            {
                var advance = font.GetGlyph(_text[i]).Advance;

                // Past a glyph's midpoint the caret belongs after it, so clicking near a boundary lands
                // where the user is aiming.
                if (local < width + advance * 0.5f)
                {
                    return i;
                }

                width += advance;
            }

            return _text.Length;
        }

        protected override void OnTextInput(UiTextInputEventArgs e)
        {
            if (IsReadOnly || !IsEffectivelyEnabled)
            {
                return;
            }

            foreach (var character in e.Text)
            {
                if (CharacterFilter != null && !CharacterFilter(character))
                {
                    continue;
                }

                InsertText(character.ToString());
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (!IsEffectivelyEnabled)
            {
                return;
            }

            var shift = e.Modifiers.HasFlag(UiModifiers.Shift);
            var control = e.Modifiers.HasFlag(UiModifiers.Control) || e.Modifiers.HasFlag(UiModifiers.Meta);

            switch (e.Key)
            {
                case UiKey.Left:
                    MoveCaret(_caret - 1, shift);
                    break;
                case UiKey.Right:
                    MoveCaret(_caret + 1, shift);
                    break;
                case UiKey.Home:
                    MoveCaret(0, shift);
                    break;
                case UiKey.End:
                    MoveCaret(_text.Length, shift);
                    break;
                case UiKey.Backspace when !IsReadOnly:
                    if (HasSelection)
                    {
                        DeleteSelection();
                    }
                    else if (_caret > 0)
                    {
                        Replace(_caret - 1, 1, string.Empty);
                        _caret--;
                        _selectionAnchor = _caret;
                    }

                    break;
                case UiKey.Delete when !IsReadOnly:
                    if (HasSelection)
                    {
                        DeleteSelection();
                    }
                    else if (_caret < _text.Length)
                    {
                        Replace(_caret, 1, string.Empty);
                    }

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
                case UiKey.Enter:
                    TextCommitted?.Invoke(this, _text);
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

        /// <summary>
        /// The clipboard copy, cut and paste go through. In memory unless a host replaces it with one
        /// backed by the platform's, which is what keeps the text controls testable headlessly.
        /// </summary>
        public static IUiClipboard Clipboard { get; set; } = new UiMemoryClipboard();

        protected override void OnLostFocus()
        {
            _selecting = false;
            TextCommitted?.Invoke(this, _text);
        }

        private void MoveCaret(int index, bool extendSelection)
        {
            _caret = Math.Clamp(index, 0, _text.Length);
            if (!extendSelection)
            {
                _selectionAnchor = _caret;
            }
        }

        private void InsertText(string insert)
        {
            if (HasSelection)
            {
                DeleteSelection();
            }

            var room = MaxLength - _text.Length;
            if (room <= 0)
            {
                return;
            }

            if (insert.Length > room)
            {
                insert = insert[..room];
            }

            Replace(_caret, 0, insert);
            _caret += insert.Length;
            _selectionAnchor = _caret;
        }

        private void DeleteSelection()
        {
            var start = SelectionStart;
            var length = SelectionLength;
            Replace(start, length, string.Empty);
            _caret = start;
            _selectionAnchor = start;
        }

        private void Replace(int start, int length, string replacement)
        {
            _builder.Clear();
            _builder.Append(_text);
            _builder.Remove(start, length);
            _builder.Insert(start, replacement);

            _text = _builder.ToString();
            InvalidateMeasure();
            TextChanged?.Invoke(this, _text);
        }
    }
}
