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
    public sealed record RoadLine(RoadClass Class, (double Longitude, double Latitude)[] Points);

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

        /// <summary>The motorways, trunk and primary roads in a tile's bytes (gzipped or not).</summary>
        public static List<RoadLine> Parse(byte[] data, int zoom, int x, int y)
        {
            var lines = new List<RoadLine>();
            if (data.Length == 0)
            {
                return lines;
            }
            if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                using var output = new MemoryStream();
                input.CopyTo(output);
                data = output.ToArray();
            }

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
                        values.Add(ReadStringValue(layer.Bytes()));
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
            if (name != "transportation")
            {
                return;
            }
            var classKey = keys.IndexOf("class");
            if (classKey < 0)
            {
                return;
            }

            // A tile is 1 / 2^zoom of the world across; a tile unit is that over its extent.
            var tileShare = 1.0 / (1 << zoom);
            var scale = tileShare / extent;
            foreach (var (start, length) in features)
            {
                ReadFeature(bytes.Slice(start, length), classKey, values, x, y, scale, tileShare, lines);
            }
        }

        private static void ReadFeature(ReadOnlySpan<byte> bytes, int classKey, List<string?> values, int tileX, int tileY,
            double scale, double extentShare, List<RoadLine> lines)
        {
            RoadClass? roadClass = null;
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
                            roadClass = ClassOf(values[value]);
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
            if (roadClass is not { } found || type != 2 || geometryStart < 0)
            {
                return;
            }

            var commands = new ProtoReader(bytes.Slice(geometryStart, geometryLength));
            var run = new List<(double Longitude, double Latitude)>();
            long cursorX = 0;
            long cursorY = 0;
            while (!commands.Done)
            {
                var command = (uint)commands.Varint();
                var id = command & 7;
                var count = command >> 3;
                if (id == 1 && run.Count >= 2)
                {
                    lines.Add(new RoadLine(found, run.ToArray()));
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
                    run.Add(ToLongitudeLatitude(tileX, tileY, cursorX, cursorY, scale, extentShare));
                }
            }
            if (run.Count >= 2)
            {
                lines.Add(new RoadLine(found, run.ToArray()));
            }
        }

        // Tile pixel (cursor) to degrees: the tile's own corner plus its share of the world, then web mercator undone.
        private static (double Longitude, double Latitude) ToLongitudeLatitude(int tileX, int tileY, long cursorX, long cursorY,
            double scale, double tileShare)
        {
            var worldX = tileX * tileShare + cursorX * scale;
            var worldY = tileY * tileShare + cursorY * scale;
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

        private static string? ReadStringValue(ReadOnlySpan<byte> bytes)
        {
            var value = new ProtoReader(bytes);
            string? text = null;
            while (value.Next(out var field, out var wire))
            {
                if (field == 1 && wire == 2)
                {
                    text = Encoding.UTF8.GetString(value.Bytes());
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
