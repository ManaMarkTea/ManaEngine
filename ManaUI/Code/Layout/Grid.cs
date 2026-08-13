namespace ManaUI
{
    /// <summary>
    /// Two-dimensional track layout with Auto, Pixel and Star sizing.
    /// <para>
    /// Columns resolve before rows, and children are then re-measured at their final column width. That
    /// ordering is what makes an Auto row containing wrapped text correct: the text cannot know how tall it
    /// is until it knows how wide it is.
    /// </para>
    /// </summary>
    public class Grid : Panel
    {
        private float _rowSpacing;
        private float _columnSpacing;

        public List<GridTrack> RowDefinitions { get; } = new();
        public List<GridTrack> ColumnDefinitions { get; } = new();

        public float RowSpacing
        {
            get => _rowSpacing;
            set => SetLayoutProperty(ref _rowSpacing, value);
        }

        public float ColumnSpacing
        {
            get => _columnSpacing;
            set => SetLayoutProperty(ref _columnSpacing, value);
        }

        /// <summary>Defines columns from a XAML-style list, e.g. "Auto, *, 2*, 120".</summary>
        public void SetColumns(string definition)
        {
            ParseInto(ColumnDefinitions, definition);
            InvalidateMeasure();
        }

        public void SetRows(string definition)
        {
            ParseInto(RowDefinitions, definition);
            InvalidateMeasure();
        }

        private static void ParseInto(List<GridTrack> tracks, string definition)
        {
            tracks.Clear();
            foreach (var part in definition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                tracks.Add(new GridTrack(GridLength.Parse(part)));
            }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // A grid with no explicit tracks behaves as a single cell, which makes it a usable plain overlay.
            EnsureImplicitTracks();

            var inner = LayoutHelper.Deflate(availableSize, Padding);

            var columnBudget = inner.X - _columnSpacing * MathF.Max(0, ColumnDefinitions.Count - 1);
            ResolveTracks(ColumnDefinitions, columnBudget, isColumns: true);

            // Now that widths are known, every child can be measured for real; that gives the heights the
            // row pass needs.
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                child.Measure(new Vector2(SpanLength(ColumnDefinitions, child, isColumns: true), LayoutHelper.Infinity));
            }

            var rowBudget = inner.Y - _rowSpacing * MathF.Max(0, RowDefinitions.Count - 1);
            ResolveTracks(RowDefinitions, rowBudget, isColumns: false);

            var totalWidth = Total(ColumnDefinitions) + _columnSpacing * MathF.Max(0, ColumnDefinitions.Count - 1);
            var totalHeight = Total(RowDefinitions) + _rowSpacing * MathF.Max(0, RowDefinitions.Count - 1);

            return LayoutHelper.Inflate(new Vector2(totalWidth, totalHeight), Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            EnsureImplicitTracks();

            var content = ContentBounds;

            // Re-resolve against the size actually granted: measure ran against the parent's offer, which
            // may have been larger or infinite, and star tracks must follow the real width.
            ResolveTracks(ColumnDefinitions, content.Width - _columnSpacing * MathF.Max(0, ColumnDefinitions.Count - 1), isColumns: true);

            // Measured again at the widths just settled, exactly as the measure pass does. Resolving auto
            // columns has to ask children for a natural width, which measures them against infinity -- so
            // without this the row pass below sizes rows from whatever a child wanted when offered unlimited
            // room. Nothing noticed until WrapPanel, whose height genuinely depends on its width: its row
            // came out one line tall while it drew two.
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                child.Measure(new Vector2(SpanLength(ColumnDefinitions, child, isColumns: true), LayoutHelper.Infinity));
            }

            ResolveTracks(RowDefinitions, content.Height - _rowSpacing * MathF.Max(0, RowDefinitions.Count - 1), isColumns: false);

            AssignOffsets(ColumnDefinitions, _columnSpacing);
            AssignOffsets(RowDefinitions, _rowSpacing);

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var slot = child.LayoutSlotOrNull;
                var column = ClampIndex(slot?.GridColumn ?? 0, ColumnDefinitions.Count);
                var row = ClampIndex(slot?.GridRow ?? 0, RowDefinitions.Count);

                var x = content.X + ColumnDefinitions[column].Offset;
                var y = content.Y + RowDefinitions[row].Offset;

                child.Arrange(new UiRect(
                    x,
                    y,
                    SpanLength(ColumnDefinitions, child, isColumns: true),
                    SpanLength(RowDefinitions, child, isColumns: false)));
            }

            return finalSize;
        }

        // ----- track resolution ----------------------------------------------------------------------

        /// <summary>
        /// Fills in each track's Resolved size. Pixel tracks are literal, Auto tracks take their largest
        /// child, and Star tracks split what remains.
        /// </summary>
        private void ResolveTracks(List<GridTrack> tracks, float budget, bool isColumns)
        {
            var unconstrained = float.IsInfinity(budget) || float.IsNaN(budget);
            var fixedTotal = 0.0f;
            var starWeight = 0.0f;

            foreach (var track in tracks)
            {
                switch (track.Length.UnitType)
                {
                    case GridUnitType.Pixel:
                        track.Resolved = track.Clamp(track.Length.Value);
                        fixedTotal += track.Resolved;
                        break;
                    case GridUnitType.Auto:
                        track.Resolved = track.MinLength;
                        break;
                    default:
                        // With no finite budget a star track has nothing to divide, so it sizes to content
                        // exactly like Auto. Otherwise it starts at zero and is filled in below.
                        track.Resolved = unconstrained ? track.MinLength : 0.0f;
                        starWeight += track.Length.Value;
                        break;
                }
            }

            MeasureAutoTracks(tracks, isColumns, includeStars: unconstrained);

            foreach (var track in tracks)
            {
                if (track.Length.IsAuto || (unconstrained && track.Length.IsStar))
                {
                    fixedTotal += track.Resolved;
                }
            }

            if (unconstrained || starWeight <= 0.0f)
            {
                return;
            }

            DistributeStars(tracks, MathF.Max(0.0f, budget - fixedTotal), starWeight);
        }

        /// <summary>
        /// Grows content-sized tracks to fit their children. Single-span children are applied first so that
        /// multi-span children only have to cover whatever deficit is left.
        /// </summary>
        private void MeasureAutoTracks(List<GridTrack> tracks, bool isColumns, bool includeStars)
        {
            bool IsContentSized(GridTrack track)
            {
                return track.Length.IsAuto || (includeStars && track.Length.IsStar);
            }

            var hasContentSized = false;
            foreach (var track in tracks)
            {
                if (IsContentSized(track))
                {
                    hasContentSized = true;
                    break;
                }
            }

            if (!hasContentSized)
            {
                return;
            }

            var spanning = new List<UiElement>();

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var (start, span) = GetSpan(child, tracks.Count, isColumns);

                // Columns must be measured before a child's real width is known, so the auto pass measures
                // against infinity on that axis and takes the natural size.
                if (isColumns)
                {
                    child.Measure(LayoutHelper.InfiniteSize);
                }

                var desired = isColumns ? child.DesiredSize.X : child.DesiredSize.Y;

                if (span == 1)
                {
                    var track = tracks[start];
                    if (IsContentSized(track))
                    {
                        track.Resolved = track.Clamp(MathF.Max(track.Resolved, desired));
                    }

                    continue;
                }

                spanning.Add(child);
            }

            foreach (var child in spanning)
            {
                var (start, span) = GetSpan(child, tracks.Count, isColumns);
                var desired = isColumns ? child.DesiredSize.X : child.DesiredSize.Y;
                var spacing = (isColumns ? _columnSpacing : _rowSpacing) * (span - 1);

                var covered = spacing;
                var growable = 0;
                for (var i = start; i < start + span; i++)
                {
                    covered += tracks[i].Resolved;
                    if (IsContentSized(tracks[i]))
                    {
                        growable++;
                    }
                }

                var deficit = desired - covered;
                if (deficit <= 0.0f || growable == 0)
                {
                    continue;
                }

                var share = deficit / growable;
                for (var i = start; i < start + span; i++)
                {
                    if (IsContentSized(tracks[i]))
                    {
                        tracks[i].Resolved = tracks[i].Clamp(tracks[i].Resolved + share);
                    }
                }
            }
        }

        /// <summary>
        /// Splits <paramref name="available"/> by star weight. Tracks that hit a min/max clamp are frozen and
        /// the remainder re-divided among the rest, which is why this iterates.
        /// </summary>
        private static void DistributeStars(List<GridTrack> tracks, float available, float totalWeight)
        {
            var active = new List<GridTrack>();
            foreach (var track in tracks)
            {
                if (track.Length.IsStar)
                {
                    active.Add(track);
                }
            }

            // Bounded by the track count: each pass freezes at least one track, or stops.
            for (var pass = 0; pass < active.Count + 1 && active.Count > 0; pass++)
            {
                var clampedAny = false;

                for (var i = active.Count - 1; i >= 0; i--)
                {
                    var track = active[i];
                    var share = totalWeight > 0.0f ? available * (track.Length.Value / totalWeight) : 0.0f;
                    var clamped = track.Clamp(share);

                    if (!LayoutHelper.AreClose(clamped, share))
                    {
                        track.Resolved = clamped;
                        available -= clamped;
                        totalWeight -= track.Length.Value;
                        active.RemoveAt(i);
                        clampedAny = true;
                    }
                    else
                    {
                        track.Resolved = share;
                    }
                }

                if (!clampedAny)
                {
                    break;
                }

                available = MathF.Max(0.0f, available);
            }
        }

        private static void AssignOffsets(List<GridTrack> tracks, float spacing)
        {
            var offset = 0.0f;
            foreach (var track in tracks)
            {
                track.Offset = offset;
                offset += track.Resolved + spacing;
            }
        }

        private float SpanLength(List<GridTrack> tracks, UiElement child, bool isColumns)
        {
            var (start, span) = GetSpan(child, tracks.Count, isColumns);
            var spacing = isColumns ? _columnSpacing : _rowSpacing;

            var total = spacing * (span - 1);
            for (var i = start; i < start + span; i++)
            {
                total += tracks[i].Resolved;
            }

            return MathF.Max(0.0f, total);
        }

        private static (int Start, int Span) GetSpan(UiElement child, int trackCount, bool isColumns)
        {
            var slot = child.LayoutSlotOrNull;
            var requested = isColumns ? slot?.GridColumn ?? 0 : slot?.GridRow ?? 0;
            var requestedSpan = isColumns ? slot?.GridColumnSpan ?? 1 : slot?.GridRowSpan ?? 1;

            var start = ClampIndex(requested, trackCount);
            var span = Math.Clamp(requestedSpan, 1, trackCount - start);
            return (start, span);
        }

        private static int ClampIndex(int index, int count)
        {
            return count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
        }

        private static float Total(List<GridTrack> tracks)
        {
            var total = 0.0f;
            foreach (var track in tracks)
            {
                total += track.Resolved;
            }

            return total;
        }

        private void EnsureImplicitTracks()
        {
            if (RowDefinitions.Count == 0)
            {
                RowDefinitions.Add(new GridTrack(GridLength.Star));
            }

            if (ColumnDefinitions.Count == 0)
            {
                ColumnDefinitions.Add(new GridTrack(GridLength.Star));
            }
        }
    }
}
