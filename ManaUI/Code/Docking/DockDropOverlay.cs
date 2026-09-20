namespace ManaUI
{
    /// <summary>
    /// Draws the drop preview. Lives in the root's overlay layer so it sits above both the docked content
    /// and the floating window being dragged.
    /// </summary>
    public sealed class DockDropOverlay : UiElement
    {
        private DockDropTarget _target;

        public DockDropOverlay()
        {
            IsHitTestVisible = false;
        }

        public void Show(DockDropTarget target)
        {
            _target = target;
            Visibility = target.IsValid ? Visibility.Visible : Visibility.Collapsed;
            InvalidateVisual();
        }

        public void Hide()
        {
            _target = default;
            Visibility = Visibility.Collapsed;
            InvalidateVisual();
        }

        /// <summary>
        /// Asks for the whole root, because that is the area this paints into.
        /// <para>
        /// This used to report zero and try to claim the root back in <see cref="ArrangeOverride"/>, which
        /// cannot work: Arrange clamps whatever ArrangeOverride returns to the greater of the slot and the
        /// desired size, and both of those were zero. So Bounds stayed 0x0, UiElement.Render skips an
        /// element with empty bounds, and the drop preview was never drawn at all -- which is why dropping
        /// a panel felt like a guess. It was: there was nothing on screen to read.
        /// </para>
        /// </summary>
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Root?.Size ?? Vector2.Zero;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (!_target.IsValid)
            {
                return;
            }

            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            drawList.FillRect(_target.Preview, theme.Accent.WithAlpha(0.25f), new CornerRadius(4.0f));
            drawList.DrawBorder(_target.Preview, theme.Accent, 2.0f, new CornerRadius(4.0f));

            // Drawn after the preview so the buttons sit on top of the wash rather than under it.
            foreach (var indicator in DockDropIndicators.ForHost(_target.HostBounds, _target.GroupBounds))
            {
                RenderIndicator(drawList, theme, indicator);
            }

            foreach (var indicator in DockDropIndicators.ForGroup(_target.GroupBounds))
            {
                RenderIndicator(drawList, theme, indicator);
            }
        }

        /// <summary>
        /// One drop button: a raised chip with a miniature of the layout it would produce inside it. The
        /// miniature is the whole point -- an arrow says "that way", a shaded half says "the panel ends up
        /// here and the pane keeps the rest", which is the thing actually being chosen.
        /// </summary>
        private void RenderIndicator(ManaUiDrawList drawList, UiTheme theme, DockDropIndicator indicator)
        {
            var armed = indicator.Side == _target.Side && indicator.IsRootEdge == _target.IsRootEdge;
            var box = indicator.Box;

            // Lifted off whatever is behind it: these land on top of a viewport or a busy panel, and a flat
            // chip on a light surface has nothing to separate it from the content it is covering.
            drawList.DrawShadow(box, new Vector4(0.0f, 0.0f, 0.0f, 0.35f), 8.0f, new CornerRadius(5.0f));
            drawList.FillRect(box, armed ? theme.Accent : theme.SurfaceRaised, new CornerRadius(5.0f));
            drawList.DrawBorder(
                box, armed ? theme.Accent : theme.BorderStrong, 1.0f, new CornerRadius(5.0f));

            var inner = box.Deflate(new Thickness(6.0f));
            var fill = armed ? theme.TextOnAccent : theme.TextMuted;

            // The pane as it is now, faint, with the half the panel would take drawn solid over it.
            drawList.DrawBorder(inner, fill.WithAlpha(0.55f), 1.0f, new CornerRadius(1.5f));

            var half = indicator.Side switch
            {
                DockSide.Left => new UiRect(inner.X, inner.Y, inner.Width * 0.5f, inner.Height),
                DockSide.Right => new UiRect(inner.Center.X, inner.Y, inner.Width * 0.5f, inner.Height),
                DockSide.Top => new UiRect(inner.X, inner.Y, inner.Width, inner.Height * 0.5f),
                DockSide.Bottom => new UiRect(inner.X, inner.Center.Y, inner.Width, inner.Height * 0.5f),
                _ => inner
            };

            drawList.FillRect(half, fill.WithAlpha(indicator.Side == DockSide.Center ? 0.75f : 0.9f),
                new CornerRadius(1.5f));

            // Centre means "become a tab here", and a solid block alone does not say tab. A stub along the
            // top edge reads as one, at the size a 20px square has to work with.
            if (indicator.Side == DockSide.Center)
            {
                drawList.FillRect(
                    new UiRect(inner.X, inner.Y, inner.Width * 0.45f, 3.0f),
                    armed ? theme.Accent : theme.SurfaceRaised,
                    new CornerRadius(1.0f));
            }
        }

        /// <summary>
        /// The overlay is positioned by its own draw call rather than by layout, so it reports zero size
        /// and paints in absolute coordinates. Rendering is skipped for empty bounds, so it opts out of
        /// that check by claiming the whole root.
        /// </summary>
        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            return Root?.Size ?? finalSize;
        }
    }
}
