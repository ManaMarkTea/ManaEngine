using System.Xml.Linq;
using Mana.Code;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Kml.Geometry
{
    public abstract class KmlGeometry : KmlElement
    {
        protected KmlGeometry(XElement element) : base(element)
        {
        }

        public static KmlGeometry? Create(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "Point":
                    return new KmlPoint(element);
                case "LineString":
                    return new KmlLineString(element);
                case "LinearRing":
                    return new KmlLinearRing(element);
                case "Polygon":
                    return new KmlPolygon(element);
                case "MultiGeometry":
                    return new KmlMultiGeometry(element);
                default:
                    // "Model" (and anything else) falls through to null, matching the source — no
                    // Model geometry class exists there either.
                    return null;
            }
        }

        public virtual LatLong GetCenterPoint()
        {
            return LatLong.FromLongitudeAndLatitude(0, 0);
        }

        // Replaces the source's `AddGeometry(Kml, Array[MeshInstance3D])`, which mutated a Godot
        // node list. Returns a mesh instead; null means "no renderable geometry" (Point, empty data).
        public virtual ManaMesh? BuildMesh(ManaContext context)
        {
            return null;
        }

        public virtual ManaMesh? BuildMesh(ManaContext context, KmlStyle? style)
        {
            return BuildMesh(context);
        }
    }
}
