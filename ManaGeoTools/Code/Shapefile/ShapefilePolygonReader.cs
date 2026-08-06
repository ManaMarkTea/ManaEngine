using System.Buffers.Binary;

namespace ManaGeoTools.Code.Shapefile
{
    /// <summary>
    /// Minimal binary reader for the Esri Shapefile .shp format, Polygon (shape type 5) records only
    /// -- the one type Natural Earth's admin-0 country boundary datasets use (verified against the
    /// actual downloaded files before writing this). Reads directly from a Stream so callers can read
    /// straight out of a ZipArchiveEntry without extracting to disk first -- which is also why this
    /// never seeks or reads Stream.Position: a ZipArchiveEntry's decompression stream is forward-only
    /// and throws NotSupportedException on both (caught by an actual run against a real .zip, not
    /// assumed). Each record's declared content length is read into one buffer and parsed from there
    /// with span slicing instead.
    ///
    /// Format reference: https://en.wikipedia.org/wiki/Shapefile -- big-endian file header fields,
    /// little-endian everything else within each record, same "mixed endianness, read explicitly"
    /// shape GeoTiffReader.cs already deals with for TIFF.
    /// </summary>
    public static class ShapefilePolygonReader
    {
        private const int FileCode = 9994;
        private const int PolygonShapeType = 5;

        /// <summary>
        /// Reads every ring (part) of every Polygon record in the stream. A "ring" is a closed loop of
        /// (longitude, latitude) points in file order -- no attempt is made here to distinguish outer
        /// boundaries from holes, since for line rendering every ring is just another border to draw.
        /// </summary>
        public static IEnumerable<List<(double Longitude, double Latitude)>> ReadRings(Stream shpStream)
        {
            // Heap-allocated, not stackalloc -- this is an iterator method (uses yield return), and
            // stackalloc'd memory can't safely survive across a yield boundary.
            var header = new byte[100];
            ReadExact(shpStream, header);

            var fileCode = BinaryPrimitives.ReadInt32BigEndian(header);
            if (fileCode != FileCode)
            {
                throw new InvalidOperationException($"Not a valid .shp file (expected file code {FileCode}, got {fileCode}).");
            }

            var shapeType = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32, 4));
            if (shapeType != PolygonShapeType)
            {
                throw new NotSupportedException($"Only Polygon (shape type {PolygonShapeType}) shapefiles are supported, this file is shape type {shapeType}.");
            }

            var recordHeader = new byte[8];

            while (true)
            {
                // Streams (e.g. a ZipArchiveEntry's DeflateStream) aren't guaranteed to fill a buffer
                // in one Read() call, so ReadExactly-style looping is used throughout this reader
                // rather than a bare Read() -- a single short read would otherwise look identical to a
                // real end-of-file here, and elsewhere would look like silent truncation.
                if (!TryReadExactly(shpStream, recordHeader))
                {
                    yield break;
                }

                // Content length is in 16-bit words, big-endian; excludes the record header itself.
                var contentLengthBytes = BinaryPrimitives.ReadInt32BigEndian(recordHeader.AsSpan(4, 4)) * 2;
                var content = new byte[contentLengthBytes];
                ReadExact(shpStream, content);

                var recordShapeType = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(0, 4));
                if (recordShapeType != PolygonShapeType)
                {
                    // Null-shape records (type 0, content length 4 bytes -- just the shape type field,
                    // nothing else) are legal filler; the whole record is already consumed above
                    // regardless of its actual shape type, so there's nothing left to skip.
                    continue;
                }

                var numParts = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(36, 4));
                var numPoints = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(40, 4));

                const int partsOffset = 44; // shape type(4) + box(32) + numParts(4) + numPoints(4)
                var partStarts = new int[numParts];
                for (var i = 0; i < numParts; i++)
                {
                    partStarts[i] = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(partsOffset + (i * 4), 4));
                }

                var pointsOffset = partsOffset + (numParts * 4);
                for (var part = 0; part < numParts; part++)
                {
                    var start = partStarts[part];
                    var end = part + 1 < numParts ? partStarts[part + 1] : numPoints;
                    var ring = new List<(double, double)>(Math.Max(0, end - start));
                    for (var p = start; p < end; p++)
                    {
                        var pointOffset = pointsOffset + (p * 16);
                        var x = BinaryPrimitives.ReadDoubleLittleEndian(content.AsSpan(pointOffset, 8));
                        var y = BinaryPrimitives.ReadDoubleLittleEndian(content.AsSpan(pointOffset + 8, 8));
                        ring.Add((x, y));
                    }
                    if (ring.Count > 0)
                    {
                        yield return ring;
                    }
                }
            }
        }

        private static void ReadExact(Stream stream, byte[] buffer)
        {
            if (!TryReadExactly(stream, buffer))
            {
                throw new InvalidOperationException("Truncated .shp file.");
            }
        }

        // Returns false only on a clean end-of-stream before any bytes of this read were consumed.
        // Throws if the stream ends partway through -- that's real truncation/corruption, not EOF.
        private static bool TryReadExactly(Stream stream, byte[] buffer)
        {
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    if (totalRead == 0)
                    {
                        return false;
                    }
                    throw new InvalidOperationException("Truncated .shp file.");
                }
                totalRead += read;
            }
            return true;
        }
    }
}
