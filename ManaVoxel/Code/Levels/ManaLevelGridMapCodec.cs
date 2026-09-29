using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;

namespace ManaVoxel.Code.Levels
{
    /// <summary>Packs and unpacks a <see cref="ManaGridMapNode"/> for <see cref="ManaLevelGridMap"/>.</summary>
    public static class ManaLevelGridMapCodec
    {
        private const int BytesPerCell = 10;

        private const int GeneratedBit = 0x80;

        /// <summary>Records written before layers, which have no layer byte and all belong to the first layer.</summary>
        private const int BytesPerUnlayeredCell = 9;

        public static ManaLevelGridMap Encode(ManaGridMapNode gridMap)
        {
            var cells = Enumerable.Range(0, gridMap.LayerCount)
                .SelectMany(layer => gridMap.LayerCells(layer)
                    .OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.Z).ThenBy(pair => pair.Key.X)
                    .Select(pair => (Layer: layer, Cell: pair.Key, Tile: pair.Value, Generated: gridMap.LayerGenerated(layer).Contains(pair.Key))))
                .ToList();
            var data = new byte[cells.Count * BytesPerCell];
            for (var i = 0; i < cells.Count; i++)
            {
                var (layer, cell, tile, generated) = cells[i];
                var offset = i * BytesPerCell;
                WriteCoordinate(data, offset, cell.X);
                WriteCoordinate(data, offset + 2, cell.Y);
                WriteCoordinate(data, offset + 4, cell.Z);
                BitConverter.TryWriteBytes(data.AsSpan(offset + 6, 2), checked((ushort)tile.Item));
                // The top bit marks a generator's tile, so a level reopened still knows which tiles were painted.
                data[offset + 8] = (byte)(tile.Orientation | (generated ? GeneratedBit : 0));
                data[offset + 9] = checked((byte)layer);
            }

            var onlyDefaultLayer = gridMap.LayerCount == 1
                && gridMap.LayerName(0) == ManaGridMapNode.DefaultLayerName
                && gridMap.IsLayerVisible(0);

            var size = gridMap.CellSize;
            return new ManaLevelGridMap
            {
                CellSize = new[] { size.X, size.Y, size.Z },
                // Readable where the zip is one of ours, the same as a placed mesh; see MeshAssetLibrary.ToReadableReference.
                Palette = gridMap.Palette
                    .Select(entry => MeshAssetLibrary.ToReadableReference(ManaGridMapNode.AssetOf(entry)) + ManaGridMapNode.VariantOf(entry))
                    .ToList(),
                Collision = gridMap.Collision,
                Tiles = Enumerable.Range(0, gridMap.Palette.Count).All(item => gridMap.ItemSettings(item) == GridMapItemSettings.Default)
                    ? null
                    : Enumerable.Range(0, gridMap.Palette.Count)
                        .Select(item => gridMap.ItemSettings(item))
                        .Select(settings => new ManaLevelGridMapTile
                        {
                            Width = settings.Width,
                            Depth = settings.Depth,
                            Scale = settings.Scale,
                            Turn = settings.Turn,
                            Align = settings.Align.ToString(),
                            Smooth = settings.Smooth,
                            Offset = settings.Offset == Vector3.Zero ? null : new[] { settings.Offset.X, settings.Offset.Y, settings.Offset.Z }
                        })
                        .ToList(),
                Layers = onlyDefaultLayer
                    ? null
                    : Enumerable.Range(0, gridMap.LayerCount)
                        .Select(layer => new ManaLevelGridMapLayer { Name = gridMap.LayerName(layer), Visible = gridMap.IsLayerVisible(layer) })
                        .ToList(),
                AutoTiles = gridMap.AutoTiles.Count == 0
                    ? null
                    : gridMap.AutoTiles
                        .Select(set => new ManaLevelGridMapAutoTile
                        {
                            Name = set.Name,
                            Pieces = set.Pieces
                                .Select(piece => new ManaLevelGridMapAutoTilePiece { Item = piece.Item, Sides = GridMapAutoTile.Describe(piece.Sides) })
                                .ToList()
                        })
                        .ToList(),
                Count = cells.Count,
                Data = Convert.ToBase64String(data)
            };
        }

