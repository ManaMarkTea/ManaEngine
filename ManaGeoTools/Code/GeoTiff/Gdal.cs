using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.GeoTiff
{
    public sealed class GdalItem
    {
        public string ItemName { get; }
        public string ItemValue { get; }

        public GdalItem(string name, string value)
        {
            ItemName = name;
            ItemValue = value;
        }
    }

    // Parses the GDAL_METADATA tag's XML blob (<GDALMetadata><Item name="...">value</Item>...</GDALMetadata>)
    // and, when present, resolves the DTED origin lat/long items into a LatLong.
    public sealed class Gdal
    {
        public List<GdalItem> Items { get; } = new();
        public LatLong? OriginLongLat { get; private set; }

        public Gdal(string xmlString)
        {
            var root = XElement.Parse(xmlString);
            foreach (var itemXml in root.Elements())
            {
                if (!itemXml.HasLocalAttribute("name"))
                {
                    continue;
                }

                var name = itemXml.LocalAttribute("name");
                var value = itemXml.Value;
                Items.Add(new GdalItem(name, value));
            }

            string? longString = null;
            string? latString = null;
            foreach (var item in Items)
            {
                if (item.ItemName == "DTED_OriginLatitude")
                {
                    latString = item.ItemValue;
                }
                if (item.ItemName == "DTED_OriginLongitude")
                {
                    longString = item.ItemValue;
                }
            }

            if (!string.IsNullOrEmpty(longString) && !string.IsNullOrEmpty(latString))
            {
                OriginLongLat = LatLong.FromDted(longString, latString);
            }
        }
    }
}
