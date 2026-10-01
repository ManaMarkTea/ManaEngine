using System.Buffers.Binary;
using SixLabors.ImageSharp.Processing;

namespace ManaUI
{
    /// <summary>Where one colour glyph's PNG sits inside the CBDT table, and how it is positioned.</summary>
    public readonly struct ManaColorBitmapEntry
    {
        public ManaColorBitmapEntry(int offset, int length, int width, int height, int bearingX, int bearingY, int advance)
        {
            Offset = offset;
            Length = length;
            Width = width;
            Height = height;
            BearingX = bearingX;
            BearingY = bearingY;
            Advance = advance;
        }

        public int Offset { get; }
        public int Length { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>Pen origin to the bitmap's left edge, in the strike's pixels.</summary>
        public int BearingX { get; }

        /// <summary>Baseline to the bitmap's top edge, positive upwards -- the opposite sign to a UI Y axis.</summary>
        public int BearingY { get; }

        public int Advance { get; }
    }

    /// <summary>
    /// Reads embedded colour bitmaps out of a font's CBDT/CBLC tables -- the format Noto Color Emoji ships
    /// in, where every glyph is simply a PNG.
    /// <para>
    /// This exists because stb_truetype rasterises outlines and nothing else. It has no idea these tables
    /// are there, which is why an emoji font renders as blanks through it. The two are complementary
    /// rather than competing: stb still owns cmap lookup and metrics, and this only supplies pixels.
    /// </para>
    /// <para>
    /// Deliberately narrow. It handles index format 1 and image format 17, which is what Noto uses, and
    /// reports everything else as unsupported rather than half-implementing a spec this codebase has one
    /// consumer for. COLR/CPAL fonts -- Segoe UI Emoji, and Noto's own COLRv1 build -- are a different
    /// problem entirely: a paint graph with gradients and transforms, not a bitmap to copy.
    /// </para>
    /// </summary>
    public sealed class ManaColorBitmapTable : IManaColorGlyphSource
    {
        private readonly byte[] _data;
        private readonly int _cbdtOffset;
        private readonly Dictionary<int, ManaColorBitmapEntry> _entries = new();
        private readonly Dictionary<int, int> _characterMap = new();

        private ManaColorBitmapTable(byte[] data, int cbdtOffset, int pixelsPerEm)
        {
            _data = data;
            _cbdtOffset = cbdtOffset;
            PixelsPerEm = pixelsPerEm;
        }

        /// <summary>
        /// Codepoint to glyph index, read from the font's own cmap.
        /// <para>
        /// Parsed here rather than borrowed from stb, because stb cannot open one of these fonts at all: a
        /// CBDT emoji font has no glyf or loca table, so there are no outlines to initialise from and
        /// stbtt_InitFont refuses the file outright. Every glyph it has is a bitmap, which is exactly the
        /// data stb does not read.
        /// </para>
        /// </summary>
        public bool TryGetGlyphIndex(int codepoint, out int glyphIndex)
        {
            return _characterMap.TryGetValue(codepoint, out glyphIndex);
        }

        public int CharacterCount => _characterMap.Count;

        /// <summary>The strike's design size. Bitmaps are authored for this and scaled to whatever is asked.</summary>
        public int PixelsPerEm { get; }

        public int GlyphCount => _entries.Count;

        /// <summary>
        /// Reads the tables, or returns null when the font has none -- which is the common case and not an
        /// error. Every font in the UI is offered to this, and only emoji fonts answer.
        /// </summary>
        public static ManaColorBitmapTable? TryRead(byte[] fontData)
        {
            try
            {
                return TryReadCore(fontData);
            }
            catch (Exception)
            {
                // A malformed table means no colour glyphs, not a dead UI. The font still works for
                // outlines through stb.
                return null;
            }
        }

        private static ManaColorBitmapTable? TryReadCore(byte[] data)
        {
            if (data.Length < 12)
            {
                return null;
            }

            var span = data.AsSpan();

            // sfnt header: tag, table count, then 16-byte records of tag/checksum/offset/length. A
            // collection ('ttcf') would need an extra indirection and no emoji font ships as one.
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            var cblcOffset = 0;
            var cbdtOffset = 0;
            var cmapOffset = 0;

            for (var i = 0; i < tableCount; i++)
            {
                var record = 12 + i * 16;
                if (record + 16 > data.Length)
                {
                    return null;
                }

                var tag = span.Slice(record, 4);
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(record + 8)..]);

                if (tag.SequenceEqual("CBLC"u8))
                {
                    cblcOffset = offset;
                }
                else if (tag.SequenceEqual("CBDT"u8))
                {
                    cbdtOffset = offset;
                }
                else if (tag.SequenceEqual("cmap"u8))
                {
                    cmapOffset = offset;
                }
            }

            if (cblcOffset == 0 || cbdtOffset == 0 || cmapOffset == 0)
            {
                return null;
            }

            var strikeCount = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(cblcOffset + 4)..]);
            if (strikeCount <= 0)
            {
                return null;
            }

            // The largest strike, since everything is scaled down to the UI's size anyway and scaling down
            // is the direction that keeps detail.
            var bestStrike = -1;
            var bestPpem = 0;

            for (var i = 0; i < strikeCount; i++)
            {
                var strike = cblcOffset + 8 + i * BitmapSizeTableLength;
                if (strike + BitmapSizeTableLength > data.Length)
                {
                    break;
                }

                var ppem = span[strike + 44];
                var bitDepth = span[strike + 46];

                // 32 bits per pixel is the colour case; 1, 2, 4 and 8 are monochrome or greyscale strikes
                // that stb's own rasterisation covers better.
                if (bitDepth == 32 && ppem > bestPpem)
                {
                    bestPpem = ppem;
                    bestStrike = strike;
                }
            }

            if (bestStrike < 0)
            {
                return null;
            }

            var table = new ManaColorBitmapTable(data, cbdtOffset, bestPpem);
            table.ReadStrike(span, cblcOffset, bestStrike);
            table.ReadCharacterMap(span, cmapOffset);
            return table.GlyphCount > 0 && table.CharacterCount > 0 ? table : null;
        }

        /// <summary>indexSubTableArrayOffset, size, count, colorRef, two 12-byte metric blocks, then ranges.</summary>
        private const int BitmapSizeTableLength = 48;

        private void ReadStrike(ReadOnlySpan<byte> span, int cblcOffset, int strike)
        {
            var arrayOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(span[strike..]);
            var subTableCount = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(strike + 8)..]);

            for (var i = 0; i < subTableCount; i++)
            {
                var record = cblcOffset + arrayOffset + i * 8;
                if (record + 8 > span.Length)
                {
                    return;
                }

                var firstGlyph = BinaryPrimitives.ReadUInt16BigEndian(span[record..]);
                var lastGlyph = BinaryPrimitives.ReadUInt16BigEndian(span[(record + 2)..]);
                var additional = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(record + 4)..]);

                ReadSubTable(span, cblcOffset + arrayOffset + additional, firstGlyph, lastGlyph);
            }
        }

        private void ReadSubTable(ReadOnlySpan<byte> span, int header, int firstGlyph, int lastGlyph)
        {
            if (header + 8 > span.Length || lastGlyph < firstGlyph)
            {
                return;
            }

            var indexFormat = BinaryPrimitives.ReadUInt16BigEndian(span[header..]);
            var imageFormat = BinaryPrimitives.ReadUInt16BigEndian(span[(header + 2)..]);
            var imageDataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(header + 4)..]);

            // Format 17 is smallGlyphMetrics followed by a length-prefixed PNG. 18 and 19 exist but Noto
            // uses none of them, and guessing at a format with no font to test against is how silent
            // corruption gets shipped.
            if (indexFormat != 1 || imageFormat != 17)
            {
                return;
            }

            var count = lastGlyph - firstGlyph + 1;

            for (var i = 0; i < count; i++)
            {
                var offsetSlot = header + 8 + i * 4;
                if (offsetSlot + 8 > span.Length)
                {
                    return;
                }

                var start = (int)BinaryPrimitives.ReadUInt32BigEndian(span[offsetSlot..]);
                var end = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(offsetSlot + 4)..]);

                // Equal offsets mean the glyph has no bitmap in this strike, which is normal for the gaps
                // between the ranges a subtable covers.
                if (end <= start)
                {
                    continue;
                }

                var record = _cbdtOffset + imageDataOffset + start;
                if (record + 9 > span.Length)
                {
                    return;
                }

                // smallGlyphMetrics: height, width, bearingX, bearingY, advance -- the bearings signed.
                var height = span[record];
                var width = span[record + 1];
                var bearingX = (sbyte)span[record + 2];
                var bearingY = (sbyte)span[record + 3];
                var advance = span[record + 4];

                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(record + 5)..]);
                var pngOffset = record + 9;

                if (length <= 0 || pngOffset + length > span.Length)
                {
                    continue;
                }

                _entries[firstGlyph + i] = new ManaColorBitmapEntry(
                    pngOffset, length, width, height, bearingX, bearingY, advance);
            }
        }

        /// <summary>
        /// Reads the character map, preferring a format 12 subtable.
        /// <para>
        /// Format 4 only reaches U+FFFF, and every emoji worth having lives above it -- U+1F600 is the
        /// canonical one. So a font whose only subtable is format 4 is read anyway, but it will not answer
        /// for anything a user would call an emoji.
        /// </para>
        /// </summary>
        private void ReadCharacterMap(ReadOnlySpan<byte> span, int cmapOffset)
        {
            var subtableCount = BinaryPrimitives.ReadUInt16BigEndian(span[(cmapOffset + 2)..]);
            var best = 0;
            var bestScore = -1;

            for (var i = 0; i < subtableCount; i++)
            {
                var record = cmapOffset + 4 + i * 8;
                if (record + 8 > span.Length)
                {
                    break;
                }

                var platform = BinaryPrimitives.ReadUInt16BigEndian(span[record..]);
                var encoding = BinaryPrimitives.ReadUInt16BigEndian(span[(record + 2)..]);
                var offset = cmapOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(span[(record + 4)..]);

                if (offset + 2 > span.Length)
                {
                    continue;
                }

                var format = BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);

                // Full-Unicode subtables first, then anything else that parses.
                var score = format switch
                {
                    12 when platform == 3 && encoding == 10 => 100,
                    12 => 90,
                    4 when platform == 3 && encoding == 1 => 50,
                    4 => 40,
                    _ => -1
                };

                if (score > bestScore)
                {
                    bestScore = score;
                    best = offset;
                }
            }

            if (bestScore < 0)
            {
                return;
            }

            var chosen = BinaryPrimitives.ReadUInt16BigEndian(span[best..]);
            if (chosen == 12)
            {
                ReadFormat12(span, best);
            }
            else
            {
                ReadFormat4(span, best);
            }
        }

        private void ReadFormat12(ReadOnlySpan<byte> span, int offset)
        {
            var groupCount = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 12)..]);

            for (var i = 0; i < groupCount; i++)
            {
                var group = offset + 16 + i * 12;
                if (group + 12 > span.Length)
                {
                    return;
                }

                var start = (int)BinaryPrimitives.ReadUInt32BigEndian(span[group..]);
                var end = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(group + 4)..]);
                var startGlyph = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(group + 8)..]);

                // Ranges are contiguous in both codepoint and glyph id, so this is a straight walk. Capped
                // because a malformed group could otherwise claim the whole codespace.
                var count = Math.Min(end - start + 1, 0x10000);
                for (var c = 0; c < count; c++)
                {
                    _characterMap[start + c] = startGlyph + c;
                }
            }
        }

        private void ReadFormat4(ReadOnlySpan<byte> span, int offset)
        {
            var segCount = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 6)..]) / 2;
            var endCodes = offset + 14;
            var startCodes = endCodes + segCount * 2 + 2;
            var deltas = startCodes + segCount * 2;
            var rangeOffsets = deltas + segCount * 2;

            for (var segment = 0; segment < segCount; segment++)
            {
                var end = BinaryPrimitives.ReadUInt16BigEndian(span[(endCodes + segment * 2)..]);
                var start = BinaryPrimitives.ReadUInt16BigEndian(span[(startCodes + segment * 2)..]);
                var delta = BinaryPrimitives.ReadInt16BigEndian(span[(deltas + segment * 2)..]);
                var rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(span[(rangeOffsets + segment * 2)..]);

                if (start > end || start == 0xFFFF)
                {
                    continue;
                }

                for (var code = start; code <= end; code++)
                {
                    int glyph;

                    if (rangeOffset == 0)
                    {
                        glyph = (code + delta) & 0xFFFF;
                    }
                    else
                    {
                        // The spec's pointer arithmetic: the offset is relative to its own slot.
                        var slot = rangeOffsets + segment * 2 + rangeOffset + (code - start) * 2;
                        if (slot + 2 > span.Length)
                        {
                            continue;
                        }

                        glyph = BinaryPrimitives.ReadUInt16BigEndian(span[slot..]);
                        if (glyph != 0)
                        {
                            glyph = (glyph + delta) & 0xFFFF;
                        }
                    }

                    if (glyph != 0)
                    {
                        _characterMap[code] = glyph;
                    }
                }
            }
        }

        public bool Contains(int glyphIndex)
        {
            return _entries.ContainsKey(glyphIndex);
        }

        public bool TryGetEntry(int glyphIndex, out ManaColorBitmapEntry entry)
        {
            return _entries.TryGetValue(glyphIndex, out entry);
        }

        /// <summary>
        /// Decodes the glyph's PNG and scales it to <paramref name="pixelsPerEm"/>. Scaled on the way in: the
        /// strike is 109 pixels and a UI asks for fifteen, so stored native most of the atlas would be wasted.
        /// </summary>
        public ManaColorGlyphImage? Draw(int codepoint, float pixelsPerEm)
        {
            if (!TryGetGlyphIndex(codepoint, out var index) || !TryGetEntry(index, out var entry))
            {
                return null;
            }

            var scale = pixelsPerEm / MathF.Max(1.0f, PixelsPerEm);
            var width = Math.Max(1, (int)MathF.Round(entry.Width * scale));
            var height = Math.Max(1, (int)MathF.Round(entry.Height * scale));

            try
            {
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(GetPng(entry));
                image.Mutate(context => context.Resize(width, height));
                var pixels = new byte[width * height * 4];
                image.CopyPixelDataTo(pixels);
                return new ManaColorGlyphImage(pixels, width, height, entry.BearingX * scale, entry.BearingY * scale, entry.Advance * scale);
            }
            catch (Exception)
            {
                // A PNG this decoder cannot read is one emoji missing, not a broken font.
                return null;
            }
        }

        /// <summary>The raw PNG bytes for a glyph, as a view into the font data rather than a copy.</summary>
        public ReadOnlySpan<byte> GetPng(ManaColorBitmapEntry entry)
        {
            return _data.AsSpan(entry.Offset, entry.Length);
        }
    }
}
