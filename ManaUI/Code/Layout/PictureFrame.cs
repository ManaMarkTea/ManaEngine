namespace ManaUI
{
    /// <summary>A border drawn as a nine-slice picture over its background, so a layout can dress a panel in its game's own art.</summary>
    public class PictureFrame : Border
    {
        private string? _picture;
        private float _slice = 16.0f;
        private float _sliceScale = 1.0f;
        private float _backgroundInset;

        /// <summary>The picture's path.</summary>
        public string? Picture
        {
            get => _picture;
            set => SetVisualProperty(ref _picture, value);
        }

        /// <summary>How far in from each edge the corners reach, in the picture's pixels.</summary>
        public float Slice
        {
            get => _slice;
            set => SetVisualProperty(ref _slice, value);
        }

        /// <summary>How large those corners are drawn: 0.5 draws a 40 pixel corner 20 wide.</summary>
        public float SliceScale
        {
            get => _sliceScale;
            set => SetVisualProperty(ref _sliceScale, value);
        }

        /// <summary>Keeps the background in from the edge, so it does not show past a picture's rounded corners.</summary>
        public float BackgroundInset
        {
            get => _backgroundInset;
            set => SetVisualProperty(ref _backgroundInset, value);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (_backgroundInset > 0.0f)
            {
                var inner = Bounds;
                drawList.FillRect(new UiRect(inner.X + _backgroundInset, inner.Y + _backgroundInset, inner.Width - _backgroundInset * 2.0f, inner.Height - _backgroundInset * 2.0f), Background, CornerRadius);
            }
            else
            {
                base.OnRender(drawList);
            }

            if (UiPictures.Get(this, _picture) is { } found)
            {
                drawList.DrawNineSlice(Bounds, new UiSkinSlice(found.Uv, found.Size, new Thickness(_slice)), UiColor.White, _sliceScale);
            }
        }
    }
}
