using System.Xml.Linq;

namespace ManaGeoTools.Code.Osm
{
    /// <summary>
    /// https://www.overpass-api.de/api/xapi?node[natural=peak][bbox=-112.2915,31.01788,-110.83008,33.32135]
    /// Pure data parsing — the source has no OSM visualization either.
    /// </summary>
    public sealed class Osm
    {
        public string FilePath { get; }

        public List<OsmNode> Nodes { get; } = new();
        public List<OsmWay> Ways { get; } = new();
        public Dictionary<string, OsmNode> NodeMap { get; } = new();
        public Dictionary<string, OsmWay> WayMap { get; } = new();

        public Osm(string filePath)
        {
            FilePath = filePath;
            ParseXml();
        }

        private void ParseXml()
        {
            Console.WriteLine("[Osm][Parsing] " + FilePath);

            XDocument xml;
            try
            {
                xml = XDocument.Load(FilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("No XML: " + ex.Message);
                return;
            }

            var root = xml.Root;
            if (root == null)
            {
                Console.WriteLine("No XML");
                return;
            }

            // Nodes must be fully parsed first so Ways can resolve <nd ref> against NodeMap.
            foreach (var nodeElement in root.Elements())
            {
                if (nodeElement.Name.LocalName != "node" || nodeElement.Attribute("id") == null)
                {
                    continue;
                }

                var node = new OsmNode(nodeElement);
                Nodes.Add(node);
                NodeMap[node.Id] = node;
            }

            foreach (var nodeElement in root.Elements())
            {
                if (nodeElement.Name.LocalName != "way" || nodeElement.Attribute("id") == null)
                {
                    continue;
                }

                var way = new OsmWay(NodeMap, nodeElement);
                Ways.Add(way);
                WayMap[way.Id] = way;
            }

            Console.WriteLine("[Osm] Done");
        }
    }
}
