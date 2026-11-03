using ManaCore.Imaging;
using Mana.Code.Components;
using ManaPixelFx.Core;

namespace Mana.Code.Content
{
    /// <summary>
    /// Bakes a live pixel effect into a sprite sheet: one run with the instance's own options, cropped to what it
    /// draws, its picture and its light as two sheets (the GPU blends one and adds the other), and a .manasheet that
    /// plays them at the effect's rate. For a game that wants a picture rather than the effect's code at runtime.
    /// </summary>
    public static class ManaPixelFxBaker
    {
        /// <summary>A sheet's largest side; past it frames are dropped, down to 15 fps, before it may grow to 4096.</summary>
        public const int MaxSide = 2048;

        /// <summary>
        /// Writes <c>&lt;name&gt;.png</c>, <c>&lt;name&gt;.light.png</c> and <c>&lt;name&gt;.manasheet</c> into
        /// <paramref name="directory"/>, keeping the sheet's id if it was baked before, and returns the sheet's file.
        /// </summary>
        public static string Bake(ManaPixelFxComponent fx, string directory, string name)
        {
            var effect = fx.Definition ?? throw new InvalidOperationException($"No pixel effect '{fx.Effect}' to bake.");
            var meta = effect.Meta;
            var options = fx.EffectOptions();
            var (w, h) = meta.Size;
            var frames = new List<(byte[] Color, byte[] Light)>();
            var drawn = PixelBounds.Empty;
            PixelFxRun.Play(effect, options, meta.Fps, (_, frame) =>
            {
                for (var i = 0; i < w * h; i++)
                {
                    if (frame.Color[i] >> 24 != 0 || (frame.Light[i] & 0xffffffu) != 0)
                    {
                        drawn = drawn.Include(i % w, i / w);
                    }
                }
                frames.Add((Bytes(frame.Color), Bytes(frame.Light)));
            });
            if (drawn.IsEmpty)
            {
                drawn = new PixelBounds(0, 0, 0, 0);
            }

            var (step, columns, rows) = Layout(frames.Count, drawn.Width, drawn.Height, meta.Fps);
            var kept = frames.Where((_, i) => i % step == 0).ToList();
            var color = new ManaImage(columns * drawn.Width, rows * drawn.Height);
            var light = new ManaImage(columns * drawn.Width, rows * drawn.Height);
            for (var k = 0; k < kept.Count; k++)
            {
                var cellX = (k % columns) * drawn.Width;
                var cellY = (k / columns) * drawn.Height;
                for (var y = 0; y < drawn.Height; y++)
                {
                    var from = ((drawn.Y0 + y) * w + drawn.X0) * 4;
                    var to = ((cellY + y) * color.Width + cellX) * 4;
                    kept[k].Color.AsSpan(from, drawn.Width * 4).CopyTo(color.Pixels.AsSpan(to));
                    kept[k].Light.AsSpan(from, drawn.Width * 4).CopyTo(light.Pixels.AsSpan(to));
                }
            }

            Directory.CreateDirectory(directory);
            var picture = Path.Combine(directory, name + ".png");
            var lightPicture = Path.Combine(directory, name + ".light.png");
            var file = Path.Combine(directory, name + ManaSpriteSheet.Extension);
            ManaImageCodec.SavePng(color, picture);
            ManaImageCodec.SavePng(light, lightPicture);

            // The node stands where the live one did: its anchor, measured from the crop's bottom left.
            var (ax, ay) = fx.AnchorPixel();
            var repo = Path.GetDirectoryName(ManaContentAssets.Root) ?? Directory.GetCurrentDirectory();
            var sheet = new ManaSpriteSheet
            {
                Id = ManaContentAssets.ReadId(file) ?? ManaContentAssets.NewId(),
                Name = name,
                Source = ManaSpriteSheet.PixelFxSource + meta.Id,
                Texture = RepoPath(repo, picture),
                Light = RepoPath(repo, lightPicture),
                Columns = columns,
                Rows = rows,
                PixelsPerUnit = fx.PixelsPerUnit,
                Pivot = [(float)((ax - drawn.X0) / drawn.Width), (float)((drawn.Y1 + 1 - ay) / drawn.Height)],
                Animations =
                [
                    new ManaSpriteAnimation
                    {
                        Name = "play",
                        Frames = Enumerable.Range(0, kept.Count).ToList(),
                        FramesPerSecond = meta.Fps / (float)step,
                        Loop = meta.Loop,
                    },
                ],
            };
            ManaContentAssets.Save(sheet, file);
            return file;
        }

