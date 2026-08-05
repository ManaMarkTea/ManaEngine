namespace ManaGeoTools.Code.GeoTiff
{
    public enum GeoTiffFieldType
    {
        Byte = 1,
        Ascii = 2,
        Short = 3,
        Long = 4,
        Rational = 5,
        SByte = 6,
        Undefined = 7,
        SShort = 8,
        SLong = 9,
        SRational = 10,
        Float = 11,
        Double = 12,
    }

    public sealed class GeoTiffTag
    {
        public string Name { get; }
        public GeoTiffFieldType Type { get; }
        public ushort Id { get; }

        public GeoTiffTag(string name, GeoTiffFieldType type, ushort id)
        {
            Name = name;
            Type = type;
            Id = id;
        }

        // Known TIFF, GeoTIFF, and GDAL tag ids used by this reader.
        public static readonly IReadOnlyDictionary<ushort, GeoTiffTag> Tags = new Dictionary<ushort, GeoTiffTag>
        {
            [256] = new GeoTiffTag("ImageWidth", GeoTiffFieldType.Long, 256),
            [257] = new GeoTiffTag("ImageLength", GeoTiffFieldType.Long, 257),
            [258] = new GeoTiffTag("BitsPerSample", GeoTiffFieldType.Short, 258),
            [259] = new GeoTiffTag("Compression", GeoTiffFieldType.Short, 259),
            [262] = new GeoTiffTag("PhotometricInterpretation", GeoTiffFieldType.Short, 262),
            [273] = new GeoTiffTag("StripOffsets", GeoTiffFieldType.Long, 273),
            [274] = new GeoTiffTag("Orientation", GeoTiffFieldType.Short, 274),
            [277] = new GeoTiffTag("SamplesPerPixel", GeoTiffFieldType.Short, 277),
            [278] = new GeoTiffTag("RowsPerStrip", GeoTiffFieldType.Long, 278),
            [279] = new GeoTiffTag("StripByteCounts", GeoTiffFieldType.Long, 279),
            [282] = new GeoTiffTag("XResolution", GeoTiffFieldType.Rational, 282),
            [283] = new GeoTiffTag("YResolution", GeoTiffFieldType.Rational, 283),
            [284] = new GeoTiffTag("PlanarConfiguration", GeoTiffFieldType.Short, 284),
            [296] = new GeoTiffTag("ResolutionUnit", GeoTiffFieldType.Short, 296),
            [317] = new GeoTiffTag("Predictor", GeoTiffFieldType.Short, 317),
            [322] = new GeoTiffTag("TileWidth", GeoTiffFieldType.Short, 322),
            [323] = new GeoTiffTag("TileLength", GeoTiffFieldType.Short, 323),
            [324] = new GeoTiffTag("TileOffsets", GeoTiffFieldType.Long, 324),
            [325] = new GeoTiffTag("TileByteCounts", GeoTiffFieldType.Long, 325),
            [270] = new GeoTiffTag("ImageDescription", GeoTiffFieldType.Ascii, 270),
            [271] = new GeoTiffTag("Make", GeoTiffFieldType.Ascii, 271),
            [272] = new GeoTiffTag("Model", GeoTiffFieldType.Ascii, 272),
            [305] = new GeoTiffTag("Software", GeoTiffFieldType.Ascii, 305),
            [306] = new GeoTiffTag("DateTime", GeoTiffFieldType.Ascii, 306),
            [315] = new GeoTiffTag("Artist", GeoTiffFieldType.Ascii, 315),
            [316] = new GeoTiffTag("HostComputer", GeoTiffFieldType.Ascii, 316),
            [320] = new GeoTiffTag("ColorMap", GeoTiffFieldType.Short, 320),
            [339] = new GeoTiffTag("SampleFormat", GeoTiffFieldType.Short, 339),
            // GeoTIFF tags
            [33550] = new GeoTiffTag("ModelPixelScaleTag", GeoTiffFieldType.Double, 33550),
            [33922] = new GeoTiffTag("ModelTiepointTag", GeoTiffFieldType.Double, 33922),
            [34264] = new GeoTiffTag("ModelTransformationTag", GeoTiffFieldType.Double, 34264),
            [34735] = new GeoTiffTag("GeoKeyDirectoryTag", GeoTiffFieldType.Short, 34735),
            [34736] = new GeoTiffTag("GeoDoubleParamsTag", GeoTiffFieldType.Double, 34736),
            [34737] = new GeoTiffTag("GeoAsciiParamsTag", GeoTiffFieldType.Ascii, 34737),
            // GDAL tags
            [42112] = new GeoTiffTag("GDAL_METADATA", GeoTiffFieldType.Ascii, 42112),
            [42113] = new GeoTiffTag("GDAL_NODATA", GeoTiffFieldType.Ascii, 42113),
        };
    }
}
