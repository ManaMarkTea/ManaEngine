namespace ManaUI
{
    [Flags]
    public enum UiDebugFlags : uint
    {
        None = 0,

        /// <summary>Outline every laid-out element, tinted by depth.</summary>
        ElementBounds = 1 << 0,

        /// <summary>Outline the clip rect of everything that clips, so a wrong clip is visible as a shape.</summary>
        ClipRects = 1 << 1,

        /// <summary>Highlight the element under the pointer and everything above it in the tree.</summary>
        HoverPath = 1 << 2,

        /// <summary>A corner readout: instances, batches, elements, and what a frame costs.</summary>
        Stats = 1 << 3,

        /// <summary>Tint each draw batch differently, which is how a lost single-draw-call frame shows up.</summary>
        Batches = 1 << 4,

        All = ElementBounds | ClipRects | HoverPath | Stats | Batches
    }

    /// <summary>
    /// A wireframe view of the UI, drawn on top of it -- the layout equivalent of a renderer's wireframe
    /// mode. What it mostly answers is "why is this element that size", which is a question the finished
    /// pixels are very good at hiding.
    /// <para>
    /// The overlay draws with the same instanced primitive as everything else, so its own boxes land in the
    /// same draw list. The stats it reports are captured before any of that is added, or the overlay would
    /// spend its time measuring itself.
    /// </para>
    /// </summary>
    public sealed class UiDebugOverlay
    {
        /// <summary>Outline colours by depth. Cycled, so nesting is legible without a legend.</summary>
        private static readonly Vector4[] DepthColors =
        {
            new(0.30f, 0.80f, 1.00f, 0.55f),
            new(0.45f, 1.00f, 0.55f, 0.55f),
            new(1.00f, 0.80f, 0.30f, 0.55f),
            new(1.00f, 0.45f, 0.70f, 0.55f),
            new(0.70f, 0.55f, 1.00f, 0.55f)
        };

        private static readonly Vector4 ClipColor = new(1.00f, 0.35f, 0.30f, 0.75f);
        private static readonly Vector4 HoverColor = new(1.00f, 1.00f, 1.00f, 0.90f);

        private readonly UiRoot _root;

        internal UiDebugOverlay(UiRoot root)
        {
            _root = root;
        }

        public UiDebugFlags Flags
        {
            get => _flags;
            set
            {
                if (_flags == value)
                {
                    return;
                }

                _flags = value;

                // A static UI re-submits last frame's buffer rather than re-recording, so turning the
                // overlay on has to say the picture changed or nothing would appear until something else
                // moved.
                _root.NotifyVisualInvalidated();
            }
        }

        private UiDebugFlags _flags = UiDebugFlags.None;

        /// <summary>Convenience for the common case of turning the whole thing on and off.</summary>
        public bool IsEnabled
        {
            get => Flags != UiDebugFlags.None;
            set => Flags = value ? UiDebugFlags.ElementBounds | UiDebugFlags.Stats : UiDebugFlags.None;
        }

        /// <summary>Elements walked on the last overlay pass, whether or not they were drawn.</summary>
        public int ElementCount { get; private set; }

        /// <summary>Deepest nesting seen. A number that only ever grows is usually a leak somewhere.</summary>
        public int MaxDepth { get; private set; }

        /// <summary>Milliseconds the host reports for the last layout pass; set from outside.</summary>
        public double LayoutMilliseconds { get; set; }

        /// <summary>Milliseconds the host reports for the last render pass; set from outside.</summary>
        public double RenderMilliseconds { get; set; }

        /// <summary>
        /// Appends the overlay to a finished draw list. Called by the root after content is recorded, with
        /// the content's own totals, so the readout describes the UI rather than the overlay.
        /// </summary>
        internal void Render(ManaUiDrawList drawList, int contentInstances, int contentBatches)
        {
            if (Flags == UiDebugFlags.None)
            {
                return;
            }

            ElementCount = 0;
            MaxDepth = 0;

            if ((Flags & (UiDebugFlags.ElementBounds | UiDebugFlags.ClipRects)) != 0)
            {
                Walk(drawList, _root, 0);
            }

            if ((Flags & UiDebugFlags.HoverPath) != 0)
            {
                RenderHoverPath(drawList);
            }

            if ((Flags & UiDebugFlags.Stats) != 0)
            {
                RenderStats(drawList, contentInstances, contentBatches);
            }
        }

        private void Walk(ManaUiDrawList drawList, UiElement element, int depth)
        {
            ElementCount++;
            MaxDepth = Math.Max(MaxDepth, depth);

            // Zero-sized elements are the interesting ones -- a control that vanished usually did so by
            // measuring to nothing -- but there is no rectangle to draw for them.
            if ((Flags & UiDebugFlags.ElementBounds) != 0 && !element.Bounds.IsEmpty)
            {
                DrawOutline(drawList, element.Bounds, DepthColors[depth % DepthColors.Length], 1.0f);
            }

            if ((Flags & UiDebugFlags.ClipRects) != 0 && element.ClipToBounds && !element.Bounds.IsEmpty)
            {
                DrawOutline(drawList, element.Bounds.Deflate(new Thickness(1.0f)), ClipColor, 1.0f);
            }

            var count = element.VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                var child = element.GetVisualChild(i);
                if (child != null && child.Visibility != Visibility.Collapsed)
                {
                    Walk(drawList, child, depth + 1);
                }
            }
        }

        /// <summary>
        /// The chain from the hovered element up to the root. Answers "what am I actually over", which is
        /// rarely what it looks like once padding and transparent containers are involved.
        /// </summary>
        private void RenderHoverPath(ManaUiDrawList drawList)
        {
            var element = _root.Input.HoveredElement;
            var thickness = 2.0f;

            while (element != null)
            {
                if (!element.Bounds.IsEmpty)
                {
                    DrawOutline(drawList, element.Bounds, HoverColor.WithAlpha(0.9f / thickness), thickness);
                }

                element = element.Parent;
                thickness = MathF.Max(1.0f, thickness - 0.5f);
            }
        }

        private void RenderStats(ManaUiDrawList drawList, int contentInstances, int contentBatches)
        {
            var theme = _root.Theme;
            var font = _root.Fonts.Get(theme.FontFamilyMono, theme.FontSizeSmall, _root.Scale);

            var hovered = _root.Input.HoveredElement;
            var lines = new[]
            {
                $"instances  {contentInstances}",
                $"batches    {contentBatches}",
                $"elements   {ElementCount}",
                $"depth      {MaxDepth}",
                $"layout     {LayoutMilliseconds:F2} ms",
                $"render     {RenderMilliseconds:F2} ms",
                $"hover      {hovered?.GetType().Name ?? "none"}"
            };

            var width = 0.0f;
            foreach (var line in lines)
            {
                width = MathF.Max(width, font.MeasureWidth(line));
            }

            var lineHeight = font.LineHeight * 1.15f;
            var panel = new UiRect(
                8.0f,
                8.0f,
                width + 16.0f,
                lines.Length * lineHeight + 12.0f);

            drawList.FillRect(panel, new Vector4(0.0f, 0.0f, 0.0f, 0.78f), new CornerRadius(4.0f));

            for (var i = 0; i < lines.Length; i++)
            {
                new TextLayout
                {
                    Font = font,
                    Text = lines[i],
                    MaxWidth = width
                }.Render(
                    drawList,
                    new UiRect(panel.X + 8.0f, panel.Y + 6.0f + i * lineHeight, width, lineHeight),
                    UiColor.White,
                    VerticalAlignment.Center);
            }
        }

        /// <summary>
        /// A hollow box: the same primitive as everything else, with a border and no fill.
        /// <para>
        /// Deliberately unclipped. The overlay runs after the tree has finished rendering, by which point
        /// every push has been popped and the clip is back to infinite -- so an outline is drawn whole even
        /// where the element it describes was itself clipped away. That is the point: a row scrolled out of
        /// its viewport is exactly the case worth seeing.
        /// </para>
        /// </summary>
        private static void DrawOutline(ManaUiDrawList drawList, UiRect rect, Vector4 color, float thickness)
        {
            drawList.DrawBox(rect, UiColor.Transparent, color, thickness, CornerRadius.Zero);
        }
    }
}
