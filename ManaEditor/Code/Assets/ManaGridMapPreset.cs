using System.Text.Json;
using ManaEditor.Code.GridMaps;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.Assets
{
    public sealed class ManaGridMapPreset
    {
        public const string Extension = ".managridpreset";
        public string Format { get; set; } = "mana-grid-preset";
        public int Version { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Grid Map Preset";
        public ManaLevelGridMap Settings { get; set; } = new();
        public ManaGridGeneratorPreset? Generator { get; set; }

        public static ManaGridMapPreset Capture(ManaGridMapNode map, string name)
        {
            var settings = ManaLevelGridMapCodec.Encode(map);
            settings.Count = 0;
            settings.Data = string.Empty;
            settings.Layers = null;
            return new() { Name = name, Settings = settings };
        }

        public static ManaGridMapPreset Load(string path)
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("format", out _)
                || !document.RootElement.TryGetProperty("settings", out _) || !document.RootElement.TryGetProperty("id", out _))
            {
                throw new InvalidDataException("Grid map preset is missing its format, asset ID, or settings.");
            }
            var preset = JsonSerializer.Deserialize<ManaGridMapPreset>(json, ManaLevel.SerializerOptions);
            if (preset == null || preset.Format != "mana-grid-preset" || preset.Version != 1 || preset.Settings == null || string.IsNullOrWhiteSpace(preset.Id))
            {
                throw new InvalidDataException("Unsupported grid map preset.");
            }
            return preset;
        }

        public void Save(string path) =>
            ManaContentLibrary.Write(path, ManaCore.Serialization.ManaJsonFormat.Indent(JsonSerializer.Serialize(this, ManaLevel.SerializerOptions)));

        /// <summary>Preserves painted cells by remapping palette indices by asset reference.</summary>
        public void Apply(ManaGridMapNode map)
        {
            var old = map.CaptureState();
            var palette = Settings.Palette.ToList();
            var settings = ManaLevelGridMapCodec.DecodeSettings(Settings);
            while (settings.Count < palette.Count)
            {
                settings.Add(GridMapItemSettings.Default);
            }
            var indices = new Dictionary<int, int>();
            for (var index = 0; index < old.Palette.Count; index++)
            {
                var target = palette.IndexOf(old.Palette[index]);
                if (target < 0)
                {
                    target = palette.Count;
                    palette.Add(old.Palette[index]);
                    settings.Add(old.Settings[index]);
                }
                indices[index] = target;
            }
            var layers = old.Layers.Select(layer => layer with
            {
                Cells = layer.Cells.ToDictionary(p => p.Key, p => p.Value with { Item = indices[p.Value.Item] })
            }).ToList();
            var active = map.ActiveLayer;
            map.Load(ManaLevelGridMapCodec.DecodeCellSize(Settings), palette, layers, Settings.Collision,
                settings, ManaLevelGridMapCodec.DecodeAutoTiles(Settings));
            map.ActiveLayer = active;
        }
    }

    public sealed class ManaGridGeneratorPreset
    {
        public GridMapWfcRules Rules { get; set; } = new();
        public int Width { get; set; } = 16;
        public int Depth { get; set; } = 16;
        public int Seed { get; set; } = 1;
        public int Rooms { get; set; } = 4;
        public int BigRooms { get; set; } = 2;
        public bool KeepPainted { get; set; } = true;
        public bool ConnectedOnly { get; set; } = true;

        public static ManaGridGeneratorPreset Capture() => new()
        {
            Rules = CloneRules(GridMapWfcEditing.Rules), Width = GridMapWfcEditing.Width,
            Depth = GridMapWfcEditing.Depth, Seed = GridMapWfcEditing.Seed, Rooms = GridMapWfcEditing.Rooms,
            BigRooms = GridMapWfcEditing.BigRooms, KeepPainted = GridMapWfcEditing.KeepPainted,
            ConnectedOnly = GridMapWfcEditing.ConnectedOnly
        };

        private static GridMapWfcRules CloneRules(GridMapWfcRules rules) => JsonSerializer.Deserialize<GridMapWfcRules>(
            JsonSerializer.Serialize(rules, ManaLevel.SerializerOptions), ManaLevel.SerializerOptions)!;

        public void Apply()
        {
            GridMapWfcEditing.Replace(CloneRules(Rules), null);
            GridMapWfcEditing.Width = Width;
            GridMapWfcEditing.Depth = Depth;
            GridMapWfcEditing.Seed = Seed;
            GridMapWfcEditing.Rooms = Rooms;
            GridMapWfcEditing.BigRooms = BigRooms;
            GridMapWfcEditing.KeepPainted = KeepPainted;
            GridMapWfcEditing.ConnectedOnly = ConnectedOnly;
        }
    }
}
