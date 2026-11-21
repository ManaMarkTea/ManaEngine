using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ManaGeoTools.Code.Osm
{
    public enum RoadClass
    {
        Motorway,
        Trunk,
        Primary
    }

    /// <summary>One stretch of road: its class and its points as (longitude, latitude) degrees.</summary>
    public sealed record RoadLine(RoadClass Class, (double Longitude, double Latitude)[] Points, bool Ramp = false, bool Bridge = false);

    /// <summary>
    /// One area of inland water (lake, river, pond, reservoir -- the sea is left out) in a tile: its rings' points as
    /// x, y in tile units (0 to <paramref name="Extent"/>, y down), the outer ring first, and where each hole starts.
    /// </summary>
    public sealed record WaterArea(double[] Points, int[] HoleStarts, double Extent, string? Class);

    /// <summary>
    /// The larger roads of one web-mercator tile from OpenFreeMap's vector tiles (the OpenMapTiles schema's
    /// <c>transportation</c> layer: motorway, trunk and primary). A CDN answers in a tenth of a second where the
    /// public Overpass servers took tens of seconds or failed. Data (c) OpenMapTiles, (c) OpenStreetMap contributors.
    /// </summary>
    public static class OsmRoadTiles
    {
        public const string TileJsonUrl = "https://tiles.openfreemap.org/planet";

        // The tile URL names the build of the planet; read once a run from the TileJSON.
        private static Task<string>? _template;

        public static async Task<byte[]> FetchAsync(HttpClient http, int zoom, int x, int y, CancellationToken cancellation)
        {
            var template = await TemplateAsync(http, cancellation).ConfigureAwait(false);
            var url = template.Replace("{z}", zoom.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            using var response = await http.GetAsync(url, cancellation).ConfigureAwait(false);
            // An empty tile (open sea) may come back 204 or 404: no roads, not a failure.
            if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
            {
                return [];
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
        }

        private static Task<string> TemplateAsync(HttpClient http, CancellationToken cancellation)
        {
            var task = _template;
            if (task == null || task.IsFaulted || task.IsCanceled)
            {
                task = ReadTemplateAsync(http, cancellation);
                _template = task;
            }
            return task;
        }

        private static async Task<string> ReadTemplateAsync(HttpClient http, CancellationToken cancellation)
        {
            var json = await http.GetStringAsync(TileJsonUrl, cancellation).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("tiles")[0].GetString()
                ?? throw new InvalidOperationException("OpenFreeMap's TileJSON names no tiles.");
        }

        private static byte[] Unzipped(byte[] data)
        {
            if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                using var output = new MemoryStream();
                input.CopyTo(output);
                return output.ToArray();
            }
            return data;
        }

        /// <summary>The motorways, trunk and primary roads in a tile's bytes (gzipped or not).</summary>
        public static List<RoadLine> Parse(byte[] data, int zoom, int x, int y)
        {
            var lines = new List<RoadLine>();
            if (data.Length == 0)
            {
                return lines;
            }
            data = Unzipped(data);

            var tile = new ProtoReader(data);
            while (tile.Next(out var field, out var wire))
            {
                if (field == 3 && wire == 2)
                {
                    ReadLayer(tile.Bytes(), zoom, x, y, lines);
                }
                else
                {
                    tile.Skip(wire);
                }
            }
            return lines;
        }

        private static void ReadLayer(ReadOnlySpan<byte> bytes, int zoom, int x, int y, List<RoadLine> lines)
        {
            var name = "";
            var extent = 4096u;
            var keys = new List<string>();
            var values = new List<string?>();
            var features = new List<(int Start, int Length)>();
            var layer = new ProtoReader(bytes);
            while (layer.Next(out var field, out var wire))
            {
                switch (field)
                {
                    case 1 when wire == 2:
                        name = Encoding.UTF8.GetString(layer.Bytes());
                        break;
                    case 2 when wire == 2:
                        features.Add(layer.Range());
                        break;
                    case 3 when wire == 2:
                        keys.Add(Encoding.UTF8.GetString(layer.Bytes()));
                        break;
                    case 4 when wire == 2:
                        values.Add(ReadTagValue(layer.Bytes()));
                        break;
                    case 5 when wire == 0:
                        extent = (uint)layer.Varint();
                        break;
                    default:
                        layer.Skip(wire);
                        break;
                }
            }
            // Features come before the keys and values they point into, so they are read once the layer is done.
            if (name != "transportation" || extent == 0)
            {
                return;
            }
            var classKey = keys.IndexOf("class");
            if (classKey < 0)
            {
                return;
            }

            var rampKey = keys.IndexOf("ramp");
            var structureKey = keys.IndexOf("brunnel");
            foreach (var (start, length) in features)
            {
                ReadFeature(bytes.Slice(start, length), classKey, rampKey, structureKey,
                    values, zoom, x, y, extent, lines);
            }
        }

        private static void ReadFeature(ReadOnlySpan<byte> bytes, int classKey, int rampKey, int structureKey, List<string?> values,
            int zoom, int tileX, int tileY, double extent, List<RoadLine> lines)
        {
            RoadClass? roadClass = null;
            var ramp = false;
            string? structure = null;
            var geometryStart = -1;
            var geometryLength = 0;
            var type = 0;
            var feature = new ProtoReader(bytes);
            while (feature.Next(out var field, out var wire))
            {
                if (field == 2 && wire == 2)
                {
                    var tags = new ProtoReader(feature.Bytes());
                    while (!tags.Done)
                    {
                        var key = (int)tags.Varint();
                        var value = (int)tags.Varint();
                        if ((uint)value >= values.Count)
                        {
                            continue;
                        }
                        if (key == classKey)
                        {
                            roadClass = ClassOf(values[value]);
                        }
                        else if (key == rampKey)
                        {
                            ramp = values[value] is "1" or "true";
                        }
                        else if (key == structureKey)
                        {
                            structure = values[value];
                        }
                    }
                }
                else if (field == 3 && wire == 0)
                {
                    type = (int)feature.Varint();
                }
                else if (field == 4 && wire == 2)
                {
                    (geometryStart, geometryLength) = feature.Range();
                }
                else
                {
                    feature.Skip(wire);
                }
            }
            // Type 2 is a line string; areas (bridges drawn as polygons) are left out.
            if (roadClass is not { } found || type != 2 || geometryStart < 0 || structure == "tunnel")
            {
                return;
            }

            var commands = new ProtoReader(bytes.Slice(geometryStart, geometryLength));
            var run = new List<(double X, double Y)>();
            long cursorX = 0;
            long cursorY = 0;
            while (!commands.Done)
            {
                var command = (uint)commands.Varint();
                var id = command & 7;
                var count = command >> 3;
                if (id == 1 && run.Count >= 2)
                {
                    AddRuns();
                }
                if (id == 1)
                {
                    run.Clear();
                }
                if (id != 1 && id != 2)
                {
                    continue;
                }
                for (var i = 0; i < count && !commands.Done; i++)
                {
                    cursorX += ZigZag(commands.Varint());
                    cursorY += ZigZag(commands.Varint());
                    run.Add((cursorX, cursorY));
                }
            }
            if (run.Count >= 2)
            {
                AddRuns();
            }

            void AddRuns()
            {
                foreach (var clipped in ClipRoad(run, extent))
                {
                    var points = Array.ConvertAll(clipped, p => TilePointToDegrees(zoom, tileX, tileY, p.X, p.Y, extent));
                    lines.Add(new RoadLine(found, points, ramp, structure == "bridge"));
                }
            }
        }

        // The vector tile includes a buffer beyond each edge. Draw a segment only in the tile that owns it.
        public static IEnumerable<(double X, double Y)[]> ClipRoad(IReadOnlyList<(double X, double Y)> points, double extent)
        {
            var run = new List<(double X, double Y)>();
            for (var i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                if (dx == 0 && dy == 0)
                {
                    continue;
                }
                var start = 0.0;
                var end = 1.0;
                if ((dx == 0 && a.X == extent) || (dy == 0 && a.Y == extent)
                    || !ClipAxis(a.X, dx, extent, ref start, ref end) || !ClipAxis(a.Y, dy, extent, ref start, ref end)
                    || start >= end)
                {
                    if (run.Count >= 2)
                    {
                        yield return run.ToArray();
                    }
                    run.Clear();
                    continue;
                }
                var from = Point(start);
                var to = Point(end);
                if (run.Count == 0 || run[^1] != from)
                {
                    if (run.Count >= 2)
                    {
                        yield return run.ToArray();
                    }
                    run.Clear();
                    run.Add(from);
                }
                run.Add(to);
                (double X, double Y) Point(double t) =>
                    (Math.Clamp(a.X + dx * t, 0, extent), Math.Clamp(a.Y + dy * t, 0, extent));
            }
            if (run.Count >= 2)
            {
                yield return run.ToArray();
            }
        }

        private static bool ClipAxis(double origin, double delta, double extent, ref double start, ref double end)
        {
            if (delta == 0.0)
            {
                return origin >= 0.0 && origin <= extent;
            }
            var a = -origin / delta;
            var b = (extent - origin) / delta;
            start = Math.Max(start, Math.Min(a, b));
            end = Math.Min(end, Math.Max(a, b));
            return start < end;
        }

        /// <summary>The lakes, rivers and ponds in a tile's bytes (the OpenMapTiles <c>water</c> layer, less the sea).</summary>
        public static List<WaterArea> ParseWater(byte[] data)
        {
            var areas = new List<WaterArea>();
            if (data.Length == 0)
            {
                return areas;
            }
            data = Unzipped(data);
            var tile = new ProtoReader(data);
            while (tile.Next(out var field, out var wire))
            {
                if (field == 3 && wire == 2)
                {
                    ReadWaterLayer(tile.Bytes(), areas);
                }
                else
                {
                    tile.Skip(wire);
                }
            }
            return areas;
        }

        private static void ReadWaterLayer(ReadOnlySpan<byte> bytes, List<WaterArea> areas)
        {
            var name = "";
            var extent = 4096u;
            var keys = new List<string>();
            var values = new List<string?>();
            var features = new List<(int Start, int Length)>();
            var layer = new ProtoReader(bytes);
            while (layer.Next(out var field, out var wire))
            {
                switch (field)
                {
                    case 1 when wire == 2:
                        name = Encoding.UTF8.GetString(layer.Bytes());
                        break;
                    case 2 when wire == 2:
                        features.Add(layer.Range());
                        break;
                    case 3 when wire == 2:
                        keys.Add(Encoding.UTF8.GetString(layer.Bytes()));
                        break;
                    case 4 when wire == 2:
                        values.Add(ReadTagValue(layer.Bytes()));
                        break;
                    case 5 when wire == 0:
                        extent = (uint)layer.Varint();
                        break;
                    default:
                        layer.Skip(wire);
                        break;
                }
            }
            if (name != "water")
            {
                return;
            }
            var classKey = keys.IndexOf("class");
            foreach (var (start, length) in features)
            {
                ReadWaterFeature(bytes.Slice(start, length), classKey, values, extent, areas);
            }
        }

        private static void ReadWaterFeature(ReadOnlySpan<byte> bytes, int classKey, List<string?> values, double extent, List<WaterArea> areas)
        {
            string? waterClass = null;
            var geometryStart = -1;
            var geometryLength = 0;
            var type = 0;
            var feature = new ProtoReader(bytes);
            while (feature.Next(out var field, out var wire))
            {
                if (field == 2 && wire == 2)
                {
                    var tags = new ProtoReader(feature.Bytes());
                    while (!tags.Done)
                    {
                        var key = (int)tags.Varint();
                        var value = (int)tags.Varint();
                        if (key == classKey && value < values.Count)
                        {
                            waterClass = values[value];
                        }
                    }
                }
                else if (field == 3 && wire == 0)
                {
                    type = (int)feature.Varint();
                }
                else if (field == 4 && wire == 2)
                {
                    (geometryStart, geometryLength) = feature.Range();
                }
                else
                {
                    feature.Skip(wire);
                }
            }
            // Type 3 is a polygon. The sea is in the satellite picture already, and its tiles are one huge area each.
            if (type != 3 || geometryStart < 0 || waterClass == "ocean")
            {
                return;
            }

            // Rings: an outer one (positive area, y down) starts a new area, and the negative ones after it are its holes.
            var points = new List<double>();
            var holes = new List<int>();
            var ring = new List<double>();
            var commands = new ProtoReader(bytes.Slice(geometryStart, geometryLength));
            long cursorX = 0;
            long cursorY = 0;
            while (!commands.Done)
            {
                var command = (uint)commands.Varint();
                var id = command & 7;
                var count = command >> 3;
                if (id == 7)
                {
                    AddRing(ring, points, holes, extent, waterClass, areas);
                    ring.Clear();
                    continue;
                }
                if (id != 1 && id != 2)
                {
                    break;
                }
                for (var i = 0; i < count && !commands.Done; i++)
                {
                    cursorX += ZigZag(commands.Varint());
                    cursorY += ZigZag(commands.Varint());
                    ring.Add(cursorX);
                    ring.Add(cursorY);
                }
            }
            if (points.Count >= 6)
            {
                areas.Add(new WaterArea(points.ToArray(), holes.ToArray(), extent, waterClass));
            }
        }

        private static void AddRing(List<double> ring, List<double> points, List<int> holes, double extent, string? waterClass, List<WaterArea> areas)
        {
            if (ring.Count < 6)
            {
                return;
            }
            var area = 0.0;
            for (int i = 0, j = ring.Count - 2; i < ring.Count; j = i, i += 2)
            {
                area += ring[j] * ring[i + 1] - ring[i] * ring[j + 1];
            }
            if (area > 0)
            {
                if (points.Count >= 6)
                {
                    areas.Add(new WaterArea(points.ToArray(), holes.ToArray(), extent, waterClass));
                }
                points.Clear();
                holes.Clear();
            }
            else if (points.Count == 0)
            {
                // A hole with no outer ring before it: nothing to cut it from.
                return;
            }
            else
            {
                holes.Add(points.Count / 2);
            }
            points.AddRange(ring);
        }

        /// <summary>A point in a tile's own units (0 to <paramref name="extent"/>, y down) to degrees.</summary>
        public static (double Longitude, double Latitude) TilePointToDegrees(int zoom, int tileX, int tileY, double px, double py, double extent)
        {
            var tileShare = 1.0 / (1 << zoom);
            var worldX = (tileX + px / extent) * tileShare;
            var worldY = (tileY + py / extent) * tileShare;
            var longitude = worldX * 360.0 - 180.0;
            var latitude = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * worldY))) * 180.0 / Math.PI;
            return (longitude, latitude);
        }

        /// <summary>The web-mercator tile holding a point.</summary>
        public static (int X, int Y) TileAt(double longitude, double latitude, int zoom)
        {
            var count = 1 << zoom;
            var x = (int)Math.Floor((longitude + 180.0) / 360.0 * count);
            var radians = Math.Clamp(latitude, -85.05, 85.05) * Math.PI / 180.0;
            var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(radians) + 1.0 / Math.Cos(radians)) / Math.PI) / 2.0 * count);
            return (((x % count) + count) % count, Math.Clamp(y, 0, count - 1));
        }

        /// <summary>A tile's corners: (west, south, east, north) in degrees.</summary>
        public static (double West, double South, double East, double North) Bounds(int zoom, int x, int y)
        {
            var count = (double)(1 << zoom);
            var west = x / count * 360.0 - 180.0;
            var east = (x + 1) / count * 360.0 - 180.0;
            var north = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / count))) * 180.0 / Math.PI;
            var south = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * (y + 1) / count))) * 180.0 / Math.PI;
            return (west, south, east, north);
        }

        private static RoadClass? ClassOf(string? value)
        {
            switch (value)
            {
                case "motorway":
                    return RoadClass.Motorway;
                case "trunk":
                    return RoadClass.Trunk;
                case "primary":
                    return RoadClass.Primary;
                default:
                    return null;
            }
        }

        private static string? ReadTagValue(ReadOnlySpan<byte> bytes)
        {
            var value = new ProtoReader(bytes);
            string? text = null;
            while (value.Next(out var field, out var wire))
            {
                if (field == 1 && wire == 2)
                {
                    text = Encoding.UTF8.GetString(value.Bytes());
                }
                else if ((field is 4 or 5 or 6 or 7) && wire == 0)
                {
                    var number = value.Varint();
                    if ((number == 1 && field != 6) || (number == 2 && field == 6))
                    {
                        text = "1";
                    }
                }
                else
                {
                    value.Skip(wire);
                }
            }
            return text;
        }

        private static long ZigZag(ulong value)
        {
            return (long)(value >> 1) ^ -(long)(value & 1);
        }

        /// <summary>Just enough protobuf to walk a vector tile: varints, length-delimited fields, skipping the rest.</summary>
        private ref struct ProtoReader(ReadOnlySpan<byte> data)
        {
            private readonly ReadOnlySpan<byte> _data = data;
            private int _position;

            public readonly bool Done => _position >= _data.Length;

            public bool Next(out int field, out int wire)
            {
                if (Done)
                {
                    field = 0;
                    wire = 0;
                    return false;
                }
                var key = Varint();
                field = (int)(key >> 3);
                wire = (int)(key & 7);
                return true;
            }

            public ulong Varint()
            {
                ulong result = 0;
                var shift = 0;
                while (_position < _data.Length)
                {
                    var b = _data[_position++];
                    result |= (ulong)(b & 0x7f) << shift;
                    if ((b & 0x80) == 0)
                    {
                        break;
                    }
                    shift += 7;
                }
                return result;
            }

            public ReadOnlySpan<byte> Bytes()
            {
                var (start, length) = Range();
                return _data.Slice(start, length);
            }

            /// <summary>A length-delimited field's place in this reader's data.</summary>
            public (int Start, int Length) Range()
            {
                var length = (int)Varint();
                var start = _position;
                _position = Math.Min(_data.Length, _position + length);
                return (start, _position - start);
            }

            public void Skip(int wire)
            {
                switch (wire)
                {
                    case 0:
                        Varint();
                        break;
                    case 1:
                        _position += 8;
                        break;
                    case 2:
                        Range();
                        break;
                    case 5:
                        _position += 4;
                        break;
                    default:
                        _position = _data.Length;
                        break;
                }
            }
        }
    }
}
