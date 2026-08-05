using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ManaGeoTools.Code.GeoTiff
{
    // Hand-rolled reader for TIFF/GeoTIFF files. Started as a direct port of GeoTiffReader.gd's
    // uncompressed-strip-only parsing approach (still the path used for e.g. USGS SRTM tiles), and
    // was extended with a tiled/Deflate/float32/floating-point-predictor path (see ReadTiledImage)
    // to read real-world modern GeoTIFFs like NOAA's ETOPO 2022 global relief tiles, which the
    // original Godot reader can't parse either.
    //
    // Mirrors the source's file-handle lifetime: it opens the file only for the duration of
    // ReadMetaData() and again for ReadImage(), rather than holding a handle open for the whole
    // lifetime of a GeoTiff — the source explicitly reopens by path before reading strips
    // (GeoTiffReader.gd's ReadImage does `CurrentFile = FileAccess.open(CurrentFile.get_path(), ...)`)
    // so that scanning a folder of many .tif files via GeoTiffLibrary doesn't hold hundreds of file
    // handles open at once.
    public sealed class GeoTiffReader
    {
        private readonly string _filePath;
        private bool _bigEndian;

        public GeoTiffReader(string filePath)
        {
            _filePath = filePath;
        }

        public GeoTiffMetadata? ReadMetaData()
        {
            using var stream = File.OpenRead(_filePath);

            var header = new byte[2];
            if (stream.Read(header, 0, 2) != 2)
            {
                return null;
            }
            var byteOrder = Encoding.ASCII.GetString(header);

            if (byteOrder == "II")
            {
                _bigEndian = false;
            }
            else if (byteOrder == "MM")
            {
                _bigEndian = true;
            }
            else
            {
                return null;
            }

            var magicNumber = ReadUInt16(stream, _bigEndian);
            if (magicNumber != 42)
            {
                return null;
            }

            var ifdOffset = ReadUInt32(stream, _bigEndian);
            stream.Seek(ifdOffset, SeekOrigin.Begin);

            var numEntries = ReadUInt16(stream, _bigEndian);
            var metadata = new GeoTiffMetadata();

            for (var i = 0; i < numEntries; i++)
            {
                var tagId = ReadUInt16(stream, _bigEndian);
                var fieldType = ReadUInt16(stream, _bigEndian);
                var numValues = (int)ReadUInt32(stream, _bigEndian);
                var valueOffset = ReadUInt32(stream, _bigEndian);

                if (!GeoTiffTag.Tags.TryGetValue(tagId, out var tag))
                {
                    Console.WriteLine($"[GeoTiff] {tagId} Missing {valueOffset}");
                    continue;
                }

                metadata.ReadTagValue(stream, _bigEndian, tag, fieldType, numValues, valueOffset);
            }

            metadata.TryComputeAreaFromModelTiepoint();

            return metadata;
        }

        public float[] ReadImage(GeoTiffMetadata metadata)
        {
            using var stream = File.OpenRead(_filePath);

            if (metadata.IsTiled)
            {
                return ReadTiledImage(stream, metadata);
            }

            var offsets = metadata.StripOffsets;
            var sizes = metadata.StripByteCounts;
            if (offsets.Length != sizes.Length)
            {
                throw new InvalidOperationException("StripOffsets and StripByteCounts length mismatch.");
            }

            var data = new float[metadata.Width * metadata.Height];

            var dataIndex = 0;
            for (var index = 0; index < sizes.Length; index++)
            {
                var offset = offsets[index];
                var size = sizes[index];
                var imageStrip = ReadShortArrayAsFloats(stream, _bigEndian, offset, size / 2);
                foreach (var pixel in imageStrip)
                {
                    data[dataIndex] = pixel;
                    dataIndex++;
                }
            }

            var dataExpected = metadata.Width * metadata.Height;
            if (dataIndex != dataExpected)
            {
                throw new InvalidOperationException($"Expected {dataExpected} height samples, found {dataIndex}.");
            }

            return data;
        }

        // Reads a tiled GeoTIFF (TileWidth/TileLength/TileOffsets/TileByteCounts), e.g. ETOPO 2022's
        // per-tile downloads: 256x256 internal tiles, Deflate-compressed, 32-bit float samples with
        // a floating-point predictor. Edge tiles are always stored as a full TileWidth x TileLength
        // block even when the image doesn't fill it — those extra pixels are clipped on copy.
        private float[] ReadTiledImage(Stream stream, GeoTiffMetadata metadata)
        {
            if (metadata.TileOffsets.Length != metadata.TileByteCounts.Length)
            {
                throw new InvalidOperationException("TileOffsets and TileByteCounts length mismatch.");
            }

            var bytesPerSample = metadata.BitsPerSample / 8;
            if (bytesPerSample != 4 && bytesPerSample != 2)
            {
                throw new NotSupportedException($"Unsupported BitsPerSample={metadata.BitsPerSample} for a tiled GeoTIFF.");
            }

            var tilesAcross = (metadata.Width + metadata.TileWidth - 1) / metadata.TileWidth;
            var tilesDown = (metadata.Height + metadata.TileLength - 1) / metadata.TileLength;
            var expectedTileCount = tilesAcross * tilesDown;
            if (metadata.TileOffsets.Length != expectedTileCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedTileCount} tiles ({tilesAcross}x{tilesDown}), found {metadata.TileOffsets.Length}.");
            }

            var output = new float[metadata.Width * metadata.Height];
            var tilePixelCount = metadata.TileWidth * metadata.TileLength;

            for (var tileIndex = 0; tileIndex < metadata.TileOffsets.Length; tileIndex++)
            {
                var tileX = tileIndex % tilesAcross;
                var tileY = tileIndex / tilesAcross;

                stream.Seek(metadata.TileOffsets[tileIndex], SeekOrigin.Begin);
                var compressed = new byte[metadata.TileByteCounts[tileIndex]];
                ReadExact(stream, compressed);

                var decompressed = Decompress(compressed, metadata.Compression, tilePixelCount * bytesPerSample);

                // The floating point predictor's byte-plane shuffle is always defined MSB-first
                // (big-endian byte significance) regardless of the file's own declared byte order —
                // a well-documented TIFF predictor gotcha — so bytes reassembled by
                // ReverseFloatingPointPredictor must be read back as big-endian even for a
                // little-endian ("II") file.
                var predictorApplied = false;
                if (metadata.Predictor == 3)
                {
                    ReverseFloatingPointPredictor(decompressed, metadata.TileWidth, metadata.TileLength, bytesPerSample);
                    predictorApplied = true;
                }
                else if (metadata.Predictor != 1)
                {
                    throw new NotSupportedException($"Unsupported Predictor={metadata.Predictor} for a tiled GeoTIFF.");
                }

                var sampleBigEndian = predictorApplied ? true : _bigEndian;
                var tileFloats = bytesPerSample == 4
                    ? BytesToFloats(decompressed, tilePixelCount, sampleBigEndian)
                    : BytesToUInt16Floats(decompressed, tilePixelCount, _bigEndian);

                var originX = tileX * metadata.TileWidth;
                var originY = tileY * metadata.TileLength;
                var copyWidth = Math.Min(metadata.TileWidth, metadata.Width - originX);
                var copyHeight = Math.Min(metadata.TileLength, metadata.Height - originY);

                for (var y = 0; y < copyHeight; y++)
                {
                    var srcRowStart = y * metadata.TileWidth;
                    var dstRowStart = ((originY + y) * metadata.Width) + originX;
                    Array.Copy(tileFloats, srcRowStart, output, dstRowStart, copyWidth);
                }
            }

            return output;
        }

        private static byte[] Decompress(byte[] compressed, int compression, int expectedByteCount)
        {
            switch (compression)
            {
                case 1:
                    return compressed;
                case 8:
                    using (var input = new MemoryStream(compressed))
                    using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream(expectedByteCount))
                    {
                        zlib.CopyTo(output);
                        var result = output.ToArray();
                        if (result.Length != expectedByteCount)
                        {
                            throw new InvalidOperationException(
                                $"Decompressed tile size {result.Length} does not match expected {expectedByteCount}.");
                        }
                        return result;
                    }
                default:
                    throw new NotSupportedException($"Unsupported TIFF Compression={compression}.");
            }
        }

        // TIFF Technical Note 3 "floating point predictor" (Predictor=3): the encoder replaces each
        // scanline's raw float bytes with (1) a horizontal byte-difference, then (2) a byte-plane
        // shuffle (all pixels' byte 0 first, then all byte 1, ...). Decoding reverses both steps, in
        // the opposite order of encoding: undo the difference first, then un-shuffle the planes.
        private static void ReverseFloatingPointPredictor(byte[] data, int width, int height, int bytesPerSample)
        {
            var rowStride = width * bytesPerSample;
            var planar = new byte[rowStride];

            for (var row = 0; row < height; row++)
            {
                var rowStart = row * rowStride;

                for (var i = 1; i < rowStride; i++)
                {
                    data[rowStart + i] = (byte)(data[rowStart + i] + data[rowStart + i - 1]);
                }

                Array.Copy(data, rowStart, planar, 0, rowStride);
                for (var pixel = 0; pixel < width; pixel++)
                {
                    for (var b = 0; b < bytesPerSample; b++)
                    {
                        data[rowStart + (pixel * bytesPerSample) + b] = planar[(b * width) + pixel];
                    }
                }
            }
        }

        private static float[] BytesToFloats(byte[] data, int count, bool bigEndian)
        {
            var output = new float[count];
            for (var i = 0; i < count; i++)
            {
                var span = data.AsSpan(i * 4, 4);
                output[i] = bigEndian ? BinaryPrimitives.ReadSingleBigEndian(span) : BinaryPrimitives.ReadSingleLittleEndian(span);
            }
            return output;
        }

        private static float[] BytesToUInt16Floats(byte[] data, int count, bool bigEndian)
        {
            var output = new float[count];
            for (var i = 0; i < count; i++)
            {
                var span = data.AsSpan(i * 2, 2);
                output[i] = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
            }
            return output;
        }

        internal static ushort ReadUInt16(Stream stream, bool bigEndian)
        {
            Span<byte> buffer = stackalloc byte[2];
            ReadExact(stream, buffer);
            return bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(buffer) : BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        }

        internal static uint ReadUInt32(Stream stream, bool bigEndian)
        {
            Span<byte> buffer = stackalloc byte[4];
            ReadExact(stream, buffer);
            return bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(buffer) : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        internal static double ReadDouble(Stream stream, bool bigEndian)
        {
            Span<byte> buffer = stackalloc byte[8];
            ReadExact(stream, buffer);
            return bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(buffer) : BinaryPrimitives.ReadDoubleLittleEndian(buffer);
        }

        internal static float ReadFloat(Stream stream, bool bigEndian)
        {
            Span<byte> buffer = stackalloc byte[4];
            ReadExact(stream, buffer);
            return bigEndian ? BinaryPrimitives.ReadSingleBigEndian(buffer) : BinaryPrimitives.ReadSingleLittleEndian(buffer);
        }

        internal static string ReadAsciiString(Stream stream, long offset, int length)
        {
            var currentPos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[length];
            ReadExact(stream, buffer);
            stream.Position = currentPos;

            // ASCII fields are NUL-terminated; trim trailing NULs like Godot's get_string_from_ascii.
            var text = Encoding.ASCII.GetString(buffer);
            var nulIndex = text.IndexOf('\0');
            return nulIndex >= 0 ? text.Substring(0, nulIndex) : text;
        }

        internal static int[] ReadIntArray(Stream stream, bool bigEndian, long offset, int numValues)
        {
            var currentPos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            var output = new int[numValues];
            for (var i = 0; i < numValues; i++)
            {
                output[i] = (int)ReadUInt32(stream, bigEndian);
            }
            stream.Position = currentPos;
            return output;
        }

        internal static ushort[] ReadShortArray(Stream stream, bool bigEndian, long offset, int numValues)
        {
            var currentPos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            var output = new ushort[numValues];
            for (var i = 0; i < numValues; i++)
            {
                output[i] = ReadUInt16(stream, bigEndian);
            }
            stream.Position = currentPos;
            return output;
        }

        internal static float[] ReadShortArrayAsFloats(Stream stream, bool bigEndian, long offset, int numValues)
        {
            var currentPos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            var output = new float[numValues];
            for (var i = 0; i < numValues; i++)
            {
                output[i] = ReadUInt16(stream, bigEndian);
            }
            stream.Position = currentPos;
            return output;
        }

        internal static double[] ReadDoubleArray(Stream stream, bool bigEndian, long offset, int numValues)
        {
            var currentPos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            var output = new double[numValues];
            for (var i = 0; i < numValues; i++)
            {
                output[i] = ReadDouble(stream, bigEndian);
            }
            stream.Position = currentPos;
            return output;
        }

        internal static float[] ReadFloatArray(Stream stream, bool bigEndian, long offset, int numValues)
        {
            var currentPos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            var output = new float[numValues];
            for (var i = 0; i < numValues; i++)
            {
                output[i] = ReadFloat(stream, bigEndian);
            }
            stream.Position = currentPos;
            return output;
        }

        private static void ReadExact(Stream stream, Span<byte> buffer)
        {
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read != buffer.Length)
            {
                throw new EndOfStreamException("Unexpected end of GeoTIFF stream.");
            }
        }
    }
}
