using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Kml.Geometry;

namespace ManaGeoTools.Code.Kml
{
    /// <summary>
    /// "Load earth data via nodes" entry point for KML — mirrors ManaGltfLoader's contract of
    /// returning a root ManaSceneNode that the caller AddChild()s into their own scene.
    /// </summary>
    public static class KmlSceneBuilder
    {
        public static ManaSceneNode BuildPlacemarks(ManaContext context, Kml kml)
        {
            var root = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = Path.GetFileNameWithoutExtension(kml.FilePath),
            };

            foreach (var folder in kml.Folders)
            {
                var folderNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = folder.Name };
                foreach (var placemark in folder.PlaceMarks)
                {
                    AddPlacemark(context, folderNode, placemark);
                }
                root.AddChild(folderNode);
            }

            return root;
        }

        private static void AddPlacemark(ManaContext context, ManaSceneNode parent, KmlPlacemark placemark)
        {
            var meshes = CollectMeshes(context, placemark.Geometry);
            if (meshes.Count == 0)
            {
                // Point / LineString / empty MultiGeometry placemarks still get a plain, mesh-less
                // node so the scene tree carries their Name/Coordinates/ExtendedData for inspection.
                var emptyNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = placemark.Name };
                parent.AddChild(emptyNode);
                return;
            }

            foreach (var mesh in meshes)
            {
                var node = new ManaMeshSceneNode(context, mesh) { Name = placemark.Name };
                parent.AddChild(node);
            }
        }

        private static List<ManaMesh> CollectMeshes(ManaContext context, KmlGeometry? geometry)
        {
            var output = new List<ManaMesh>();
            switch (geometry)
            {
                case null:
                    break;
                case KmlMultiGeometry multi:
                    foreach (var child in multi.Geometries)
                    {
                        output.AddRange(CollectMeshes(context, child));
                    }
                    break;
                default:
                    var mesh = geometry.BuildMesh(context);
                    if (mesh != null)
                    {
                        output.Add(mesh);
                    }
                    break;
            }
            return output;
        }
    }
}
