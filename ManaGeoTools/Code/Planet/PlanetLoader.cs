using Mana.Code;
using Mana.Code.Nodes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    public static class PlanetLoader
    {
        public const float EarthRadiusMeters = 6371000f;

        /// <summary>
        /// "Place a planet node" entry point — mirrors GeoTiffLoader.CreateTerrainNode. Default
        /// radius (100 world units) matches the source addon's own scaled-down EARTH_RADIUS
        /// convention (real Earth ~6371km scaled to 100), chosen to fit this engine's existing scale
        /// (camera far clip plane 1000, default fly speed 200 units/sec).
        /// </summary>
        public static ManaMeshSceneNode CreatePlanetNode(
            ManaContext context,
            float radius = 100f,
            int resolution = 32,
            GeoTiffLibrary? heightLibrary = null,
            float heightExaggeration = 1f)
        {
            var heightScale = (radius / EarthRadiusMeters) * heightExaggeration;
            var mesh = new PlanetMesh(context, radius, resolution, heightScale, heightLibrary);
            return new ManaMeshSceneNode(context, mesh) { Name = "Planet" };
        }
    }
}