        public static List<GridMapAutoTileSet> DecodeAutoTiles(ManaLevelGridMap encoded) =>
            (encoded.AutoTiles ?? [])
                .Select(set => new GridMapAutoTileSet(
                    set.Name,
                    set.Pieces
                        .Where(piece => (uint)piece.Item < (uint)encoded.Palette.Count)
                        .Select(piece => new GridMapAutoTilePiece(piece.Item, GridMapAutoTile.Parse(piece.Sides)))
                        .ToList()))
                .ToList();

        /// <summary>Every layer, with its cells. A map saved before layers comes back as one default layer.</summary>
        public static List<GridMapLayerState> DecodeLayers(ManaLevelGridMap encoded)
        {
            var names = encoded.Layers is { Count: > 0 } saved
                ? saved
                : new List<ManaLevelGridMapLayer> { new() { Name = ManaGridMapNode.DefaultLayerName, Visible = true } };
            var cells = names.Select(_ => new Dictionary<GridMapCell, GridMapTile>()).ToList();
            var generated = names.Select(_ => new HashSet<GridMapCell>()).ToList();
            foreach (var (layer, cell, tile, isGenerated) in DecodeRecords(encoded))
            {
                var index = Math.Min(layer, cells.Count - 1);
                cells[index][cell] = tile;
                if (isGenerated)
                {
                    generated[index].Add(cell);
                }
            }

            return names.Select((layer, i) => new GridMapLayerState(layer.Name, layer.Visible, cells[i], generated[i])).ToList();
        }

        /// <summary>The first layer's cells -- all of them, on a map with one layer.</summary>
        public static List<KeyValuePair<GridMapCell, GridMapTile>> DecodeCells(ManaLevelGridMap encoded) =>
            DecodeLayers(encoded)[0].Cells.ToList();

        private static List<(int Layer, GridMapCell Cell, GridMapTile Tile, bool Generated)> DecodeRecords(ManaLevelGridMap encoded)
        {
            var data = Convert.FromBase64String(encoded.Data);
            var stride = encoded.Count > 0 && data.Length == encoded.Count * BytesPerUnlayeredCell ? BytesPerUnlayeredCell : BytesPerCell;
            var count = data.Length / stride;
            if (data.Length % stride != 0 || (encoded.Count != 0 && encoded.Count != count))
            {
                throw new InvalidDataException($"Grid map blob holds {data.Length} bytes, which is not the {encoded.Count} cells the level says.");
            }

            var records = new List<(int, GridMapCell, GridMapTile, bool)>(count);
            for (var i = 0; i < count; i++)
            {
                var offset = i * stride;
                var item = BitConverter.ToUInt16(data, offset + 6);
                if (item >= encoded.Palette.Count)
                {
                    throw new InvalidDataException($"Grid map cell {i} uses palette item {item}, but the palette holds {encoded.Palette.Count}.");
                }

                records.Add((
                    stride == BytesPerCell ? data[offset + 9] : 0,
                    new GridMapCell(
                        BitConverter.ToInt16(data, offset),
                        BitConverter.ToInt16(data, offset + 2),
                        BitConverter.ToInt16(data, offset + 4)),
                    new GridMapTile(item, data[offset + 8] & 0x7F),
                    (data[offset + 8] & GeneratedBit) != 0));
            }

            return records;
        }

        public static List<GridMapItemSettings> DecodeSettings(ManaLevelGridMap encoded) =>
            (encoded.Tiles ?? [])
                .Select(tile => new GridMapItemSettings
                {
                    Width = tile.Width,
                    Depth = tile.Depth,
                    Scale = tile.Scale,
                    Turn = tile.Turn,
                    Align = Enum.TryParse<GridMapAlign>(tile.Align, ignoreCase: true, out var align) ? align : GridMapAlign.Model,
                    Smooth = tile.Smooth,
                    Offset = tile.Offset is { Length: >= 3 } offset ? new Vector3(offset[0], offset[1], offset[2]) : Vector3.Zero
                })
                .ToList();

        public static Vector3 DecodeCellSize(ManaLevelGridMap encoded) =>
            encoded.CellSize is { Length: >= 3 } size ? new Vector3(size[0], size[1], size[2]) : Vector3.One;

        private static void WriteCoordinate(byte[] data, int offset, int coordinate)
        {
            if (coordinate is > short.MaxValue or < short.MinValue)
            {
                throw new InvalidDataException($"Grid map cell coordinate {coordinate} is outside the range a level file can store.");
            }

            BitConverter.TryWriteBytes(data.AsSpan(offset, 2), (short)coordinate);
        }
    }
}