        /// <summary>A sprite sheet component playing a baked sheet as the live effect looked: its flip and tint.</summary>
        public static ManaSpriteSheetComponent SheetFor(ManaPixelFxComponent fx, string sheetFile) => new(fx.Owner)
        {
            Sheet = ManaContentAssets.Reference(sheetFile),
            Animation = "play",
            PixelArt = true,
            Unlit = true,
            FlipX = fx.FlipX,
            Tint = fx.Tint,
        };

        /// <summary>Puts <paramref name="to"/> on the node in <paramref name="from"/>'s place: a baked sheet for its live effect, or back on undo.</summary>
        public static void Swap(ManaComponent from, ManaComponent to)
        {
            var node = from.Owner;
            lock (node.Context.World.SceneLock)
            {
                var index = node.Components.IndexOf(from);
                if (index < 0)
                {
                    return;
                }
                node.Components.RemoveAt(index);
                from.OnRemoved();
                node.Components.Insert(Math.Min(index, node.Components.Count), to);
            }
        }

        /// <summary>A file name in <paramref name="directory"/> no sheet has yet: "Torch", then "Torch 2".</summary>
        public static string FreeName(string directory, string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(c =>
            {
                if (invalid.Contains(c))
                {
                    return '_';
                }
                return c;
            }).ToArray()).Trim();
            if (clean.Length == 0)
            {
                clean = "Pixel FX";
            }
            var candidate = clean;
            for (var n = 2; File.Exists(Path.Combine(directory, candidate + ManaSpriteSheet.Extension)); n++)
            {
                candidate = $"{clean} {n}";
            }
            return candidate;
        }

        /// <summary>Every frame-th kept, and the grid they go in: as square as fits, frames dropped only to fit.</summary>
        public static (int Step, int Columns, int Rows) Layout(int frames, int w, int h, int fps)
        {
            (int Columns, int Rows)? Fit(int n, int max)
            {
                (int Columns, int Rows)? best = null;
                var bestSide = int.MaxValue;
                for (var columns = 1; columns <= n; columns++)
                {
                    var rows = (n + columns - 1) / columns;
                    var side = Math.Max(columns * w, rows * h);
                    if (columns * w <= max && rows * h <= max && side < bestSide)
                    {
                        best = (columns, rows);
                        bestSide = side;
                    }
                }
                return best;
            }
            int Count(int step) => (frames + step - 1) / step;

            // Below 15 fps an effect reads as choppy, so it takes a bigger sheet first.
            var step = 1;
            var grid = Fit(frames, MaxSide);
            while (grid == null && fps / (step * 2.0) >= 15)
            {
                step *= 2;
                grid = Fit(Count(step), MaxSide);
            }
            grid ??= Fit(Count(step), 4096);
            while (grid == null && step < 8)
            {
                step *= 2;
                grid = Fit(Count(step), 4096);
            }
            var (c, r) = grid ?? throw new InvalidOperationException($"{w}x{h} frames do not fit a 4096 px sheet.");
            return (step, c, r);
        }

        private static byte[] Bytes(uint[] buffer) => System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan()).ToArray();

        private static string RepoPath(string repo, string file) => Path.GetRelativePath(repo, Path.GetFullPath(file)).Replace('\\', '/');
    }
}
