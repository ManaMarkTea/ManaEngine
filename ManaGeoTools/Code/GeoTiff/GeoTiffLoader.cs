using Mana.Code;
using Mana.Code.Nodes;

namespace ManaGeoTools.Code.GeoTiff
{
    public static class GeoTiffLoader
    {
        public static ManaMeshSceneNode CreateTerrainNode(
            ManaContext context, GeoTiff heightMap, int terrainWidth, int terrainLength, float maxHeight)
        {
            var mesh = new GeoTiffTerrainMesh(context, heightMap, terrainWidth, terrainLength, maxHeight);
            var node = new ManaMeshSceneNode(context, mesh) { Name = "GeoTiff Terrain" };
            return node;
        }
    }
}
