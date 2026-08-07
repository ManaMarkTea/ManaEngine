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
        private const int MaxKmlLineBatchIndexes = 500_000;

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
                IncludeInEnvironmentCapture = false,
            };

            var placemarksAdded = 0;
            foreach (var folder in kml.Folders)
            {
                if (maxPlacemarks.HasValue && placemarksAdded >= maxPlacemarks.Value)
                {
                    break;
                }

                var folderNode = new ManaSceneNode(context, Matrix4x4.Identity)
                {
                    Name = folder.Name,
                    IncludeInEnvironmentCapture = false,
                };
                var lineBatches = new List<KmlLineBatch>();
                foreach (var placemark in folder.PlaceMarks)
                {
                    if (maxPlacemarks.HasValue && placemarksAdded >= maxPlacemarks.Value)
                    {
                        break;
                    }

                    AddPlacemark(context, kml, placemark, folderNode, lineBatches);
                    placemarksAdded++;
                }

                AddLineBatchNodes(context, folderNode, folder.Name, lineBatches);
                if (folderNode.Children.Count > 0)
                {
                    root.AddChild(folderNode);
                }
            }

            return root;
        }

        private static void AddPlacemark(ManaContext context, Kml kml, KmlPlacemark placemark, ManaSceneNode folderNode, List<KmlLineBatch> lineBatches)
        {
            var style = ResolveStyle(kml, placemark);
            var addedAny = CollectLines(placemark.Geometry, style, lineBatches);
            if (!addedAny)
            {
                // Point / LineString-that-failed-to-build / empty MultiGeometry placemarks still get a
                // plain, mesh-less node so the scene tree carries their Name/Coordinates/ExtendedData
                // for inspection -- same convention this builder used before line rendering moved to
                // batched ManaLineMesh output; that batching only covers geometry that produces an
                // actual line, so it silently dropped everything else without this fallback.
                var emptyNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = placemark.Name };
                folderNode.AddChild(emptyNode);
            }
        }

        private static bool CollectLines(KmlGeometry? geometry, KmlStyle? style, List<KmlLineBatch> lineBatches)
        {
            switch (geometry)
            {
                case null:
                    return false;
                case KmlMultiGeometry multi:
                    var addedAny = false;
                    foreach (var child in multi.Geometries)
                    {
                        addedAny |= CollectLines(child, style, lineBatches);
                    }
                    return addedAny;
                case KmlLineString lineString:
                    return AddLine(lineString.Coordinates.Data, closed: false, style, lineBatches);
                case KmlLinearRing linearRing:
                    return AddLine(linearRing.Coordinates.Data, closed: true, style, lineBatches);
                case KmlPolygon polygon:
                    var added = AddLine(polygon.OuterBoundary.Coordinates.Data, closed: true, style, lineBatches);
                    foreach (var innerBoundary in polygon.InnerBoundaries)
                    {
                        added |= AddLine(innerBoundary.Coordinates.Data, closed: true, style, lineBatches);
                    }
                    return added;
                default:
                    return false;
            }
        }

        private static bool AddLine(
            IReadOnlyList<Vector3> points,
            bool closed,
            KmlStyle? style,
            List<KmlLineBatch> lineBatches)
        {
            if (!KmlLineMeshFactory.TryCreatePolyline(points, closed, out var line, out var estimatedIndexCount))
            {
                return false;
            }

            var renderStyle = KmlLineMeshFactory.ResolveRenderStyle(style);
            foreach (var batch in lineBatches)
            {
                if (batch.Style.Equals(renderStyle) &&
                    batch.EstimatedIndexCount + estimatedIndexCount <= MaxKmlLineBatchIndexes)
                {
                    batch.Lines.Add(line);
                    batch.EstimatedIndexCount += estimatedIndexCount;
                    return true;
                }
            }

            lineBatches.Add(new KmlLineBatch(renderStyle, line, estimatedIndexCount));
            return true;
        }

        private static void AddLineBatchNodes(
            ManaContext context,
            ManaSceneNode parent,
            string folderName,
            IReadOnlyList<KmlLineBatch> lineBatches)
        {
            for (var i = 0; i < lineBatches.Count; i++)
            {
                var batch = lineBatches[i];
                if (batch.Lines.Count == 0)
                {
                    continue;
                }

                var mesh = new ManaLineMesh(
                    context,
                    batch.Lines,
                    batch.Style.Thickness,
                    batch.Style.Color,
                    batch.Style.EmissionStrength,
                    ManaLineProfile.Ribbon);
                var node = new ManaMeshSceneNode(context, mesh)
                {
                    Name = $"{folderName} Lines {i + 1}",
                    IncludeInEnvironmentCapture = false,
                };
                parent.AddChild(node);
            }
        }

        private static KmlStyle? ResolveStyle(Kml kml, KmlPlacemark placemark)
        {
            return !string.IsNullOrWhiteSpace(placemark.StyleUrl) && kml.StyleMap.TryGetValue(placemark.StyleUrl, out var style)
                ? style
                : null;
        }

        private sealed class KmlLineBatch
        {
            public KmlLineBatch(KmlLineMeshFactory.RenderStyle style, ManaLinePolyline line, int estimatedIndexCount)
            {
                Style = style;
                Lines = new List<ManaLinePolyline> { line };
                EstimatedIndexCount = estimatedIndexCount;
            }

            public KmlLineMeshFactory.RenderStyle Style { get; }
            public List<ManaLinePolyline> Lines { get; }
            public int EstimatedIndexCount { get; set; }
        }
    }
}
