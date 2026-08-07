namespace ManaGeoTools.Code.GeoTiff
{
    /// <summary>
    /// A single height-data tile covering some lat/long area, usable by GeoTiffLibrary regardless of
    /// where it actually came from -- GeoTiff (a real decoded TIFF file) and TerrariumHeightTile (a
    /// decoded Web Mercator PNG tile fetched on demand, see Terrarium/TerrariumHeightTile.cs) both
    /// implement this, so GeoTiffLibrary's existing FindCurrentTile/height-lookup consumers work
    /// unchanged regardless of which kind of tile actually answers a given query.
    /// </summary>
    public interface IHeightTile
    {
        LatLongArea Area { get; }

        float GetHeightAtLatLong(LatLong location);
    }
}
