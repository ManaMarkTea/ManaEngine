using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Osm
{
    public sealed class OsmWay : OsmElement
    {
        public List<OsmNode> NodeRefs { get; } = new();

        public OsmWay(IReadOnlyDictionary<string, OsmNode> nodeMap, XElement element) : base(element)
        {
            OsmNode? previousNode = null;

            foreach (var childXml in element.LocalElements("nd"))
            {
                if (!childXml.HasLocalAttribute("ref"))
                {
                    continue;
                }

                var refId = childXml.LocalAttribute("ref");
                if (!nodeMap.TryGetValue(refId, out var node))
                {
                    continue;
                }

                if (previousNode != null)
                {
                    previousNode.Connections.Add(node);
                    node.Connections.Add(previousNode);
                }

                NodeRefs.Add(node);
                previousNode = node;
            }
        }
    }
}
