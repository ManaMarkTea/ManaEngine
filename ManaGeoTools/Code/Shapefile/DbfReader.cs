using System.Buffers.Binary;
using System.Text;

namespace ManaGeoTools.Code.Shapefile
{
    /// <summary>
    /// Minimal dBase III (.dbf) reader -- just enough to pull one named field's value out of every
    /// record, in file order (which matches the paired .shp file's record order, standard Shapefile
    /// convention). Used here only to attach a country name to logged/debug output as boundary rings
    /// load; not required for rendering the borders themselves.
    /// </summary>
    public static class DbfReader
    {
        public static List<string> ReadFieldValues(Stream dbfStream, string fieldName)
        {
            var header = new byte[32];
            ReadExact(dbfStream, header);

            var recordCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            var headerLength = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(8, 2));
            var recordLength = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(10, 2));

            // Field descriptors run from byte 32 up to a 0x0D terminator, right before headerLength.
            var descriptorBytes = new byte[headerLength - 32 - 1];
            ReadExact(dbfStream, descriptorBytes);
            ReadExact(dbfStream, new byte[1]); // the 0x0D terminator itself

            int? targetOffset = null;
            var targetLength = 0;
            var offset = 1; // every record starts with a 1-byte deletion flag before its fields
            for (var i = 0; i + 32 <= descriptorBytes.Length; i += 32)
            {
                var nameBytes = descriptorBytes.AsSpan(i, 11);
                var nameEnd = nameBytes.IndexOf((byte)0);
                var name = Encoding.ASCII.GetString(nameEnd >= 0 ? nameBytes.Slice(0, nameEnd) : nameBytes);
                var length = descriptorBytes[i + 16];

                if (targetOffset == null && string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    targetOffset = offset;
                    targetLength = length;
                }

                offset += length;
            }

            if (targetOffset == null)
            {
                throw new InvalidOperationException($"DBF file has no field named '{fieldName}'.");
            }

            var values = new List<string>(recordCount);
            var recordBuffer = new byte[recordLength];
            for (var r = 0; r < recordCount; r++)
            {
                ReadExact(dbfStream, recordBuffer);
                // This dataset pads character fields with null bytes, not the ASCII spaces the DBF
                // convention normally implies (verified against the actual file) -- '\0' isn't
                // whitespace as far as string.Trim() is concerned, so it's stripped explicitly here.
                // Trimming both characters in one call (rather than chaining two Trim calls) handles
                // either padding character showing up on either side of the other.
                values.Add(Encoding.UTF8.GetString(recordBuffer, targetOffset.Value, targetLength).Trim(' ', '\0'));
            }

            return values;
        }

        private static void ReadExact(Stream stream, byte[] buffer)
        {
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    throw new InvalidOperationException("Truncated .dbf file.");
                }
                totalRead += read;
            }
        }
    }
}
