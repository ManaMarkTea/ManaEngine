namespace ManaGeoTools.Code.GeoTiff
{
    public sealed class GeoTiff : IHeightTile
    {
        public enum GeoTiffCompression
        {
            Unknown = 0,
            None = 1,
            ModifiedHuffman = 2,
            Ccitt2D = 3,
            CcittFax = 4,
            LempelZivWelch = 5,
            Jpeg = 6,
            JpegNew = 7,
            Deflate = 8,
            Jbig = 9,
            Jbig2Legacy = 0xA,
            NextRle = 0x7FFE,
            PackBits = 0x8005,
            ThunderScanRle = 0x8029,
            RasterPadding = 0x807F,
            RleLineWork = 0x8080,
            RleHighRes = 0x8081,
            RleBinaryLineWork = 0x80B2,
            KodakDcs = 0x80B3,
            JbigLibTiff = 0x8765,
            Jpeg2000 = 0x8798,
            NikonNef = 0x8799,
            Jbig2 = 0x879B,
        }

        private readonly GeoTiffReader _reader;
        private float[]? _loadedImageData;

        public GeoTiffMetadata Metadata { get; }
        public LatLongArea Area => Metadata.Area!;
        public bool IsDetailLoaded { get; private set; }
        public string FilePath { get; }

        public GeoTiff(string filePath)
        {
            FilePath = filePath;
            _reader = new GeoTiffReader(filePath);
            var metadata = _reader.ReadMetaData() ?? throw new InvalidOperationException("Not a valid GeoTIFF file.");
            Metadata = metadata;
        }

        public (int X, int Y)? GetPixelAtLatLong(LatLong location)
        {
            if (_loadedImageData != null && Area.Contains(location))
            {
                var upperLeft = Area.UpperLeft;
                var diff = location.Subtracted(upperLeft);
                var pixelY = (int)MathF.Abs(diff.Latitude / Area.PixelSize.Y);
                var pixelX = (int)MathF.Abs(diff.Longitude / Area.PixelSize.X);

                if (pixelX >= Metadata.Width || pixelY >= Metadata.Height)
                {
                    throw new InvalidOperationException("Computed pixel is outside the loaded image bounds.");
                }

                return (pixelX, pixelY);
            }

            return null;
        }

        public float GetHeightAtLatLong(LatLong location)
        {
            var pixelPos = GetPixelAtLatLong(location);
            if (pixelPos == null)
            {
                return 0.0f;
            }

            return GetHeightAtPixel(pixelPos.Value.X, pixelPos.Value.Y);
        }

        public float GetHeightAtPixel(int pixelX, int pixelY)
        {
            if (_loadedImageData == null)
            {
                return 0.0f;
            }

            var pX = Math.Clamp(pixelX, 0, Metadata.Width - 1);
            var pY = Math.Clamp(pixelY, 0, Metadata.Height - 1);

            var pixelIndex = pY * Metadata.Width + pX;
            return _loadedImageData[pixelIndex];
        }

        public Vector3 GetVertexAtLatLong(LatLong latLong, float radius, float heightScale)
        {
            var pointOnUnitSphere = latLong.ToUnitSphere();
            var pixelPos = GetPixelAtLatLong(latLong);
            var height = pixelPos == null ? 0.0f : GetHeightAtPixel(pixelPos.Value.X, pixelPos.Value.Y);
            var scaledHeight = height * heightScale;
            return pointOnUnitSphere * (radius + scaledHeight);
        }

        public float[] LoadImageData()
        {
            var startTime = DateTime.UtcNow;

            _loadedImageData = _reader.ReadImage(Metadata);
            IsDetailLoaded = true;

            var elapsed = DateTime.UtcNow - startTime;
            Console.WriteLine($"[Tile]: {Area.UpperLeft.ToPrettyString()}\t\t{elapsed.TotalMilliseconds} ms");
            return _loadedImageData;
        }

        public void ReleaseImageData()
        {
            _loadedImageData = null;
            IsDetailLoaded = false;
        }

        public static string GetCompressionName(int compressionValue)
        {
            return Enum.IsDefined(typeof(GeoTiffCompression), compressionValue)
                ? ((GeoTiffCompression)compressionValue).ToString()
                : GeoTiffCompression.Unknown.ToString();
        }

        public static GeoTiffCompression GetCompression(int compressionValue)
        {
            return Enum.IsDefined(typeof(GeoTiffCompression), compressionValue)
                ? (GeoTiffCompression)compressionValue
                : GeoTiffCompression.Unknown;
        }
    }
}
