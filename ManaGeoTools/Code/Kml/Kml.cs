using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    /// <summary>
    /// https://3dairspace.org.uk/
    /// </summary>
    public sealed class Kml
    {
        // Default is in meters, used to compute center points in xyz from lat/long.
        public static float EarthRadius = 6371e3f;

        // Default is 1 to 1, multiplies the altitude by this amount for xyz from lat/long.
        public static float AltitudeScale = 1.0f;

        public string FilePath { get; }
        public string Description { get; private set; } = string.Empty;

        public List<KmlStyle> Styles { get; } = new();
        public Dictionary<string, KmlStyle> StyleMap { get; } = new();

        public List<KmlFolder> Folders { get; } = new();
        public Dictionary<string, KmlFolder> FolderMap { get; } = new();

        public List<KmlSchema> Schema { get; } = new();
        public Dictionary<string, KmlSchema> SchemaMap { get; } = new();

        public Kml(string kmlFilePath, float earthRadius = 6371e3f, float altitudeScale = 1.0f)
        {
            FilePath = kmlFilePath;
            EarthRadius = earthRadius;
            AltitudeScale = altitudeScale;
            ParseXml();
        }

        private void ParseXml()
        {
            Console.WriteLine("[Kml][Parsing] " + FilePath);

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

            // Real-world KML almost always wraps content in a <Document>, but some exports omit it —
            // fall back to the root element itself when there's no Document wrapper.
            var document = root.LocalElement("Document") ?? root;

            foreach (var nodeElement in document.Elements())
            {
                switch (nodeElement.Name.LocalName)
                {
                    case "description":
                        Description = nodeElement.Value;
                        break;
                    case "Style":
                        {
                            var style = new KmlStyle(nodeElement);
                            Styles.Add(style);
                            StyleMap["#" + style.Id] = style;
                            break;
                        }
                    case "Schema":
                        {
                            var schema = new KmlSchema(nodeElement);
                            Schema.Add(schema);
                            SchemaMap["#" + schema.Id] = schema;
                            break;
                        }
                    case "Folder":
                        {
                            var folder = new KmlFolder(nodeElement);
                            Folders.Add(folder);
                            FolderMap["#" + folder.Name] = folder;
                            break;
                        }
                }
            }

            Console.WriteLine("[Kml] Done");
        }
    }
}
