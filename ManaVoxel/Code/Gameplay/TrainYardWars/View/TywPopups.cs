using ManaUI;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>
    /// Little labels that rise and fade over the board where something happened: "+4 Wood" at a depot, "+1 prestige" at a
    /// town. A fixed pool of text blocks on a canvas, moved each frame to where their spot is on screen.
    /// </summary>
    internal sealed class TywPopups
    {
        private const int PoolSize = 24;
        private const float Seconds = 1.8f;
        private const float Rise = 1.4f;

        private readonly Canvas _canvas = new();
        private readonly TextBlock[] _labels = new TextBlock[PoolSize];
        private readonly Border[] _pills = new Border[PoolSize];
        private readonly List<Popup> _active = new();
        private readonly float _fontSize;

        private sealed record Popup(Vector3 At, string Text, Vector4 Color, float Born, bool Big, float Seconds);

        public TywPopups(UiTheme theme)
        {
            _fontSize = theme.FontSize;
            // A dark pill behind each label: bare text vanishes over the pale fields and towns.
            for (var i = 0; i < PoolSize; i++)
            {
                _labels[i] = new TextBlock();
                _pills[i] = new Border().Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.72f)).Rounded(8.0f).PaddingOf(8.0f, 2.0f).Content(_labels[i]);
                _pills[i].Visibility = Visibility.Collapsed;
                _canvas.Children.Add(_pills[i]);
            }
        }

        public UiElement Element => _canvas;

        public void Add(Vector3 world, string text, Vector4 color, float now, bool big = false, float seconds = Seconds)
        {
            if (_active.Count >= PoolSize)
            {
                _active.RemoveAt(0);
            }

            // Several on one spot (two loads in at a depot) would print over each other: stack each above the last.
            var stacked = _active.Count(p => Vector3.Distance(p.At with { Y = world.Y }, world) < 0.5f);
            _active.Add(new Popup(world + new Vector3(0, stacked * 0.6f, 0), text, color, now, big, seconds));
        }

        public void Clear() => _active.Clear();

        public void Update(Matrix4x4 viewProjection, float now)
        {
            _active.RemoveAll(p => now - p.Born > p.Seconds);
            var size = new Vector2(_canvas.Bounds.Width, _canvas.Bounds.Height);
            for (var i = 0; i < PoolSize; i++)
            {
                var label = _labels[i];
                var pill = _pills[i];
                if (i >= _active.Count || size.X <= 0.0f)
                {
                    pill.Visibility = Visibility.Collapsed;
                    continue;
                }

                var popup = _active[i];
                var age = (now - popup.Born) / popup.Seconds;
                var clip = Vector4.Transform(new Vector4(popup.At + new Vector3(0, 1.0f + Rise * EaseOut(age), 0), 1.0f), viewProjection);
                if (clip.W <= 0.0f)
                {
                    pill.Visibility = Visibility.Collapsed;
                    continue;
                }

                var x = (clip.X / clip.W * 0.5f + 0.5f) * size.X;
                var y = (0.5f - clip.Y / clip.W * 0.5f) * size.Y;
                label.Text = popup.Text;
                label.FontSize = popup.Big ? _fontSize + 8.0f : _fontSize;
                label.Foreground = popup.Color;
                // Fades out over the last third of its life.
                pill.Opacity = age < 0.66f ? 1.0f : 1.0f - (age - 0.66f) / 0.34f;
                pill.Visibility = Visibility.Visible;
                pill.At(x - pill.DesiredSize.X * 0.5f, y - pill.DesiredSize.Y * 0.5f);
            }

            _canvas.InvalidateArrange();
        }

        private static float EaseOut(float t)
        {
            t = Math.Clamp(t, 0.0f, 1.0f);
            return 1.0f - (1.0f - t) * (1.0f - t);
        }
    }
}
