namespace ManaGeoTools.Code.GeoTiff
{
    // Holds the parsed TIFF/GeoTIFF/GDAL tags for a single file.
    public sealed class GeoTiffMetadata
    {
        public List<GeoTiffTag> Tags { get; } = new();
        public LatLongArea? Area { get; private set; }
        public Gdal? GdalData { get; private set; }

        public int Width;
        public int Height;
        public int BitsPerSample;
        public int Compression;
        public int PhotometricInterpretation;
        public int[] StripOffsets = Array.Empty<int>();
        public int Orientation;
        public int SamplesPerPixel;
        public int RowsPerStrip;
        public int[] StripByteCounts = Array.Empty<int>();
        public double XResolution;
        public double YResolution;
        public int PlanarConfiguration;
        public int ResolutionUnit;
        public string ImageDescription = string.Empty;
        public string Make = string.Empty;
        public string Model = string.Empty;
        public string Software = string.Empty;
        public string DateTime = string.Empty;
        public string Artist = string.Empty;
        public string HostComputer = string.Empty;
        public List<int> ColorMap { get; } = new();
        public double[] ModelPixelScale = Array.Empty<double>();
        public double[] ModelTiepoint = Array.Empty<double>();
        public double[] ModelTransformation = Array.Empty<double>();
        public ushort[] GeoKeyDirectory = Array.Empty<ushort>();
        public double[] GeoDoubleParams = Array.Empty<double>();
        public string GeoAsciiParams = string.Empty;
        public string GdalXml = string.Empty;

        // Tiled-TIFF layout (e.g. ETOPO 2022's per-tile GeoTIFFs). Absent (Width/Length == 0) means
        // the file is strip-organized instead — see StripOffsets/StripByteCounts/RowsPerStrip above.
        public int TileWidth;
        public int TileLength;
        public int[] TileOffsets = Array.Empty<int>();
        public int[] TileByteCounts = Array.Empty<int>();
        public bool IsTiled => TileOffsets.Length > 0;

        // TIFF Predictor tag (317): 1 = None (default when absent), 2 = horizontal integer
        // differencing, 3 = floating point predictor (what ETOPO 2022's compressed float tiles use).
        public int Predictor = 1;

        // TIFF SampleFormat tag (339): 1 = unsigned int (default when absent, e.g. the SRTM tiles),
        // 2 = signed int, 3 = IEEE float (e.g. ETOPO 2022).
        public int SampleFormat = 1;

        public void ParseGdal()
        {
            GdalData = new Gdal(GdalXml);

            // Not every GDAL_METADATA blob carries the DTED_Origin* items this addresses (e.g. ETOPO
            // 2022's GDAL_METADATA has neither) — in that case TryComputeAreaFromModelTiepoint(),
            // called once the whole IFD has been read, is the fallback that computes Area instead.
            if (GdalData.OriginLongLat == null || ModelPixelScale.Length < 2)
            {
                return;
            }

            var latLongPerPixel = new Vector3((float)ModelPixelScale[0], (float)ModelPixelScale[1], 0.0f);
            Area = new LatLongArea(GdalData.OriginLongLat, Width, Height, latLongPerPixel);
            Area.PixelSize = new Vector2((float)ModelPixelScale[0], (float)ModelPixelScale[1]);
        }

        // Standard (non-GDAL-specific) GeoTIFF georeferencing: ModelTiepointTag ties a raster pixel
        // (conventionally (0,0), the upper-left corner) to a model-space (longitude, latitude), and
        // ModelPixelScaleTag gives degrees-per-pixel. Most real-world GeoTIFFs (including ETOPO 2022)
        // carry these instead of the GDAL_METADATA/DTED-origin convention the SRTM tiles use. Called
        // as a fallback after the IFD is fully read, only if GDAL parsing didn't already set Area.
        public void TryComputeAreaFromModelTiepoint()
        {
            if (Area != null || ModelPixelScale.Length < 2 || ModelTiepoint.Length < 6)
            {
                return;
            }

            var pixelScaleX = (float)ModelPixelScale[0];
            var pixelScaleY = (float)ModelPixelScale[1];
            var upperLeftLongitude = (float)ModelTiepoint[3];
            var upperLeftLatitude = (float)ModelTiepoint[4];

            var lowerLeft = new LatLong(upperLeftLongitude, upperLeftLatitude - (Height * pixelScaleY));
            Area = new LatLongArea(lowerLeft, Width, Height, new Vector3(pixelScaleX, pixelScaleY, 0f));
            Area.PixelSize = new Vector2(pixelScaleX, pixelScaleY);
        }

