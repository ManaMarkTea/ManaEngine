namespace Mana.Code.Content
{
    /// <summary>
    /// A 9-slice, set up once: the nine pictures a block of any size is built from -- four corners, four edges and
    /// the middle -- as Kenney's terrain tiles come. Saved as a .manaslice in Content/; a Sprite 9-Slice component
    /// lays one out across whatever size it is given. Any piece can be left empty: it falls back to the middle of
    /// its row, then to the centre, so a one-row platform needs only its top three.
    /// </summary>
    public sealed class ManaSpriteSlice
    {
        public const string Extension = ".manaslice";

        public string Format { get; set; } = "mana-sprite-slice";
        public int Version { get; set; } = 1;
        public string Id { get; set; } = ManaContentAssets.NewId();
        public string Name { get; set; } = "9-Slice";

        /// <summary>Pixels per metre: 64 makes Kenney's 64-pixel tiles one metre each.</summary>
        public float PixelsPerUnit { get; set; } = 64.0f;

        public string TopLeft { get; set; } = string.Empty;
        public string Top { get; set; } = string.Empty;
        public string TopRight { get; set; } = string.Empty;
        public string Left { get; set; } = string.Empty;
        public string Center { get; set; } = string.Empty;
        public string Right { get; set; } = string.Empty;
        public string BottomLeft { get; set; } = string.Empty;
        public string Bottom { get; set; } = string.Empty;
        public string BottomRight { get; set; } = string.Empty;

        /// <summary>The picture for a cell, by which edge it is on: -1 the first row or column, 1 the last, 0 between.</summary>
        public string Piece(int row, int column)
        {
            var pieces = row switch
            {
                < 0 => (TopLeft, Top, TopRight),
                > 0 => (BottomLeft, Bottom, BottomRight),
                _ => (Left, Center, Right)
            };
            var own = column switch { < 0 => pieces.Item1, > 0 => pieces.Item3, _ => pieces.Item2 };
            return First(own, pieces.Item2, column < 0 ? Left : column > 0 ? Right : Center, Center, Top);
        }

        /// <summary>The grid of pictures for a block of <paramref name="columns"/> x <paramref name="rows"/> tiles, top row first.</summary>
        public List<List<string>> Layout(int columns, int rows)
        {
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);
            var grid = new List<List<string>>();
            for (var row = 0; row < rows; row++)
            {
                // A block one tile high is all top row; one tile wide, all its left column -- the edge that shows.
                var rowEdge = row == 0 ? -1 : row == rows - 1 ? 1 : 0;
                var cells = new List<string>();
                for (var column = 0; column < columns; column++)
                {
                    var columnEdge = columns == 1 ? 0 : column == 0 ? -1 : column == columns - 1 ? 1 : 0;
                    cells.Add(Piece(rowEdge, columnEdge));
                }
                grid.Add(cells);
            }
            return grid;
        }

        /// <summary>Any piece at all, to measure a tile by.</summary>
        public string AnyPiece => First(Center, Top, Left, TopLeft, Right, Bottom, TopRight, BottomLeft, BottomRight);

        private static string First(params string[] pictures) => pictures.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty;
    }
}
