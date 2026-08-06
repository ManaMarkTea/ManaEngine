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
        /// <summary>
        /// maxPlacemarks, if given, stops after that many placemarks total across all folders — each
        /// mesh node is a separate draw call with no culling/LOD/batching yet, so an unbounded KML
        /// (real-world airspace/OSM-derived files routinely have thousands of placemarks) can produce
        /// enough scene nodes to make the per-frame scene walk and draw call count the bottleneck.
        /// </summary>
        public static ManaSceneNode BuildPlacemarks(ManaContext context, Kml kml, int? maxPlacemarks = null)
        {
            var root = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = Path.GetFileNameWithoutExtension(kml.FilePath),
            };

            var placemarksAdded = 0;
            foreach (var folder in kml.Folders)
            {
                if (maxPlacemarks.HasValue && placemarksAdded >= maxPlacemarks.Value)
                {
                    break;
                }

                var folderNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = folder.Name };
                foreach (var placemark in folder.PlaceMarks)
                {
                    if (maxPlacemarks.HasValue && placemarksAdded >= maxPlacemarks.Value)
                    {
                        break;
                    }

                    AddPlacemark(context, folderNode, placemark);
                    placemarksAdded++;
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
