namespace ManaGeoTools.Code.Osm
{
    /// <summary>Cleans fragments within a tile while leaving junctions and parallel carriageways independent.</summary>
    public static class OsmRoadCleanup
    {
        public static List<RoadLine> Join(IEnumerable<RoadLine> source)
        {
            var lines = source.Where(line => line.Points.Length >= 2).Distinct(new LineComparer()).ToArray();
            var ends = new Dictionary<(double Longitude, double Latitude), List<int>>();
            for (var i = 0; i < lines.Length; i++)
            {
                AddEnd(lines[i].Points[0], i);
                AddEnd(lines[i].Points[^1], i);
            }

            var used = new bool[lines.Length];
            var result = new List<RoadLine>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (used[i])
                {
                    continue;
                }
                var line = lines[i];
                used[i] = true;
                var points = new List<(double Longitude, double Latitude)>(line.Points);
                Extend(points, line);
                points.Reverse();
                Extend(points, line);
                points.Reverse();
                result.Add(line with { Points = points.ToArray() });
            }
            return result;

            void AddEnd((double Longitude, double Latitude) point, int index)
            {
                if (!ends.TryGetValue(point, out var at))
                {
                    at = [];
                    ends.Add(point, at);
                }
                at.Add(index);
            }

            void Extend(List<(double Longitude, double Latitude)> points, RoadLine style)
            {
                while (ends.TryGetValue(points[^1], out var at) && at.Count == 2)
                {
                    var index = at.FirstOrDefault(candidate => !used[candidate], -1);
                    if (index < 0)
                    {
                        break;
                    }
                    var next = lines[index];
                    if (next.Class != style.Class || next.Ramp != style.Ramp || next.Bridge != style.Bridge)
                    {
                        break;
                    }
                    var joining = points[^1];
                    var forward = next.Points[0] == joining;
                    var after = next.Points[^2];
                    if (forward)
                    {
                        after = next.Points[1];
                    }
                    var before = points[^2];
                    var scale = Math.Cos(joining.Latitude * (Math.PI / 180.0));
                    var alignment = (joining.Longitude - before.Longitude) * (after.Longitude - joining.Longitude) * scale * scale
                        + (joining.Latitude - before.Latitude) * (after.Latitude - joining.Latitude);
                    if (alignment <= 0.0)
                    {
                        break;
                    }
                    used[index] = true;
                    if (forward)
                    {
                        points.AddRange(next.Points.AsSpan(1));
                    }
                    else
                    {
                        for (var p = next.Points.Length - 2; p >= 0; p--)
                        {
                            points.Add(next.Points[p]);
                        }
                    }
                }
            }
        }

        private sealed class LineComparer : IEqualityComparer<RoadLine>
        {
            public int GetHashCode(RoadLine line)
                => HashCode.Combine(line.Class, line.Ramp, line.Bridge, line.Points.Length,
                    line.Points[0].GetHashCode() ^ line.Points[^1].GetHashCode());

            public bool Equals(RoadLine? a, RoadLine? b)
            {
                if (a == null || b == null || a.Class != b.Class || a.Ramp != b.Ramp || a.Bridge != b.Bridge || a.Points.Length != b.Points.Length)
                {
                    return a == b;
                }
                if (a.Points.AsSpan().SequenceEqual(b.Points))
                {
                    return true;
                }
                for (var i = 0; i < a.Points.Length; i++)
                {
                    if (a.Points[i] != b.Points[^(i + 1)])
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
