namespace ManaUI
{
    /// <summary>
    /// Text with styled runs -- some words bold, some coloured -- that wraps like a <see cref="TextBlock"/>.
    /// <para>
    /// Written as light markup: <c>**bold**</c>, <c>[#7cfc8a]coloured[/]</c>, or a named colour <c>[good]...[/]</c>
    /// from <see cref="NamedColors"/>; they nest (<c>[gold]**+2 prestige**[/]</c>). Each word is its own text block
    /// in a wrapping row, so a run can break across lines the way plain text does; a TextBlock has one font and one
    /// colour, which is too little for a description that should read at a glance.
    /// </para>
    /// </summary>
    public class RichTextBlock : WrapPanel
    {
        private string _markup = string.Empty;
        private float _fontSize = float.NaN;
        private Vector4? _foreground;
        private string? _boldFamily;

        /// <summary>Colours a run can name instead of giving hex: <c>[good]</c>, <c>[bad]</c>, and any an app adds.</summary>
        public static Dictionary<string, Vector4> NamedColors { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["good"] = new Vector4(0.45f, 0.95f, 0.5f, 1.0f),
            ["bad"] = new Vector4(1.0f, 0.42f, 0.36f, 1.0f),
            ["gold"] = new Vector4(1.0f, 0.86f, 0.42f, 1.0f),
            ["magic"] = new Vector4(0.53f, 0.66f, 1.0f, 1.0f),
            ["muted"] = new Vector4(0.72f, 0.76f, 0.86f, 0.9f)
        };

        public RichTextBlock()
        {
            Orientation = Orientation.Horizontal;
            VerticalSpacing = 1.0f;
        }

        public RichTextBlock(string markup)
            : this()
        {
            Markup = markup;
        }

        public string Markup
        {
            get => _markup;
            set
            {
                value ??= string.Empty;
                if (_markup == value)
                {
                    return;
                }

                _markup = value;
                Rebuild();
            }
        }

        /// <summary>Size of every run; NaN for the theme's.</summary>
        public float FontSize
        {
            get => _fontSize;
            set
            {
                _fontSize = value;
                Rebuild();
            }
        }

        /// <summary>Colour of runs that set none; null for the theme's text colour.</summary>
        public Vector4? Foreground
        {
            get => _foreground;
            set
            {
                _foreground = value;
                Rebuild();
            }
        }

        /// <summary>Family for <c>**bold**</c> runs; null for the theme's medium weight.</summary>
        public string? BoldFamily
        {
            get => _boldFamily;
            set
            {
                _boldFamily = value;
                Rebuild();
            }
        }

        /// <summary>The words as styled runs: text, bold, colour (null for the default). Exposed for tests.</summary>
        public static List<(string Text, bool Bold, Vector4? Color)> Parse(string markup)
        {
            return ParseRuns(markup).Select(run => (run.Text, run.Bold, run.Color)).ToList();
        }

        /// <summary>
        /// The same runs, each also saying whether it directly follows the one before with no space between: a word cut into
        /// differently coloured pieces, as in <c>[a]fit[/][b](x)[/]</c>, which must be drawn without a gap and kept together.
        /// </summary>
        private static List<(string Text, bool Bold, Vector4? Color, bool Glued)> ParseRuns(string markup)
        {
            var runs = new List<(string, bool, Vector4?, bool)>();
            var colors = new Stack<Vector4?>();
            var bold = false;
            var word = new System.Text.StringBuilder();
            var separated = true;

            void Flush()
            {
                if (word.Length > 0)
                {
                    runs.Add((word.ToString(), bold, colors.Count > 0 ? colors.Peek() : null, !separated));
                    word.Clear();
                    separated = false;
                }
            }

            for (var i = 0; i < markup.Length; i++)
            {
                var c = markup[i];
                if (c == '*' && i + 1 < markup.Length && markup[i + 1] == '*')
                {
                    Flush();
                    bold = !bold;
                    i++;
                }
                else if (c == '[' && markup.IndexOf(']', i) is var close and > 0)
                {
                    var tag = markup.Substring(i + 1, close - i - 1);
                    if (tag == "/")
                    {
                        Flush();
                        if (colors.Count > 0)
                        {
                            colors.Pop();
                        }
                    }
                    else if (TryColor(tag, out var color))
                    {
                        Flush();
                        colors.Push(color);
                    }
                    else
                    {
                        // Not a tag after all: keep it as text.
                        word.Append(markup, i, close - i + 1);
                    }

                    i = close;
                }
                else if (c == ' ')
                {
                    Flush();
                    separated = true;
                }
                else if (c == '\n')
                {
                    // A line break is a run of its own.
                    Flush();
                    runs.Add(("\n", false, null, false));
                    separated = true;
                }
                else
                {
                    word.Append(c);
                }
            }

            Flush();
            return runs;
        }

        private static bool TryColor(string tag, out Vector4 color)
        {
            if (NamedColors.TryGetValue(tag, out color))
            {
                return true;
            }

            if (tag.Length is 7 or 9 && tag[0] == '#'
                && uint.TryParse(tag.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var hex))
            {
                var hasAlpha = tag.Length == 9;
                var rgba = hasAlpha ? hex : (hex << 8) | 0xFF;
                color = new Vector4((rgba >> 24 & 0xFF) / 255.0f, (rgba >> 16 & 0xFF) / 255.0f, (rgba >> 8 & 0xFF) / 255.0f, (rgba & 0xFF) / 255.0f);
                return true;
            }

            color = default;
            return false;
        }

        private readonly List<(TextBlock Block, bool Bold)> _runs = new();

        private void Rebuild()
        {
            Children.Clear();
            _runs.Clear();
            // Runs that touch (no space between) are one word: they sit in a row with no gap and wrap together. Only the
            // gaps between words are spaces.
            StackPanel? word = null;
            TextBlock? lone = null;
            foreach (var (text, bold, color, glued) in ParseRuns(_markup))
            {
                if (text == "\n")
                {
                    Children.Add(new WrapBreak());
                    word = null;
                    lone = null;
                    continue;
                }

                var block = new TextBlock { Text = text, Foreground = color ?? _foreground };
                if (!float.IsNaN(_fontSize))
                {
                    block.FontSize = _fontSize;
                }
                _runs.Add((block, bold));

                if (glued && lone != null)
                {
                    // The word turns out to be more than one run: swap its lone block for a row holding both.
                    var index = Children.IndexOf(lone);
                    Children.Remove(lone);
                    word = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0.0f };
                    word.Children.Add(lone);
                    word.Children.Add(block);
                    Children.Insert(index, word);
                    lone = null;
                }
                else if (glued && word != null)
                {
                    word.Children.Add(block);
                }
                else
                {
                    Children.Add(block);
                    lone = block;
                    word = null;
                }
            }

            InvalidateMeasure();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // Bold needs the theme, which a run only has once it is in a tree; and the gap between words is a space.
            var theme = Root?.Theme;
            foreach (var (block, bold) in _runs)
            {
                block.FontFamily = bold ? _boldFamily ?? theme?.FontFamilyMedium : null;
            }

            var size = float.IsNaN(_fontSize) ? theme?.FontSize ?? 14.0f : _fontSize;
            HorizontalSpacing = MathF.Round(size * 0.28f);
            // A pixel of slack: a block arranged at exactly its measured width can otherwise wrap one word more than it
            // measured for (float rounding), drawing a line over whatever comes next.
            var measured = base.MeasureOverride(availableSize);
            return new Vector2(MathF.Ceiling(measured.X) + 1.0f, measured.Y);
        }
    }
}