        // valueOffset is the raw 4-byte IFD "value/offset" field. For scalar tags that fit in
        // 4 bytes it *is* the value; for array/string tags it is a byte offset to seek to and
        // read numValues entries from. This mirrors the source reader's simplification of not
        // implementing full TIFF left-justified-short packing — preserved intentionally.
        public void ReadTagValue(Stream stream, bool bigEndian, GeoTiffTag tag, int fieldType, int numValues, uint valueOffset)
        {
            Tags.Add(tag);

            switch (tag.Name)
            {
                case "ImageWidth":
                    Width = (int)valueOffset;
                    break;
                case "ImageLength":
                    Height = (int)valueOffset;
                    break;
                case "BitsPerSample":
                    BitsPerSample = (int)valueOffset;
                    break;
                case "Compression":
                    Compression = (int)valueOffset;
                    break;
                case "PhotometricInterpretation":
                    PhotometricInterpretation = (int)valueOffset;
                    break;
                case "StripOffsets":
                    StripOffsets = GeoTiffReader.ReadIntArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "Orientation":
                    Orientation = (int)valueOffset;
                    break;
                case "SamplesPerPixel":
                    SamplesPerPixel = (int)valueOffset;
                    break;
                case "RowsPerStrip":
                    RowsPerStrip = (int)valueOffset;
                    break;
                case "StripByteCounts":
                    StripByteCounts = GeoTiffReader.ReadIntArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "SampleFormat":
                    SampleFormat = (int)valueOffset;
                    break;
                case "Predictor":
                    Predictor = (int)valueOffset;
                    break;
                case "TileWidth":
                    TileWidth = (int)valueOffset;
                    break;
                case "TileLength":
                    TileLength = (int)valueOffset;
                    break;
                case "TileOffsets":
                    TileOffsets = GeoTiffReader.ReadIntArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "TileByteCounts":
                    TileByteCounts = GeoTiffReader.ReadIntArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "XResolution":
                    XResolution = valueOffset;
                    break;
                case "YResolution":
                    YResolution = valueOffset;
                    break;
                case "PlanarConfiguration":
                    PlanarConfiguration = (int)valueOffset;
                    break;
                case "ResolutionUnit":
                    ResolutionUnit = (int)valueOffset;
                    break;
                case "ImageDescription":
                    ImageDescription = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "Make":
                    Make = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "Model":
                    Model = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "Software":
                    Software = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "DateTime":
                    DateTime = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "Artist":
                    Artist = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "HostComputer":
                    HostComputer = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "ColorMap":
                    ColorMap.Add((int)valueOffset);
                    break;
                case "ModelPixelScaleTag":
                    ModelPixelScale = GeoTiffReader.ReadDoubleArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "ModelTiepointTag":
                    ModelTiepoint = GeoTiffReader.ReadDoubleArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "ModelTransformationTag":
                    ModelTransformation = GeoTiffReader.ReadDoubleArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "GeoKeyDirectoryTag":
                    GeoKeyDirectory = GeoTiffReader.ReadShortArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "GeoDoubleParamsTag":
                    GeoDoubleParams = GeoTiffReader.ReadDoubleArray(stream, bigEndian, valueOffset, numValues);
                    break;
                case "GeoAsciiParamsTag":
                    GeoAsciiParams = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                case "GDAL_METADATA":
                    GdalXml = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    ParseGdal();
                    break;
                case "GDAL_NODATA":
                    GdalXml = GeoTiffReader.ReadAsciiString(stream, valueOffset, numValues);
                    break;
                default:
                    Console.WriteLine($"[GeoTiff] Unhandled tag {tag.Name} = {valueOffset}");
                    break;
            }
        }
    }
}
